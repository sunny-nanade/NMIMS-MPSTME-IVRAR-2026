# Maze layout report

Seed 20260415, 120 valid layouts, 7140 pairs ranked.

Constraints: 6 x 6 cells of 2 m, unique shortest path 18 steps (36 m), 6 junctions on it, 3 dead ends, at least 2 misleading junctions.

## Top 10 pairs

| rank | score | edge overlap (best symmetry) | path overlap | difficulty gap |
|---|---|---|---|---|
| 1 | 1.378 | 0.49 | 0.41 | 0.28 |
| 2 | 1.246 | 0.39 | 0.41 | 0.56 |
| 3 | 1.245 | 0.47 | 0.52 | 0.28 |
| 4 | 1.205 | 0.45 | 0.52 | 0.33 |
| 5 | 1.075 | 0.49 | 0.46 | 0.50 |
| 6 | 1.074 | 0.49 | 0.46 | 0.50 |
| 7 | 1.056 | 0.44 | 0.41 | 0.67 |
| 8 | 1.030 | 0.44 | 0.46 | 0.61 |
| 9 | 1.005 | 0.40 | 0.41 | 0.78 |
| 10 | 0.954 | 0.66 | 0.52 | 0.28 |

Pair 1 is used as Layout A and Layout B.

## Layout A

start (3, 0) exit (4, 5), used cells 32, turns 10, longest straight 4, loops 2, branch depth 4
junctions: (5, 1)@3 correct W wrong ['N'], (3, 2)@6 correct S wrong ['N'] MISLEADING, (2, 1)@8 correct W wrong ['S'], (1, 2)@10 correct W wrong ['E'] MISLEADING, (0, 2)@11 correct N wrong ['S'], (0, 4)@13 correct N wrong ['E']
dead ends: [(3, 4), (4, 3), (5, 3)]

```
+--+--+--+--+--+--+
|o  o  o  o  E|#|
+  +--+--+--+--+--+
|o  .  .|.|#|#|
+  +--+  +  +--+--+
|o|#|.|.  .|.|
+  +--+  +  +--+  +
|o  o  .|o  o|.|
+  +  +--+  +  +  +
|.|o  o  o|o  o|
+  +--+  +--+--+  +
|.  .  .|S  o  o|
+--+--+--+--+--+--+
```

## Layout B

start (4, 0) exit (5, 5), used cells 33, turns 10, longest straight 4, loops 2, branch depth 4
junctions: (5, 1)@2 correct W wrong ['N'] MISLEADING, (2, 0)@6 correct N wrong ['W'], (2, 2)@8 correct N wrong ['E'], (2, 3)@9 correct E wrong ['W'], (3, 4)@11 correct W wrong ['E'] MISLEADING, (1, 4)@13 correct N wrong ['W']
dead ends: [(0, 3), (0, 5), (5, 4)]

```
+--+--+--+--+--+--+
|.|o  o  o  o  E|
+  +  +--+--+--+--+
|.  o  o  o  .|.|
+  +--+--+  +  +  +
|.|.  o  o|.|.|
+--+  +  +--+  +  +
|#|.|o  .  .|.|
+--+  +  +--+--+  +
|#|.|o|o  o  o|
+--+  +  +  +--+  +
|#|.  o  o|S  o|
+--+--+--+--+--+--+
```
