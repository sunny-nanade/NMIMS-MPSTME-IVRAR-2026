from __future__ import annotations

import ctypes
import os
import platform
from dataclasses import dataclass


@dataclass(frozen=True)
class ResourceSnapshot:
    logical_cpus: int
    available_ram_gb: float | None
    total_ram_gb: float | None


def _windows_memory():
    class MEMORYSTATUSEX(ctypes.Structure):
        _fields_ = [
            ("dwLength", ctypes.c_ulong),
            ("dwMemoryLoad", ctypes.c_ulong),
            ("ullTotalPhys", ctypes.c_ulonglong),
            ("ullAvailPhys", ctypes.c_ulonglong),
            ("ullTotalPageFile", ctypes.c_ulonglong),
            ("ullAvailPageFile", ctypes.c_ulonglong),
            ("ullTotalVirtual", ctypes.c_ulonglong),
            ("ullAvailVirtual", ctypes.c_ulonglong),
            ("ullAvailExtendedVirtual", ctypes.c_ulonglong),
        ]

    status = MEMORYSTATUSEX()
    status.dwLength = ctypes.sizeof(MEMORYSTATUSEX)
    if ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status)):
        div = 1024.0 ** 3
        return status.ullAvailPhys / div, status.ullTotalPhys / div
    return None, None


def _linux_memory():
    try:
        values = {}
        with open("/proc/meminfo", "r", encoding="utf-8") as f:
            for line in f:
                key, value = line.split(":", 1)
                values[key] = float(value.strip().split()[0]) * 1024.0
        div = 1024.0 ** 3
        return values.get("MemAvailable", 0.0) / div, values.get("MemTotal", 0.0) / div
    except Exception:
        return None, None


def resources() -> ResourceSnapshot:
    avail = total = None
    try:
        if platform.system() == "Windows":
            avail, total = _windows_memory()
        elif platform.system() == "Linux":
            avail, total = _linux_memory()
    except Exception:
        pass
    return ResourceSnapshot(max(1, os.cpu_count() or 1), avail, total)


def resolve_n_envs(requested: str | int, algorithm: str) -> int:
    """Resolve auto/1/2/4/6 to a conservative worker count.

    Auto is intentionally capped at 4 for PPO and 2 for SAC on a typical 16-GB
    Windows laptop. Six workers remain available for explicit benchmarking, but are
    never selected automatically on a 16-GB machine.
    """
    allowed = {1, 2, 4, 6}
    if str(requested).lower() != "auto":
        n = int(requested)
        if n not in allowed:
            raise ValueError("--n-envs must be auto, 1, 2, 4, or 6")
        return n

    r = resources()
    avail = r.available_ram_gb
    cpu = r.logical_cpus
    algo = algorithm.lower()

    if algo == "ppo":
        # Four independent PyBullet workers are a good utilization target for the
        # user's 14th-gen i7/16-GB system without turning RAM pressure into paging.
        if cpu >= 8 and (avail is None or avail >= 7.0):
            return 4
        if cpu >= 4 and (avail is None or avail >= 4.5):
            return 2
        return 1

    if algo == "sac":
        # SAC additionally owns a replay buffer and performs one gradient update per
        # collected transition, so two physics workers are the safer default.
        if cpu >= 6 and (avail is None or avail >= 6.0):
            return 2
        return 1

    return 1


def print_resource_plan(algorithm: str, n_envs: int) -> None:
    r = resources()
    ram = "unknown" if r.available_ram_gb is None else f"{r.available_ram_gb:.1f} GB available / {r.total_ram_gb:.1f} GB total"
    print(f"[performance] {algorithm.upper()} physics workers: {n_envs}")
    print(f"[performance] logical CPUs: {r.logical_cpus}; RAM: {ram}")
    if n_envs == 6:
        print("[performance] 6 workers is an explicit high-throughput mode; watch RAM/CPU and fall back to 4 if Windows starts paging.")
