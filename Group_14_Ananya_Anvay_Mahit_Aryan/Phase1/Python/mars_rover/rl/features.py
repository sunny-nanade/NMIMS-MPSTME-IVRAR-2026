from __future__ import annotations

import torch
from torch import nn
from stable_baselines3.common.torch_layers import BaseFeaturesExtractor

from mars_rover.rl.precision import autocast_context


class TerrainStateExtractor(BaseFeaturesExtractor):
    """Small laptop-friendly multimodal CNN + rover-state encoder.

    Safe AMP policy:
    - Parameters always remain FP32.
    - During *training only*, the feature encoder may execute under CUDA BF16
      autocast when precision="bf16".
    - Returned features are explicitly FP32, so PPO/SAC policy heads, probability
      ratios, Bellman targets, entropy terms and losses stay in SB3's normal FP32
      path.
    - In eval/predict mode this extractor is always FP32, preserving deterministic
      validation/testing precision.
    """

    def __init__(self, observation_space, features_dim: int = 256, precision: str = "fp32"):
        super().__init__(observation_space, features_dim)
        terrain_shape = observation_space["terrain"].shape
        state_dim = observation_space["state"].shape[0]
        channels = int(terrain_shape[0])
        self.precision = str(precision).lower()
        if self.precision not in {"fp32", "bf16"}:
            raise ValueError("TerrainStateExtractor precision must resolve to 'fp32' or 'bf16'")

        self.cnn = nn.Sequential(
            nn.Conv2d(channels, 32, kernel_size=5, stride=2, padding=2),
            nn.ReLU(),
            nn.Conv2d(32, 64, kernel_size=3, stride=2, padding=1),
            nn.ReLU(),
            nn.Conv2d(64, 96, kernel_size=3, stride=2, padding=1),
            nn.ReLU(),
            nn.AdaptiveAvgPool2d((2, 2)),
            nn.Flatten(),
            nn.Linear(96 * 2 * 2, 192),
            nn.ReLU(),
        )
        self.state_mlp = nn.Sequential(
            nn.Linear(state_dim, 64),
            nn.ReLU(),
            nn.Linear(64, 64),
            nn.ReLU(),
        )
        self.fusion = nn.Sequential(
            nn.Linear(192 + 64, features_dim),
            nn.ReLU(),
            nn.LayerNorm(features_dim),
        )

    def forward(self, observations):
        terrain = observations["terrain"].float() / 255.0
        state = observations["state"].float()
        # Only the encoder is autocast. SB3's policy/critic heads and all RL math see
        # FP32 features. self.training=False during deterministic predict/evaluation.
        amp_enabled = self.training and terrain.is_cuda and self.precision == "bf16"
        with autocast_context(self.precision, enabled=amp_enabled):
            encoded = self.fusion(torch.cat([self.cnn(terrain), self.state_mlp(state)], dim=1))
        return encoded.float()
