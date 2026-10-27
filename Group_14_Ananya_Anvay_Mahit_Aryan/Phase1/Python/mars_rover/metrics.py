import numpy as np

def path_metrics(path,height,slope,meters_per_cell):
    if path is None or len(path)<2:
        return dict(path_length_m=np.nan,elevation_gain_m=np.nan,mean_slope_deg=np.nan,max_slope_deg=np.nan,energy_proxy=np.nan)
    a,b=path[:-1],path[1:]
    dr=b[:,0]-a[:,0]; dc=b[:,1]-a[:,1]
    ds=np.sqrt(dr*dr+dc*dc)*meters_per_cell
    z0=height[a[:,0],a[:,1]]; z1=height[b[:,0],b[:,1]]; dz=z1-z0
    sampled=slope[path[:,0],path[:,1]]
    energy=np.sum(ds*(1.0+0.02*sampled[:-1])+2.5*np.clip(dz,0,None))
    return dict(path_length_m=float(ds.sum()),elevation_gain_m=float(np.clip(dz,0,None).sum()),
                mean_slope_deg=float(sampled.mean()),max_slope_deg=float(sampled.max()),energy_proxy=float(energy))
