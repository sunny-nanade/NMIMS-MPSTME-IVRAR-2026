"""Plot actual Python v1.5f benchmark output. Refuses simulated faculty CSVs."""
from pathlib import Path
import argparse
import csv
import numpy as np
import matplotlib.pyplot as plt

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--csv',type=Path,required=True)
    args=ap.parse_args()
    p=args.csv
    if 'SIMULATED' in p.name.upper():
        raise SystemExit('Refusing faculty synthetic CSV. Use genuine Phase 1 benchmark_summary.csv.')
    if not p.exists(): raise SystemExit(f'No genuine benchmark CSV: {p}')
    with p.open(newline='',encoding='utf8-sig') as f:
        reader=csv.DictReader(f)
        expected={'model','regime','episodes','success','collision'}
        if not expected.issubset(reader.fieldnames or []):
            raise SystemExit(f'Wrong CSV columns; need {sorted(expected)}. Got {reader.fieldnames}')
        rows=list(reader)
    if not rows:raise SystemExit('CSV contains no evaluated episodes')
    filtered=[r for r in rows if int(float(r['episodes']))>0]
    if not filtered:raise SystemExit('No nonzero episode counts')
    names=[r['model']+' / '+r['regime'] for r in filtered]
    success=np.array([float(r['success']) for r in filtered]);collision=np.array([float(r['collision']) for r in filtered]);
    if not (np.isfinite(success).all() and np.isfinite(collision).all()):raise SystemExit('Nonfinite metrics')
    if np.any((success<0)|(success>1)|(collision<0)|(collision>1)):raise SystemExit('Rate outside [0,1]')
    y=np.arange(len(names));fig,ax=plt.subplots(figsize=(11,max(3.5,0.55*len(names)+1.5)))
    ax.barh(y-0.17,success*100,height=.32,label='Goal success %')
    ax.barh(y+0.17,collision*100,height=.32,label='Collision %')
    ax.set_yticks(y,names);ax.set_xlim(0,105);ax.set_xlabel('Percent of evaluated episodes')
    ax.set_title('Measured Phase 1 v1.5f Benchmark - source: benchmark_summary.csv')
    ax.legend();ax.grid(axis='x',alpha=.25)
    fig.tight_layout()
    out=Path(__file__).resolve().parents[1]/'docs'/'figures'/'figure3_measured_sac_comparison.png'
    fig.savefig(out,dpi=300);plt.close(fig)
    print(f'MEASURED chart saved: {out}; rows: {len(rows)}')
if __name__=='__main__':main()
