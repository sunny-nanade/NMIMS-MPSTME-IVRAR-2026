from __future__ import annotations

import math
import random
from dataclasses import dataclass

import numpy as np
import torch
from torch import nn


class DuelingQNetwork(nn.Module):
    def __init__(self, terrain_channels: int = 5, state_dim: int = 14, n_actions: int = 9):
        super().__init__()
        self.cnn = nn.Sequential(
            nn.Conv2d(terrain_channels, 32, 5, stride=2, padding=2), nn.ReLU(),
            nn.Conv2d(32, 64, 3, stride=2, padding=1), nn.ReLU(),
            nn.Conv2d(64, 96, 3, stride=2, padding=1), nn.ReLU(),
            nn.AdaptiveAvgPool2d((2, 2)), nn.Flatten(),
            nn.Linear(96 * 2 * 2, 192), nn.ReLU(),
        )
        self.state = nn.Sequential(
            nn.Linear(state_dim, 64), nn.ReLU(), nn.Linear(64, 64), nn.ReLU()
        )
        self.shared = nn.Sequential(nn.Linear(256, 256), nn.ReLU())
        self.value = nn.Sequential(nn.Linear(256, 128), nn.ReLU(), nn.Linear(128, 1))
        self.advantage = nn.Sequential(nn.Linear(256, 128), nn.ReLU(), nn.Linear(128, n_actions))

    def forward(self, terrain, state):
        if terrain.dtype == torch.uint8:
            terrain = terrain.float() / 255.0
        else:
            terrain = terrain.float()
            if terrain.max().detach().item() > 1.5:
                terrain = terrain / 255.0
        x = self.shared(torch.cat([self.cnn(terrain), self.state(state.float())], dim=1))
        v = self.value(x)
        a = self.advantage(x)
        return v + a - a.mean(dim=1, keepdim=True)


class SumTree:
    """O(log N) proportional-priority tree."""
    def __init__(self, capacity: int):
        self.capacity = int(capacity)
        self.size = 1
        while self.size < self.capacity:
            self.size *= 2
        self.tree = np.zeros(2 * self.size, dtype=np.float64)

    @property
    def total(self):
        return float(self.tree[1])

    def update(self, idx: int, value: float):
        node = self.size + int(idx)
        delta = float(value) - self.tree[node]
        while node >= 1:
            self.tree[node] += delta
            node //= 2

    def find_prefix(self, mass: float) -> int:
        node = 1
        mass = min(max(float(mass), 0.0), max(self.total - 1e-12, 0.0))
        while node < self.size:
            left = node * 2
            if mass <= self.tree[left]:
                node = left
            else:
                mass -= self.tree[left]
                node = left + 1
        return min(node - self.size, self.capacity - 1)


class PrioritizedReplayBuffer:
    """Memory-efficient PER: terrain frames stay uint8; states remain float32."""
    def __init__(self, capacity: int, terrain_shape=(5, 32, 32), state_dim=14, alpha=0.6):
        self.capacity = int(capacity)
        self.alpha = float(alpha)
        self.terrain = np.empty((capacity, *terrain_shape), dtype=np.uint8)
        self.next_terrain = np.empty((capacity, *terrain_shape), dtype=np.uint8)
        self.state = np.empty((capacity, state_dim), dtype=np.float32)
        self.next_state = np.empty((capacity, state_dim), dtype=np.float32)
        self.action = np.empty(capacity, dtype=np.int64)
        self.reward = np.empty(capacity, dtype=np.float32)
        self.done = np.empty(capacity, dtype=np.float32)
        self.tree = SumTree(capacity)
        self.pos = 0
        self.length = 0
        self.max_priority = 1.0

    def __len__(self):
        return self.length

    def add(self, obs, action, reward, next_obs, done):
        i = self.pos
        self.terrain[i] = obs["terrain"]
        self.state[i] = obs["state"]
        self.next_terrain[i] = next_obs["terrain"]
        self.next_state[i] = next_obs["state"]
        self.action[i] = int(action)
        self.reward[i] = float(reward)
        self.done[i] = float(done)
        self.tree.update(i, self.max_priority ** self.alpha)
        self.pos = (self.pos + 1) % self.capacity
        self.length = min(self.length + 1, self.capacity)

    def sample(self, batch_size: int, beta: float, device: torch.device):
        total = self.tree.total
        if total <= 0 or self.length == 0:
            raise RuntimeError("Cannot sample an empty replay buffer")
        segment = total / batch_size
        idxs = []
        probs = []
        for k in range(batch_size):
            lo, hi = segment * k, segment * (k + 1)
            idx = self.tree.find_prefix(random.uniform(lo, hi))
            # Tree may point to an unwritten leaf before buffer has wrapped.
            if idx >= self.length:
                idx = random.randrange(self.length)
            idxs.append(idx)
            leaf = self.tree.tree[self.tree.size + idx]
            probs.append(max(leaf / total, 1e-12))
        idxs = np.asarray(idxs, dtype=np.int64)
        probs = np.asarray(probs, dtype=np.float64)
        weights = (self.length * probs) ** (-float(beta))
        weights /= max(weights.max(), 1e-12)

        batch = {
            "terrain": torch.as_tensor(self.terrain[idxs], device=device),
            "state": torch.as_tensor(self.state[idxs], device=device),
            "next_terrain": torch.as_tensor(self.next_terrain[idxs], device=device),
            "next_state": torch.as_tensor(self.next_state[idxs], device=device),
            "action": torch.as_tensor(self.action[idxs], device=device).long(),
            "reward": torch.as_tensor(self.reward[idxs], device=device),
            "done": torch.as_tensor(self.done[idxs], device=device),
            "weights": torch.as_tensor(weights, device=device, dtype=torch.float32),
            "indices": idxs,
        }
        return batch

    def update_priorities(self, indices, priorities):
        for idx, prio in zip(indices, priorities):
            p = max(float(prio), 1e-5)
            self.max_priority = max(self.max_priority, p)
            self.tree.update(int(idx), p ** self.alpha)


@dataclass
class LinearSchedule:
    start: float
    end: float
    duration: int

    def value(self, step: int) -> float:
        f = min(max(step, 0) / max(1, self.duration), 1.0)
        return self.start + f * (self.end - self.start)
