using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fractured_Final
{
    [Serializable] public class MazeCellRef { public int c; public int r; }
    [Serializable] public class MazeEnd { public int c; public int r; public string side; }
    [Serializable] public class MazeJunctionData { public int c; public int r; public int index; public string correct; public string[] wrong; public bool misleading; }

    /// <summary>JSON written by Tools/MazeLayoutVerifier.py (Data/MazeLayoutA.json and B). Cells are flat: index = r * grid + c, bit mask N=1 E=2 S=4 W=8.</summary>
    [Serializable]
    public class MazeLayoutData
    {
        public string name;
        public int grid;
        public float cellSize;
        public MazeEnd start;
        public MazeEnd exit;
        public int[] cells;
        public MazeCellRef[] path;
        public int shortestSteps;
        public float shortestMeters;
        public MazeJunctionData[] junctions;
        public MazeCellRef[] deadEnds;
    }

    public struct MazeCell : IEquatable<MazeCell>
    {
        public int c, r;
        public MazeCell(int c, int r) { this.c = c; this.r = r; }
        public bool Equals(MazeCell o) { return c == o.c && r == o.r; }
        public override bool Equals(object o) { return o is MazeCell m && Equals(m); }
        public override int GetHashCode() { return c * 31 + r; }
        public override string ToString() { return "(" + c + "," + r + ")"; }
        public static bool operator ==(MazeCell a, MazeCell b) { return a.Equals(b); }
        public static bool operator !=(MazeCell a, MazeCell b) { return !a.Equals(b); }
    }

    /// <summary>Runtime view of a maze layout: adjacency, shortest path, junctions, dead ends, cell geometry (origin at the south-west corner of the grid).</summary>
    public class MazeLayout
    {
        public const int N = 1, E = 2, S = 4, W = 8;
        static readonly int[] DC = { 0, 1, 0, -1 };
        static readonly int[] DR = { 1, 0, -1, 0 };
        static readonly int[] Bits = { N, E, S, W };

        public MazeLayoutData Data { get; private set; }
        public int Grid { get { return Data.grid; } }
        public float Cell { get { return Data.cellSize; } }
        public MazeCell Start { get; private set; }
        public MazeCell Exit { get; private set; }
        public List<MazeCell> Path { get; } = new List<MazeCell>();
        public HashSet<MazeCell> Junctions { get; } = new HashSet<MazeCell>();
        public HashSet<MazeCell> DeadEnds { get; } = new HashSet<MazeCell>();
        readonly Dictionary<MazeCell, int> _pathIndex = new Dictionary<MazeCell, int>();

        public static MazeLayout Parse(string json)
        {
            var d = JsonUtility.FromJson<MazeLayoutData>(json);
            if (d == null || d.cells == null || d.cells.Length != d.grid * d.grid) throw new ArgumentException("bad maze layout json");
            var l = new MazeLayout { Data = d };
            l.Start = new MazeCell(d.start.c, d.start.r);
            l.Exit = new MazeCell(d.exit.c, d.exit.r);
            foreach (var p in d.path) { l._pathIndex[new MazeCell(p.c, p.r)] = l.Path.Count; l.Path.Add(new MazeCell(p.c, p.r)); }
            foreach (var j in d.junctions) l.Junctions.Add(new MazeCell(j.c, j.r));
            foreach (var e in d.deadEnds) l.DeadEnds.Add(new MazeCell(e.c, e.r));
            return l;
        }

        public int Mask(int c, int r) { return Data.cells[r * Data.grid + c]; }
        public bool Inside(int c, int r) { return c >= 0 && r >= 0 && c < Data.grid && r < Data.grid; }
        public bool Used(int c, int r) { return Inside(c, r) && (Mask(c, r) != 0 || new MazeCell(c, r) == Start || new MazeCell(c, r) == Exit); }
        public bool IsOpen(int c, int r, int dir) { return (Mask(c, r) & Bits[dir]) != 0; }
        public static int DirDc(int dir) { return DC[dir]; }
        public static int DirDr(int dir) { return DR[dir]; }
        public static int DirBit(int dir) { return Bits[dir]; }

        public int Degree(MazeCell cell)
        {
            int n = 0; for (int d = 0; d < 4; d++) if (IsOpen(cell.c, cell.r, d)) n++; return n;
        }

        public int PathIndex(MazeCell cell) { return _pathIndex.TryGetValue(cell, out int i) ? i : -1; }
        public bool OnPath(MazeCell cell) { return _pathIndex.ContainsKey(cell); }
        public bool IsDeadEnd(MazeCell cell) { return DeadEnds.Contains(cell); }
        public bool IsJunction(MazeCell cell) { return Junctions.Contains(cell); }

        /// <summary>World position of a cell centre (maze origin at world 0,0,0 plus optional offset).</summary>
        public Vector3 CellCenter(MazeCell cell, Vector3 origin = default)
        {
            return origin + new Vector3((cell.c + 0.5f) * Data.cellSize, 0f, (cell.r + 0.5f) * Data.cellSize);
        }

        public bool TryCellOf(Vector3 world, out MazeCell cell, Vector3 origin = default)
        {
            Vector3 p = world - origin;
            int c = Mathf.FloorToInt(p.x / Data.cellSize), r = Mathf.FloorToInt(p.z / Data.cellSize);
            cell = new MazeCell(c, r);
            return Inside(c, r);
        }

        public struct Metrics { public int steps; public bool unique; public int junctions; public int deadEnds; public int misleading; }

        /// <summary>Recomputes the key metrics from the cell masks (same rules as the Python verifier).</summary>
        public Metrics Verify()
        {
            var dist = new Dictionary<MazeCell, int> { { Start, 0 } };
            var count = new Dictionary<MazeCell, int> { { Start, 1 } };
            var q = new Queue<MazeCell>(); q.Enqueue(Start);
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    if (!IsOpen(cur.c, cur.r, d)) continue;
                    var nb = new MazeCell(cur.c + DC[d], cur.r + DR[d]);
                    if (!dist.ContainsKey(nb)) { dist[nb] = dist[cur] + 1; count[nb] = 0; q.Enqueue(nb); }
                    if (dist[nb] == dist[cur] + 1) count[nb] += count[cur];
                }
            }
            var m = new Metrics();
            if (!dist.ContainsKey(Exit)) return m;
            m.steps = dist[Exit]; m.unique = count[Exit] == 1;
            var path = new List<MazeCell> { Exit };
            var at = Exit;
            while (at != Start)
            {
                for (int d = 0; d < 4; d++)
                {
                    if (!IsOpen(at.c, at.r, d)) continue;
                    var nb = new MazeCell(at.c + DC[d], at.r + DR[d]);
                    if (dist.TryGetValue(nb, out int dn) && dn == dist[at] - 1) { path.Add(nb); at = nb; break; }
                }
            }
            path.Reverse();
            for (int i = 1; i < path.Count - 1; i++)
            {
                var cell = path[i];
                if (Degree(cell) < 3) continue;
                m.junctions++;
                for (int d = 0; d < 4; d++)
                {
                    if (!IsOpen(cell.c, cell.r, d)) continue;
                    var nb = new MazeCell(cell.c + DC[d], cell.r + DR[d]);
                    if (nb == path[i - 1] || nb == path[i + 1]) continue;
                    if (Mathf.Abs(nb.c - Exit.c) + Mathf.Abs(nb.r - Exit.r) < Mathf.Abs(path[i + 1].c - Exit.c) + Mathf.Abs(path[i + 1].r - Exit.r)) { m.misleading++; break; }
                }
            }
            foreach (var kv in dist)
                if (kv.Key != Start && kv.Key != Exit && Degree(kv.Key) == 1) m.deadEnds++;
            return m;
        }
    }
}
