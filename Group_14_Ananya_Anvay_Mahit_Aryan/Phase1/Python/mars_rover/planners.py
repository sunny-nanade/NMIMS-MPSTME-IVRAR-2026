from __future__ import annotations
from dataclasses import dataclass
from heapq import heappop, heappush
import math, time
import numpy as np

SQRT2=math.sqrt(2.0)
MOVES8=[(-1,0,1.0),(1,0,1.0),(0,-1,1.0),(0,1,1.0),
        (-1,-1,SQRT2),(-1,1,SQRT2),(1,-1,SQRT2),(1,1,SQRT2)]

def _heuristic(a,b):
    dr,dc=abs(a[0]-b[0]),abs(a[1]-b[1])
    return max(dr,dc)+(SQRT2-1.0)*min(dr,dc)

def _neighbors(cost,u):
    rows,cols=cost.shape; r,c=u
    if not np.isfinite(cost[u]): return
    for dr,dc,d in MOVES8:
        v=(r+dr,c+dc)
        if 0<=v[0]<rows and 0<=v[1]<cols and np.isfinite(cost[v]):
            yield v,d*0.5*(cost[u]+cost[v])

@dataclass
class PlanResult:
    path: np.ndarray
    planning_time_ms: float
    nodes_expanded: int
    success: bool

def astar(cost,start,goal):
    t0=time.perf_counter(); pq=[(0.0,start)]; g={start:0.0}; parent={}; closed=set(); expanded=0
    while pq:
        _,u=heappop(pq)
        if u in closed: continue
        closed.add(u); expanded+=1
        if u==goal: break
        for v,edge in _neighbors(cost,u):
            ng=g[u]+edge
            if ng<g.get(v,math.inf):
                g[v]=ng; parent[v]=u
                heappush(pq,(ng+_heuristic(v,goal),v))
    if goal not in g:
        return PlanResult(np.empty((0,2),int),(time.perf_counter()-t0)*1000,expanded,False)
    path=[goal]
    while path[-1]!=start: path.append(parent[path[-1]])
    path.reverse()
    return PlanResult(np.asarray(path,int),(time.perf_counter()-t0)*1000,expanded,True)

class DStarLite:
    """D* Lite for an 8-connected weighted grid, with lazy priority-queue deletion."""
    def __init__(self,cost,start,goal):
        self.cost=cost.copy(); self.start=start; self.goal=goal; self.last=start; self.km=0.0
        self.g={}; self.rhs={goal:0.0}; self.U=[]; self.active={}; self.counter=0; self.expanded=0
        self._push(goal)
    def _G(self,s): return self.g.get(s,math.inf)
    def _RHS(self,s): return self.rhs.get(s,math.inf)
    def _key(self,s):
        m=min(self._G(s),self._RHS(s))
        return (m+_heuristic(self.start,s)+self.km,m)
    def _push(self,s):
        key=self._key(s); self.counter+=1; self.active[s]=key
        heappush(self.U,(key,self.counter,s))
    def _remove(self,s):
        self.active.pop(s,None)
    def _discard_stale(self):
        while self.U:
            key,_,s=self.U[0]
            if self.active.get(s)==key: break
            heappop(self.U)
    def _top_key(self):
        self._discard_stale()
        return self.U[0][0] if self.U else (math.inf,math.inf)
    def _pop(self):
        self._discard_stale()
        if not self.U: return None,None
        key,_,s=heappop(self.U); self.active.pop(s,None); return key,s
    def update_vertex(self,u):
        if u!=self.goal:
            vals=[edge+self._G(v) for v,edge in _neighbors(self.cost,u)]
            self.rhs[u]=min(vals) if vals else math.inf
        self._remove(u)
        if self._G(u)!=self._RHS(u): self._push(u)
    def compute_shortest_path(self,max_iter=2_000_000):
        it=0
        while self._top_key()<self._key(self.start) or self._RHS(self.start)!=self._G(self.start):
            if it>=max_iter: raise RuntimeError("D* Lite exceeded max iterations")
            it+=1; k_old,u=self._pop()
            if u is None: break
            k_new=self._key(u)
            if k_old<k_new:
                self._push(u)
            elif self._G(u)>self._RHS(u):
                self.g[u]=self._RHS(u); self.expanded+=1
                for pred,_ in _neighbors(self.cost,u): self.update_vertex(pred)
            else:
                self.g[u]=math.inf; self.expanded+=1
                self.update_vertex(u)
                for pred,_ in _neighbors(self.cost,u): self.update_vertex(pred)
    def update_costs(self,changed):
        self.km+=_heuristic(self.last,self.start); self.last=self.start
        affected=set()
        for r,c,new_cost in changed:
            self.cost[r,c]=new_cost; affected.add((r,c))
            for dr,dc,_ in MOVES8:
                rr,cc=r+dr,c+dc
                if 0<=rr<self.cost.shape[0] and 0<=cc<self.cost.shape[1]: affected.add((rr,cc))
        for u in affected: self.update_vertex(u)
    def extract_path(self,max_steps=10000):
        if not np.isfinite(self._G(self.start)) and not np.isfinite(self._RHS(self.start)):
            return np.empty((0,2),int)
        u=self.start; path=[u]; seen={u}
        for _ in range(max_steps):
            if u==self.goal: return np.asarray(path,int)
            choices=[(edge+self._G(v),v) for v,edge in _neighbors(self.cost,u)]
            choices=[x for x in choices if np.isfinite(x[0])]
            if not choices: break
            _,u=min(choices,key=lambda x:x[0])
            if u in seen: break
            seen.add(u); path.append(u)
        return np.empty((0,2),int)
    def plan(self):
        t0=time.perf_counter(); self.compute_shortest_path(); path=self.extract_path()
        return PlanResult(path,(time.perf_counter()-t0)*1000,self.expanded,len(path)>0)

