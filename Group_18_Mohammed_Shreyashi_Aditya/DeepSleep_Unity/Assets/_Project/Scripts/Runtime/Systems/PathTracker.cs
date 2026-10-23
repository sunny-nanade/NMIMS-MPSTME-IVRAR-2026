using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Follows the head through the maze grid and counts what the study measures.
    /// Wrong turn: stepping from a cell on the shortest path into a cell that is not on it (once per departure).
    /// Dead-end entry: entering a dead-end cell. Backtrack: stepping back into the cell just left (a U-turn between cells).
    /// Path length: horizontal head travel (jumps over 1 m are ignored). Efficiency: shortest metres / travelled metres, at most 1.
    /// Idle: horizontal speed under 0.1 m/s.
    /// </summary>
    public class PathTracker : MonoBehaviour
    {
        [SerializeField] float idleSpeed = 0.1f;
        [SerializeField] float jumpIgnoreMeters = 1f;

        public bool Tracking { get; private set; }
        public int WrongTurns { get; private set; }
        public int DeadEndEntries { get; private set; }
        public int Backtracks { get; private set; }
        public float PathLength { get; private set; }
        public float IdleTotal { get; private set; }
        public float IdleStreak { get; private set; }
        public MazeCell Current { get; private set; }
        public List<MazeCell> Visits { get; } = new List<MazeCell>();
        public float Efficiency => PathLength <= 0.01f || _layout == null ? 1f : Mathf.Min(1f, _layout.Data.shortestMeters / PathLength);

        public event Action<MazeCell> CellEntered;
        public event Action<MazeCell> JunctionEntered;
        public event Action<MazeCell> DeadEndEntered;
        public event Action<MazeCell> WrongTurnMade;

        MazeLayout _layout;
        Vector3 _lastPos; bool _hasLast;
        bool _inGrid;
        readonly HashSet<MazeCell> _junctionsSeen = new HashSet<MazeCell>();

        public void Begin(MazeLayout layout)
        {
            _layout = layout;
            WrongTurns = DeadEndEntries = Backtracks = 0; PathLength = IdleTotal = IdleStreak = 0f;
            Visits.Clear(); _junctionsSeen.Clear(); _hasLast = false; _inGrid = false;
            Tracking = true;
        }

        public void End() { Tracking = false; }

        void Update()
        {
            if (!Tracking || _layout == null) return;
            var rig = PlayerRig.Instance;
            if (rig == null || rig.Head == null) return;
            Vector3 p = rig.Head.position;
            if (_hasLast)
            {
                Vector3 d = p - _lastPos; d.y = 0f;
                float dist = d.magnitude;
                if (dist < jumpIgnoreMeters) PathLength += dist;
                float speed = Time.deltaTime > 0f ? dist / Time.deltaTime : 0f;
                if (speed < idleSpeed) { IdleTotal += Time.deltaTime; IdleStreak += Time.deltaTime; } else IdleStreak = 0f;
            }
            _lastPos = p; _hasLast = true;

            if (!_layout.TryCellOf(p, out var cell) || !_layout.Used(cell.c, cell.r)) return;
            if (_inGrid && cell == Current) return;
            // ignore diagonal cuts through wall corners: only count cells that are open neighbours of the current one
            if (_inGrid && !Adjacent(Current, cell)) return;
            Enter(cell);
        }

        bool Adjacent(MazeCell a, MazeCell b)
        {
            for (int d = 0; d < 4; d++)
                if (_layout.IsOpen(a.c, a.r, d) && a.c + MazeLayout.DirDc(d) == b.c && a.r + MazeLayout.DirDr(d) == b.r) return true;
            return false;
        }

        void Enter(MazeCell cell)
        {
            if (_inGrid)
            {
                bool wasOnPath = _layout.OnPath(Current), nowOnPath = _layout.OnPath(cell);
                if (Visits.Count >= 2 && Visits[Visits.Count - 2] == cell) Backtracks++;
                if (wasOnPath && !nowOnPath) { WrongTurns++; WrongTurnMade?.Invoke(Current); }
            }
            Visits.Add(cell);
            Current = cell; _inGrid = true;
            CellEntered?.Invoke(cell);
            if (_layout.IsDeadEnd(cell)) { DeadEndEntries++; DeadEndEntered?.Invoke(cell); }
            if (_layout.IsJunction(cell) && _junctionsSeen.Add(cell)) JunctionEntered?.Invoke(cell);
        }
    }
}
