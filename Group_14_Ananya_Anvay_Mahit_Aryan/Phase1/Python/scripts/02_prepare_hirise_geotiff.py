import argparse
from pathlib import Path
from mars_rover.terrain import load_geotiff_crop,save_terrain
p=argparse.ArgumentParser(); p.add_argument("tif"); p.add_argument("--out",default="data/processed/hirise_mars.npz")
p.add_argument("--size",type=int,default=257); p.add_argument("--crop",type=int,default=512)
args=p.parse_args()
terrain=load_geotiff_crop(args.tif,out_size=args.size,crop_size_px=args.crop); save_terrain(args.out,terrain)
print("Saved:",Path(args.out).resolve()); print("Processed shape:",terrain["height"].shape); print("Meters/cell:",terrain["meters_per_cell"])