def dstar_lite(cost,start,goal): return DStarLite(cost,start,goal).plan()

def hybrid_astar(cost,start,goal,heading_bins=16,step_cells=2.0,turn_bins=(-1,0,1),turn_penalty=0.25):
    t0=time.perf_counter(); rows,cols=cost.shape
    start_state=(int(start[0]),int(start[1]),0)
    pq=[(0.0,start_state)]; g={start_state:0.0}; parent={}; closed=set(); expanded=0; best_goal=None
    def h(st): return math.hypot(st[0]-goal[0],st[1]-goal[1])
    while pq:
        _,u=heappop(pq)
        if u in closed: continue
        closed.add(u); expanded+=1
        r,c,hb=u
        if math.hypot(r-goal[0],c-goal[1])<=max(2.0,step_cells):
            best_goal=u; break
        for turn in turn_bins:
            nh=(hb+turn)%heading_bins; theta=2*math.pi*nh/heading_bins
            nr=int(round(r+step_cells*math.sin(theta))); nc=int(round(c+step_cells*math.cos(theta)))
            if not (0<=nr<rows and 0<=nc<cols and np.isfinite(cost[nr,nc])): continue
            samples=max(2,int(math.ceil(step_cells*2))); valid=True; edge_cost=0.0; pr,pc=r,c
            for k in range(1,samples+1):
                a=k/samples; rr=int(round(r+a*(nr-r))); cc=int(round(c+a*(nc-c)))
                if not (0<=rr<rows and 0<=cc<cols and np.isfinite(cost[rr,cc])):
                    valid=False; break
                edge_cost+=math.hypot(rr-pr,cc-pc)*cost[rr,cc]; pr,pc=rr,cc
            if not valid: continue
            v=(nr,nc,nh); ng=g[u]+edge_cost+turn_penalty*abs(turn)
            if ng<g.get(v,math.inf):
                g[v]=ng; parent[v]=u; heappush(pq,(ng+h(v),v))
    if best_goal is None:
        return PlanResult(np.empty((0,2),int),(time.perf_counter()-t0)*1000,expanded,False)
    states=[best_goal]
    while states[-1]!=start_state: states.append(parent[states[-1]])
    states.reverse(); path=np.asarray([(s[0],s[1]) for s in states],int)
    if tuple(path[-1])!=tuple(goal): path=np.vstack([path,np.asarray(goal,int)])
    return PlanResult(path,(time.perf_counter()-t0)*1000,expanded,True)
