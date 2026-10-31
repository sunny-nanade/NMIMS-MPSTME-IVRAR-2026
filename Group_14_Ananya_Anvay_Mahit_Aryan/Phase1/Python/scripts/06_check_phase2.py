import importlib
import sys
from pathlib import Path
import xml.etree.ElementTree as ET

mods = ["numpy", "scipy", "pandas", "rasterio", "pybullet", "gymnasium", "stable_baselines3", "torch", "tensorboard"]
for m in mods:
    try:
        mod = importlib.import_module(m)
        print(f"{m:20s} OK {getattr(mod, '__version__', '')}")
    except Exception as e:
        print(f"{m:20s} FAIL {e}")

try:
    import torch
    print("\nCUDA available:", torch.cuda.is_available())
    if torch.cuda.is_available():
        print("GPU:", torch.cuda.get_device_name(0))
except Exception:
    pass

try:
    import mars_rover
    print("\nmars_rover Phase-2 package: OK")
except Exception as e:
    print("\nmars_rover package: FAIL", e)

urdf = Path("assets/urdf/perseverance_surrogate.urdf")
try:
    root = ET.parse(urdf).getroot()
    wheels = [j.attrib["name"] for j in root.findall("joint") if j.attrib.get("name", "").endswith("wheel_joint")]
    print("Six-wheel surrogate URDF: OK", urdf)
    print("Drive wheel joints:", len(wheels), wheels)
except Exception as e:
    print("Six-wheel surrogate URDF: FAIL", e)
