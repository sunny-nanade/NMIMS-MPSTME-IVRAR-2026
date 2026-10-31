from pathlib import Path
from mars_rover.terrain import generate_procedural,save_terrain
out=Path("data/processed/procedural_mars.npz")
terrain=generate_procedural(size=129,meters_per_cell=1.0,seed=7,crater_count=7,boulder_count=35)
save_terrain(out,terrain)
print(f"Saved {out}")
print("Shape:",terrain["height"].shape)
print("Elevation range (m):",float(terrain["height"].min()),"to",float(terrain["height"].max()))
print("Virtual obstacle cells:",int(terrain["obstacle"].sum()))
