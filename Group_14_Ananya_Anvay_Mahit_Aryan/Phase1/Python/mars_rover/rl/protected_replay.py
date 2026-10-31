from __future__ import annotations

from pathlib import Path
import time
from typing import Any

import numpy as np
import torch
from stable_baselines3.common.buffers import DictReplayBuffer


def _one_env_obs(obs: dict[str, np.ndarray], i: int) -> dict[str, np.ndarray]:
    return {k: np.array(v[i], copy=True) for k, v in obs.items()}


class ProtectedDemoDictReplayBuffer(DictReplayBuffer):
    """FIFO replay plus a protected buffer containing only good experience."""

    def __init__(
        self,
        *args,
        demo_capacity: int = 30_000,
        demo_fraction: float = 0.25,
        protect_near_success: bool = True,
        near_success_route_progress: float = 0.80,
        **kwargs,
    ):
        super().__init__(*args, **kwargs)
        self.demo_capacity = int(demo_capacity)
        self.demo_fraction = float(np.clip(demo_fraction, 0.0, 0.50))
        self.protect_near_success = bool(protect_near_success)
        self.near_success_route_progress = float(near_success_route_progress)

        self.protected = DictReplayBuffer(
            buffer_size=self.demo_capacity,
            observation_space=self.observation_space,
            action_space=self.action_space,
            device=self.device,
            n_envs=1,
            optimize_memory_usage=False,
            handle_timeout_termination=True,
        )
        self._episode_cache: list[list[tuple[Any, ...]]] = [
            [] for _ in range(self.n_envs)
        ]
        self.teacher_demo_transitions = 0
        self.online_protected_episodes = 0
        self.online_protected_transitions = 0

    def set_demo_fraction(self, fraction: float) -> None:
        self.demo_fraction = float(np.clip(fraction, 0.0, 0.50))

    def protected_size(self) -> int:
        return int(self.protected.size())

    def _add_protected_transition(
        self,
        obs: dict[str, np.ndarray],
        next_obs: dict[str, np.ndarray],
        action: np.ndarray,
        reward: float,
        done: bool,
        info: dict | None = None,
        teacher: bool = False,
    ) -> None:
        o = {k: np.asarray(v)[None, ...] for k, v in obs.items()}
        no = {k: np.asarray(v)[None, ...] for k, v in next_obs.items()}
        a = np.asarray(action, dtype=np.float32).reshape(1, -1)
        r = np.asarray([reward], dtype=np.float32)
        d = np.asarray([done], dtype=np.float32)
        self.protected.add(o, no, a, r, d, [dict(info or {})])
        if teacher:
            self.teacher_demo_transitions += 1

    def add_teacher_transition(self, obs, next_obs, action, reward, done) -> None:
        self._add_protected_transition(
            obs, next_obs, action, reward, done, info={}, teacher=True
        )

    def seed_normal_from_teacher_npz(
        self, path: str | Path, max_items: int = 5_000
    ) -> int:
        """Prefill NORMAL vectorized replay without repeated NPZ decompression.

        NumPy ``.npz`` files are ZIP archives and ``NpzFile`` is lazy.  Indexing
        ``data["terrain"]`` repeatedly inside a loop can decompress that member
        repeatedly.  We therefore materialize each required array once, close the
        ZIP, and only then enter the replay insertion loop.

        The insertion remains vector-env correct: each parent add() receives
        exactly ``self.n_envs`` transitions.
        """
        path = Path(path)
        if not path.exists():
            raise FileNotFoundError(path)
        if self.n_envs < 1:
            raise RuntimeError(f"Invalid replay-buffer n_envs={self.n_envs}")

        t0 = time.perf_counter()
        with np.load(path, allow_pickle=False) as data:
            required = {
                "terrain", "state", "actions",
                "next_terrain", "next_state", "rewards", "dones",
            }
            missing = sorted(required - set(data.files))
            if missing:
                raise ValueError(f"Teacher demo file missing keys: {missing}")

            n_total = int(len(data["actions"]))
            n = min(int(max_items), n_total)
            if n <= 0:
                raise ValueError("Teacher demo dataset contains zero transitions")

            # Each ZIP member is decompressed exactly once here.
            terrain = np.asarray(data["terrain"], dtype=np.uint8)[:n].copy()
            state = np.asarray(data["state"], dtype=np.float32)[:n].copy()
            actions = np.asarray(data["actions"], dtype=np.float32)[:n].copy()
            next_terrain = np.asarray(data["next_terrain"], dtype=np.uint8)[:n].copy()
            next_state = np.asarray(data["next_state"], dtype=np.float32)[:n].copy()
            rewards = np.asarray(data["rewards"], dtype=np.float32)[:n].copy()
            dones = np.asarray(data["dones"], dtype=np.bool_)[:n].copy()

        logical_added = 0
        for base in range(0, n, self.n_envs):
            logical_count = min(self.n_envs, n - base)
            idx = np.asarray(
                [min(base + j, n - 1) for j in range(self.n_envs)],
                dtype=np.int64,
            )

            o = {"terrain": terrain[idx], "state": state[idx]}
            no = {"terrain": next_terrain[idx], "state": next_state[idx]}
            a = actions[idx]
            r = rewards[idx].reshape(self.n_envs)
            d = dones[idx].astype(np.float32, copy=False).reshape(self.n_envs)
            infos = [{} for _ in range(self.n_envs)]

            if a.shape != (self.n_envs, self.action_dim):
                raise RuntimeError(
                    "Teacher prefill action batch has wrong shape: "
                    f"got {a.shape}, expected {(self.n_envs, self.action_dim)}"
                )

            DictReplayBuffer.add(self, o, no, a, r, d, infos)
            logical_added += logical_count

        elapsed = time.perf_counter() - t0
        print(
            f"[teacher prefill] inserted {logical_added:,} transitions into "
            f"normal replay in {elapsed:.1f}s"
        )
        return int(logical_added)

    def load_teacher_npz(
        self, path: str | Path, max_items: int | None = None
    ) -> int:
        """Load teacher demos into PROTECTED replay efficiently.

        Critical performance detail:
        ``np.load(compressed_npz)`` is lazy.  The previous implementation indexed
        ``data["terrain"][i]`` and ``data["next_terrain"][i]`` inside a 20,000-step
        loop.  With a compressed NPZ, those expressions can repeatedly decompress
        the same large ZIP members, making startup appear frozen.

        This implementation eagerly materializes each required array ONCE, closes
        the NPZ, then performs only in-memory indexing during replay insertion.
        """
        path = Path(path)
        if not path.exists():
            raise FileNotFoundError(path)

        print(f"[protected] loading teacher dataset from {path} ...")
        t0 = time.perf_counter()

        with np.load(path, allow_pickle=False) as data:
            required = {
                "terrain", "state", "actions",
                "next_terrain", "next_state", "rewards", "dones",
            }
            missing = sorted(required - set(data.files))
            if missing:
                raise ValueError(f"Teacher demo file missing keys: {missing}")

            n_total = int(len(data["actions"]))
            n = n_total if max_items is None else min(n_total, int(max_items))
            if n <= 0:
                raise ValueError("Teacher demo dataset contains zero transitions")

            # Decompress each ZIP member exactly once.
            terrain = np.asarray(data["terrain"], dtype=np.uint8)[:n].copy()
            state = np.asarray(data["state"], dtype=np.float32)[:n].copy()
            actions = np.asarray(data["actions"], dtype=np.float32)[:n].copy()
            next_terrain = np.asarray(data["next_terrain"], dtype=np.uint8)[:n].copy()
            next_state = np.asarray(data["next_state"], dtype=np.float32)[:n].copy()
            rewards = np.asarray(data["rewards"], dtype=np.float32)[:n].copy()
            dones = np.asarray(data["dones"], dtype=np.bool_)[:n].copy()

        load_elapsed = time.perf_counter() - t0
        obs_mib = (terrain.nbytes + next_terrain.nbytes) / (1024 ** 2)
        print(
            f"[protected] NPZ materialized once: {n:,} transitions, "
            f"terrain+next={obs_mib:.1f} MiB, {load_elapsed:.1f}s"
        )

        t1 = time.perf_counter()
        for i in range(n):
            self.add_teacher_transition(
                {"terrain": terrain[i], "state": state[i]},
                {"terrain": next_terrain[i], "state": next_state[i]},
                actions[i],
                float(rewards[i]),
                bool(dones[i]),
            )

        insert_elapsed = time.perf_counter() - t1
        print(
            f"[protected] inserted {n:,} teacher transitions in "
            f"{insert_elapsed:.1f}s"
        )
        return int(n)

    def add(self, obs, next_obs, action, reward, done, infos):
        done_arr = np.asarray(done)
        reward_arr = np.asarray(reward)
        for i in range(self.n_envs):
            tr = (
                _one_env_obs(obs, i),
                _one_env_obs(next_obs, i),
                np.array(action[i], copy=True),
                float(reward_arr[i]),
                bool(done_arr[i]),
                dict(infos[i]) if infos is not None else {},
            )
            self._episode_cache[i].append(tr)
            if bool(done_arr[i]):
                info = tr[5]
                metrics = info.get("episode_metrics", {}) if isinstance(info, dict) else {}
                success = bool(metrics.get("success", 0))
                route_progress = float(metrics.get("route_progress", 0.0))
                collision = bool(metrics.get("collision", 0))
                reason = str(metrics.get("terminal_reason", ""))
                near_success = bool(
                    self.protect_near_success
                    and route_progress >= self.near_success_route_progress
                    and not collision
                    and reason not in {"rollover", "out_of_bounds"}
                )
                if success or near_success:
                    for item in self._episode_cache[i]:
                        self._add_protected_transition(*item, teacher=False)
                    self.online_protected_episodes += 1
                    self.online_protected_transitions += len(self._episode_cache[i])
                self._episode_cache[i].clear()
        super().add(obs, next_obs, action, reward, done, infos)

    @staticmethod
    def _concat_samples(normal, demo):
        """Concatenate normal + protected SB3 replay samples safely.

        Stable-Baselines3 2.7+ replay sample NamedTuples may contain optional
        fields such as ``discounts``.  For ordinary one-step replay that field is
        legitimately ``None``.  It must therefore be preserved as ``None`` rather
        than passed to torch.cat()/indexed during batch shuffling.

        Both buffers are created with the same replay configuration, so an
        optional field must either be present in BOTH samples or absent in BOTH.
        A one-sided None is treated as an integration error instead of silently
        fabricating a value.
        """
        fields = getattr(normal, "_fields", None)
        if fields is None:
            raise TypeError(
                "Unsupported SB3 replay sample type; expected NamedTuple-like _fields"
            )

        merged = {}
        for name in fields:
            a = getattr(normal, name)
            b = getattr(demo, name)

            # Optional SB3 fields (notably `discounts` for one-step replay).
            if a is None or b is None:
                if a is None and b is None:
                    merged[name] = None
                    continue
                raise TypeError(
                    f"Protected replay field {name!r} has incompatible optional "
                    f"values: normal={type(a).__name__}, demo={type(b).__name__}"
                )

            if isinstance(a, dict):
                if not isinstance(b, dict):
                    raise TypeError(
                        f"Replay field {name!r}: normal is dict but demo is "
                        f"{type(b).__name__}"
                    )
                if set(a.keys()) != set(b.keys()):
                    raise KeyError(
                        f"Replay field {name!r} observation keys differ: "
                        f"normal={sorted(a.keys())}, demo={sorted(b.keys())}"
                    )
                merged[name] = {
                    k: torch.cat([a[k], b[k]], dim=0)
                    for k in a.keys()
                }
            elif torch.is_tensor(a):
                if not torch.is_tensor(b):
                    raise TypeError(
                        f"Replay field {name!r}: normal is Tensor but demo is "
                        f"{type(b).__name__}"
                    )
                merged[name] = torch.cat([a, b], dim=0)
            else:
                raise TypeError(
                    f"Unsupported replay sample field {name!r}: "
                    f"{type(a).__name__}"
                )

        out = type(normal)(**merged)

        if out.actions is None or not torch.is_tensor(out.actions):
            raise TypeError("Replay sample 'actions' must be a Tensor")
        batch_n = int(out.actions.shape[0])
        perm = torch.randperm(batch_n, device=out.actions.device)

        shuffled = {}
        for name in fields:
            value = getattr(out, name)

            if value is None:
                shuffled[name] = None
            elif isinstance(value, dict):
                shuffled[name] = {k: v[perm] for k, v in value.items()}
            elif torch.is_tensor(value):
                # Only batch-shaped tensors are expected in SB3 replay samples.
                if value.shape[0] != batch_n:
                    raise RuntimeError(
                        f"Replay field {name!r} batch dimension "
                        f"{value.shape[0]} != actions batch {batch_n}"
                    )
                shuffled[name] = value[perm]
            else:
                raise TypeError(
                    f"Unsupported replay sample field {name!r} after merge: "
                    f"{type(value).__name__}"
                )

        return type(out)(**shuffled)

    def sample(self, batch_size: int, env=None):
        if self.protected.size() <= 0 or self.demo_fraction <= 0 or batch_size < 2:
            return super().sample(batch_size, env=env)
        demo_bs = int(np.clip(round(batch_size * self.demo_fraction), 1, batch_size - 1))
        normal_bs = batch_size - demo_bs
        normal = super().sample(normal_bs, env=env)
        demo = self.protected.sample(demo_bs, env=env)
        return self._concat_samples(normal, demo)

    def stats(self) -> dict:
        normal_rows = int(self.size())
        return {
            # ``size()`` is vector-buffer rows; multiply by n_envs for the number
            # of actual transitions represented in normal replay.
            "normal_size": int(normal_rows * self.n_envs),
            "normal_rows": normal_rows,
            "normal_n_envs": int(self.n_envs),
            "protected_size": int(self.protected.size()),
            "demo_fraction": float(self.demo_fraction),
            "teacher_demo_transitions": int(self.teacher_demo_transitions),
            "online_protected_episodes": int(self.online_protected_episodes),
            "online_protected_transitions": int(self.online_protected_transitions),
        }
