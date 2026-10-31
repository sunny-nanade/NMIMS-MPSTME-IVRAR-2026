import platform,sys
print("Python:",sys.version); print("OS:",platform.platform())
try:
    import torch
    print("PyTorch:",torch.__version__); print("CUDA available:",torch.cuda.is_available())
    if torch.cuda.is_available(): print("GPU:",torch.cuda.get_device_name(0)); print("CUDA runtime:",torch.version.cuda)
except Exception as e: print("PyTorch check skipped/failed:",e)
for name in ["numpy","scipy","matplotlib","rasterio","pybullet"]:
    try:
        mod=__import__(name); print(f"{name}: OK",getattr(mod,"__version__",""))
    except Exception as e: print(f"{name}: MISSING ({e})")
