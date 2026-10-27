from __future__ import annotations

from contextlib import nullcontext
from typing import Union

import torch


_VALID = {"auto", "bf16", "fp32"}


def _device_type(device: Union[str, torch.device]) -> str:
    return torch.device(device).type


def resolve_precision(requested: str, device: Union[str, torch.device]) -> str:
    """Resolve safe training precision.

    Only BF16 autocast or full FP32 are allowed. We intentionally do not expose
    FP16 here because RL losses/TD targets are more sensitive and FP16 would also
    require GradScaler plumbing. On CPU, or on CUDA hardware without native BF16,
    BF16 requests fall back to FP32.
    """
    mode = str(requested).lower().strip()
    if mode not in _VALID:
        raise ValueError(f"precision must be one of {sorted(_VALID)}")

    if _device_type(device) != "cuda" or not torch.cuda.is_available():
        if mode == "bf16":
            print("[precision] BF16 requested without CUDA; falling back to FP32")
        return "fp32"

    bf16_ok = bool(torch.cuda.is_bf16_supported())
    if mode == "auto":
        return "bf16" if bf16_ok else "fp32"
    if mode == "bf16" and not bf16_ok:
        print("[precision] CUDA device does not report BF16 support; falling back to FP32")
        return "fp32"
    return mode


def autocast_context(mode: str, enabled: bool = True):
    """Return a CUDA BF16 autocast context or a no-op context.

    Master weights are never converted away from FP32. Callers should cast outputs
    back to FP32 before numerically sensitive RL math (TD targets, PPO ratios, losses,
    PER priorities, etc.).
    """
    use_amp = bool(enabled and mode == "bf16" and torch.cuda.is_available())
    if not use_amp:
        return nullcontext()
    return torch.autocast(device_type="cuda", dtype=torch.bfloat16, enabled=True)


def assert_fp32_master_weights(module: torch.nn.Module, name: str = "model") -> None:
    bad = [(n, p.dtype) for n, p in module.named_parameters() if p.is_floating_point() and p.dtype != torch.float32]
    if bad:
        preview = ", ".join(f"{n}:{dtype}" for n, dtype in bad[:8])
        raise RuntimeError(
            f"{name} contains non-FP32 master parameters ({preview}). "
            "Safe AMP requires FP32 master weights."
        )


def print_precision_plan(mode: str, device: Union[str, torch.device], scope: str) -> None:
    dev = torch.device(device)
    print(f"[precision] {scope}: {mode.upper()} training compute on {dev}")
    if mode == "bf16":
        print("[precision] master weights/optimizer state remain FP32; no GradScaler is used for BF16")
        print("[precision] sensitive RL math and validation/evaluation remain FP32")
    else:
        print("[precision] full FP32 reference mode")
    print("[precision] TF32 settings are not modified by this patch")
