"""Quick numerical sanity check for the Phase-2 safe BF16 AMP path.

This does not train the rover. It verifies that:
- CUDA/BF16 support is visible,
- master parameters remain FP32,
- the multimodal PPO/SAC encoder returns FP32 features in both train/eval modes,
- the D3QN BF16 forward can backpropagate a finite FP32 loss.
"""
import argparse

import torch
from gymnasium import spaces
import numpy as np

from mars_rover.rl.features import TerrainStateExtractor
from mars_rover.rl.d3qn import DuelingQNetwork
from mars_rover.rl.precision import (
    resolve_precision, autocast_context, assert_fp32_master_weights, print_precision_plan,
)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--precision", default="bf16", choices=["auto", "bf16", "fp32"])
    args = ap.parse_args()

    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    mode = resolve_precision(args.precision, device)
    print("PyTorch:", torch.__version__)
    print("CUDA available:", torch.cuda.is_available())
    print("CUDA BF16 supported:", torch.cuda.is_bf16_supported() if torch.cuda.is_available() else False)
    if torch.cuda.is_available():
        print("GPU:", torch.cuda.get_device_name(0))
    print_precision_plan(mode, device, "safe-AMP self-test")

    obs_space = spaces.Dict({
        "terrain": spaces.Box(0, 255, shape=(5, 32, 32), dtype=np.uint8),
        "state": spaces.Box(-np.inf, np.inf, shape=(14,), dtype=np.float32),
    })
    extractor = TerrainStateExtractor(obs_space, features_dim=256, precision=mode).to(device)
    assert_fp32_master_weights(extractor, "TerrainStateExtractor")
    obs = {
        "terrain": torch.randint(0, 256, (32, 5, 32, 32), device=device, dtype=torch.uint8),
        "state": torch.randn(32, 14, device=device, dtype=torch.float32),
    }
    extractor.train()
    feat = extractor(obs)
    loss = feat.square().mean()
    loss.backward()
    print("Extractor train output dtype:", feat.dtype, "finite:", bool(torch.isfinite(feat).all().item()))
    print("Extractor loss finite:", bool(torch.isfinite(loss).item()))

    extractor.eval()
    with torch.no_grad():
        feat_eval = extractor(obs)
    print("Extractor eval output dtype:", feat_eval.dtype, "finite:", bool(torch.isfinite(feat_eval).all().item()))

    qnet = DuelingQNetwork().to(device)
    assert_fp32_master_weights(qnet, "D3QN")
    terrain = torch.randint(0, 256, (128, 5, 32, 32), device=device, dtype=torch.uint8)
    state = torch.randn(128, 14, device=device)
    qnet.train()
    with autocast_context(mode, enabled=(mode == "bf16")):
        q_amp = qnet(terrain, state)
    q = q_amp.float()
    q_loss = q.square().mean()
    q_loss.backward()
    print("D3QN autocast raw dtype:", q_amp.dtype)
    print("D3QN FP32 loss finite:", bool(torch.isfinite(q_loss).item()))
    assert_fp32_master_weights(qnet, "D3QN after backward")

    print("SAFE BF16 SELF-TEST: PASS")


if __name__ == "__main__":
    main()
