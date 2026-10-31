using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// V1.5f runtime-obstacle local replanner — V4 rover-anchored target.
///
/// Drop-in replacement for the previous DynamicGuidanceAvoidance component.
/// It keeps the SAME public API used by UnityUdpBridge/V01HUD, but replaces the
/// heuristic lateral target shift with a true 32x32 local A* replanner.
///
/// This is intentionally close to the V1.5f PyBullet training semantics and fixes the V3 stale-world-target deadlock:
///
///   dense local obstacle map
///      -> inflate by rover footprint + safety margin
///      -> local A* around a newly appeared obstacle
///      -> ~5.5 m moving target on the temporary local path
///      -> SAC generates steering
///
/// It does NOT override SAC steering.
/// It normally does NOT cap throttle either; RoverDrive already reproduces the
/// PyBullet turn-speed scaling: speed *= (1 - 0.38 * abs(steer)).
///
/// Only an optional last-resort emergency throttle cap remains.
/// </summary>
public class DynamicGuidanceAvoidance : MonoBehaviour
{
    [Header("Master Toggle")]
    public bool enableDynamicGuidanceAssist = true;

    [Header("References")]
    public Rigidbody roverBody;
    public LidarGridSensor lidar;

    [Header("V1.5f Geometry")]
    public float roverFootprintRadiusM = 1.95f;
    public float safetyMarginM = 0.35f;

    [Header("Local Planner")]
    [Tooltip("Local A* plans toward an anchor this far ahead. Must fit inside the 24 m local grid front half.")]
    [Range(7f, 11.5f)]
    public float localPlanHorizonM = 10.5f;

    [Tooltip("SAC receives a target this far along the temporary local A* path. V1.5f static guidance is 6 m; 5.5 m gives slightly earlier curvature response.")]
    [Range(3.5f, 6.0f)]
    public float localGuidanceLookaheadM = 5.5f;

    [Tooltip("Start replanning when the nominal route corridor has less free margin than this.")]
    [Range(0f, 1.5f)]
    public float triggerFreeMarginM = 0.80f;

    [Tooltip("Do not release the detour until nominal route margin is at least this much for a stable period.")]
    [Range(0.4f, 2.0f)]
    public float releaseFreeMarginM = 1.20f;

    [Tooltip("Require the nominal corridor to remain clear for this long before returning to static A*.")]
    [Range(0.25f, 3f)]
    public float clearStableSeconds = 0.70f;

    [Tooltip("Minimum physical rover travel after detour activation before release is allowed.")]
    [Range(2f, 10f)]
    public float minimumDetourTravelM = 3.0f;

    [Header("A* Cost")]
    [Tooltip("Penalty for cells close to an inflated obstacle. Helps the local route avoid grazing rock edges.")]
    [Range(0f, 8f)]
    public float nearObstaclePenalty = 2.4f;

    [Tooltip("Extra cost for switching to the opposite side after a detour side has been chosen.")]
    [Range(0f, 5f)]
    public float oppositeSidePenalty = 1.2f;

    [Tooltip("Keep the initially chosen passing side unless the planner cannot find a route.")]
    public bool preferCommittedSide = true;

    [Header("Rover-Anchored Target Motion")]
    [Tooltip("Maximum guidance bearing presented to SAC. Prevents an A* grid corner from becoming an almost-sideways/behind target.")]
    [Range(30f, 75f)]
    public float maxGuidanceBearingDeg = 52.0f;

    [Tooltip("Minimum forward component of the temporary target in the rover frame.")]
    [Range(1f, 5f)]
    public float minGuidanceForwardM = 2.75f;

    [Tooltip("Maximum bearing change rate of the temporary target. Filtering is done in the CURRENT rover frame, never in stale world coordinates.")]
    [Range(45f, 240f)]
    public float guidanceBearingSlewDegPerSecond = 140.0f;

    [Tooltip("Maximum target-distance change rate.")]
    [Range(1f, 12f)]
    public float guidanceDistanceSlewMPerSecond = 6.0f;

    [Header("Detour Release")]
    [Tooltip("Absolute time limit for the travel-based commitment once the nominal corridor is already stably clear. Prevents a low-speed deadlock.")]
    [Range(1f, 10f)]
    public float maxClearDetourCommitSeconds = 3.5f;

    [Header("Emergency Throttle Only")]
    [Tooltip("Recommended OFF for the first test. RoverDrive already applies the PyBullet-matched turn-speed reduction.")]
    public bool enableThrottleGovernor = false;

    [Tooltip("Only used if Enable Throttle Governor is ON and no safe local path exists very close to an obstacle.")]
    [Range(0f, 1f)]
    public float emergencyThrottleCap = 0.42f;

    [Tooltip("Only cap throttle if nearest occupied cell is closer than this.")]
    [Range(0.5f, 3f)]
    public float emergencyObstacleDistanceM = 1.35f;

    [Header("Debug")]
    public bool drawDebug = true;

    [HideInInspector] public bool assistActive;
    [HideInInspector] public int chosenSide; // +1 left, -1 right, 0 none
    [HideInInspector] public float currentTargetOffsetM;
    [HideInInspector] public float nominalCorridorClearanceM = 99f;
    [HideInInspector] public float selectedCorridorClearanceM = 99f;
    [HideInInspector] public float recommendedThrottleCap = 1f;
    [HideInInspector] public float selectedObstacleAheadM = 99f;
    [HideInInspector] public float detourTravelledM = 0f;
    [HideInInspector] public Vector3 adjustedGuidanceWorld;
    [HideInInspector] public bool localPlanFound;
    [HideInInspector] public int localPlanCellCount;

    private readonly List<Vector2Int> plannedCells = new List<Vector2Int>();
    private readonly List<Vector3> plannedLocalWorld = new List<Vector3>();

    // IMPORTANT: guidance filtering is stored as rover-relative bearing/distance.
    // The previous V3 stored a smoothed WORLD point. Because AdjustGuidance() is
    // called at telemetry cadence, that world point could be physically passed by
    // the rover and then appear 90-120 degrees behind it.
    private float filteredGuidanceBearingDeg;
    private float filteredGuidanceDistanceM;
    private bool haveFilteredGuidance;
    private float lastAdjustUnscaledTime = -1f;

    private Vector3 detourStartWorld;
    private bool haveDetourStart;
    private float detourStartTime = -1f;
    private float nominalClearSince = -1f;

    private int lockedSide = 0;

    private float HardRadius =>
        Mathf.Max(0.25f, roverFootprintRadiusM + safetyMarginM);

    private struct Node
    {
        public int r;
        public int c;
        public float f;

        public Node(int r, int c, float f)
        {
            this.r = r;
            this.c = c;
            this.f = f;
        }
    }

    public Vector3 AdjustGuidance(
        Vector3 nominalGuidanceWorld,
        float[] occupancy)
    {
        adjustedGuidanceWorld = nominalGuidanceWorld;
        recommendedThrottleCap = 1f;
        localPlanFound = false;
        localPlanCellCount = 0;

        float nowUnscaled =
            Time.unscaledTime;

        float guidanceDt =
            lastAdjustUnscaledTime < 0f
            ? 0.10f
            : Mathf.Clamp(
                nowUnscaled -
                lastAdjustUnscaledTime,
                0.01f,
                0.35f
            );

        lastAdjustUnscaledTime =
            nowUnscaled;

        if (!enableDynamicGuidanceAssist ||
            roverBody == null ||
            lidar == null ||
            occupancy == null ||
            occupancy.Length != lidar.gridSize * lidar.gridSize)
        {
            ReturnTowardNominal(nominalGuidanceWorld);
            return adjustedGuidanceWorld;
        }

        int n = lidar.gridSize;
        float width = lidar.gridWidthMeters;
        float cell = width / Mathf.Max(1, n);

        Vector3 nominalLocal3 =
            roverBody.transform.InverseTransformPoint(nominalGuidanceWorld);

        Vector2 nominalLocal =
            new Vector2(nominalLocal3.x, nominalLocal3.z);

        if (nominalLocal.magnitude < 0.5f)
        {
            ReturnTowardNominal(nominalGuidanceWorld);
            return adjustedGuidanceWorld;
        }

        Vector2 nominalDir =
            nominalLocal.normalized;

        bool[,] rawBlocked =
            BuildRawBlocked(occupancy, n);

        bool[,] inflated =
            InflateBlocked(
                rawBlocked,
                n,
                cell,
                HardRadius
            );

        nominalCorridorClearanceM =
            CorridorFreeMargin(
                rawBlocked,
                n,
                cell,
                nominalDir,
                localPlanHorizonM
            );

        bool nominalBlocked =
            nominalCorridorClearanceM <
            triggerFreeMarginM;

        if (assistActive && haveDetourStart)
        {
            Vector3 d =
                roverBody.position -
                detourStartWorld;

            d.y = 0f;
            detourTravelledM = d.magnitude;
        }
        else
        {
            detourTravelledM = 0f;
        }

        bool nominalClearlyFree =
            nominalCorridorClearanceM >=
            releaseFreeMarginM;

        if (nominalClearlyFree)
        {
            if (nominalClearSince < 0f)
                nominalClearSince = Time.time;
        }
        else
        {
            nominalClearSince = -1f;
        }

        bool releaseStable =
            nominalClearSince >= 0f &&
            Time.time - nominalClearSince >= clearStableSeconds;

        bool minimumTravelSatisfied =
            !haveDetourStart ||
            detourTravelledM >= minimumDetourTravelM;

        bool clearCommitTimeoutSatisfied =
            haveDetourStart &&
            detourStartTime >= 0f &&
            releaseStable &&
            Time.time - detourStartTime >=
                maxClearDetourCommitSeconds;

        bool releaseAllowed =
            releaseStable &&
            (
                minimumTravelSatisfied ||
                clearCommitTimeoutSatisfied
            );

        bool keepDetour =
            assistActive &&
            !releaseAllowed;

        if (!nominalBlocked && !keepDetour)
        {
            assistActive = false;
            chosenSide = 0;
            lockedSide = 0;
            haveDetourStart = false;
            detourStartTime = -1f;
            detourTravelledM = 0f;

            selectedCorridorClearanceM =
                nominalCorridorClearanceM;

            ReturnTowardNominal(
                nominalGuidanceWorld
            );

            return adjustedGuidanceWorld;
        }

        // Local A* start = rover centre.
        Vector2Int start =
            LocalToCell(
                Vector2.zero,
                n,
                width
            );

        // Plan toward a point farther down the current static-route direction.
        Vector2 anchorLocal =
            nominalDir *
            localPlanHorizonM;

        Vector2Int goal =
            LocalToCell(
                anchorLocal,
                n,
                width
            );

        // Keep a small start bubble free so tiny occupancy quantization around
        // the rover itself cannot invalidate the planner.
        ClearStartBubble(
            inflated,
            start,
            1
        );

        goal =
            FindNearestFree(
                inflated,
                goal,
                n
            );

        bool found =
            AStar(
                inflated,
                rawBlocked,
                start,
                goal,
                n,
                cell,
                nominalDir,
                lockedSide,
                out List<Vector2Int> path
            );

        if (!found && lockedSide != 0)
        {
            // If commitment became impossible, retry without side preference.
            found =
                AStar(
                    inflated,
                    rawBlocked,
                    start,
                    goal,
                    n,
                    cell,
                    nominalDir,
                    0,
                    out path
                );
        }

        if (!found || path == null || path.Count < 2)
        {
            localPlanFound = false;
            localPlanCellCount = 0;

            selectedCorridorClearanceM =
                nominalCorridorClearanceM;

            selectedObstacleAheadM =
                FirstObstacleAheadDistance(
                    rawBlocked,
                    n,
                    cell,
                    nominalDir
                );

            if (enableThrottleGovernor &&
                selectedObstacleAheadM <=
                emergencyObstacleDistanceM)
            {
                recommendedThrottleCap =
                    emergencyThrottleCap;
            }
            else
            {
                recommendedThrottleCap = 1f;
            }

            // Do not invent a second steering controller. Keep nominal target
            // if local planning genuinely cannot find a safe detour.
            ReturnTowardNominal(
                nominalGuidanceWorld
            );

            return adjustedGuidanceWorld;
        }

        localPlanFound = true;
        localPlanCellCount = path.Count;

        plannedCells.Clear();
        plannedCells.AddRange(path);

        plannedLocalWorld.Clear();

        for (int i = 0; i < path.Count; i++)
        {
            plannedLocalWorld.Add(
                CellToLocal(
                    path[i],
                    n,
                    width
                )
            );
        }

        Vector3 temporaryLocal =
            PointAlongLocalPath(
                plannedLocalWorld,
                localGuidanceLookaheadM
            );

        // Determine/lock which side of the nominal corridor the local path used.
        Vector2 left =
            new Vector2(
                -nominalDir.y,
                nominalDir.x
            );

        float signedOffset =
            Vector2.Dot(
                new Vector2(
                    temporaryLocal.x,
                    temporaryLocal.z
                ) -
                nominalDir *
                Mathf.Min(
                    localGuidanceLookaheadM,
                    new Vector2(
                        temporaryLocal.x,
                        temporaryLocal.z
                    ).magnitude
                ),
                left
            );

        int pathSide =
            Mathf.Abs(signedOffset) > 0.10f
            ? (signedOffset > 0f ? +1 : -1)
            : 0;

        if (!assistActive)
        {
            assistActive = true;
            detourStartWorld =
                roverBody.position;
            haveDetourStart = true;
            detourStartTime = Time.time;
            detourTravelledM = 0f;

            if (pathSide != 0)
                lockedSide = pathSide;
        }
        else if (lockedSide == 0 &&
                 pathSide != 0)
        {
            lockedSide = pathSide;
        }

        chosenSide = lockedSide;

        // ------------------------------------------------------------
        // KINEMATIC TARGET CONDITIONING
        //
        // The local A* is an 8-neighbour grid planner and can legitimately make
        // a sharp diagonal around an inflated rock. A real Ackermann rover should
        // not be commanded toward a waypoint that is almost lateral or behind.
        //
        // Keep the target decisively forward and cap the bearing presented to SAC.
        // This does NOT modify SAC steering; it gives SAC a feasible moving target.
        // ------------------------------------------------------------
        Vector2 desiredLocal2 =
            new Vector2(
                temporaryLocal.x,
                temporaryLocal.z
            );

        float desiredDistance =
            Mathf.Max(
                1.0f,
                desiredLocal2.magnitude
            );

        float desiredBearingDeg =
            Mathf.Atan2(
                desiredLocal2.x,
                desiredLocal2.y
            ) *
            Mathf.Rad2Deg;

        desiredBearingDeg =
            Mathf.Clamp(
                desiredBearingDeg,
                -maxGuidanceBearingDeg,
                +maxGuidanceBearingDeg
            );

        // Enforce a positive forward component. If the A* target would be too
        // sideways, extend it forward while keeping the capped bearing.
        float desiredBearingRad =
            desiredBearingDeg *
            Mathf.Deg2Rad;

        float cosBearing =
            Mathf.Max(
                0.10f,
                Mathf.Cos(
                    desiredBearingRad
                )
            );

        desiredDistance =
            Mathf.Max(
                desiredDistance,
                minGuidanceForwardM /
                cosBearing
            );

        if (!haveFilteredGuidance)
        {
            filteredGuidanceBearingDeg =
                desiredBearingDeg;

            filteredGuidanceDistanceM =
                desiredDistance;

            haveFilteredGuidance =
                true;
        }
        else
        {
            filteredGuidanceBearingDeg =
                Mathf.MoveTowardsAngle(
                    filteredGuidanceBearingDeg,
                    desiredBearingDeg,
                    guidanceBearingSlewDegPerSecond *
                    guidanceDt
                );

            filteredGuidanceBearingDeg =
                Mathf.Clamp(
                    filteredGuidanceBearingDeg,
                    -maxGuidanceBearingDeg,
                    +maxGuidanceBearingDeg
                );

            filteredGuidanceDistanceM =
                Mathf.MoveTowards(
                    filteredGuidanceDistanceM,
                    desiredDistance,
                    guidanceDistanceSlewMPerSecond *
                    guidanceDt
                );
        }

        float filteredBearingRad =
            filteredGuidanceBearingDeg *
            Mathf.Deg2Rad;

        Vector3 conditionedLocal =
            new Vector3(
                Mathf.Sin(filteredBearingRad) *
                    filteredGuidanceDistanceM,
                temporaryLocal.y,
                Mathf.Cos(filteredBearingRad) *
                    filteredGuidanceDistanceM
            );

        // Critical V4 fix:
        // Transform the CURRENT rover-relative target into world space every call.
        // We never carry a stale world-space waypoint from the previous telemetry tick.
        adjustedGuidanceWorld =
            roverBody.transform.TransformPoint(
                conditionedLocal
            );

        // Signed HUD offset relative to the nominal 6 m target.
        Vector3 deltaLocal =
            conditionedLocal -
            nominalLocal3;

        currentTargetOffsetM =
            Vector2.Dot(
                new Vector2(
                    deltaLocal.x,
                    deltaLocal.z
                ),
                left
            );

        selectedCorridorClearanceM =
            PathFreeMargin(
                rawBlocked,
                n,
                cell,
                plannedLocalWorld
            );

        selectedObstacleAheadM =
            FirstObstacleAheadDistance(
                rawBlocked,
                n,
                cell,
                new Vector2(
                    temporaryLocal.x,
                    temporaryLocal.z
                ).normalized
            );

        // PyBullet-like behavior:
        // leave ordinary throttle to SAC + RoverDrive's turn-speed scaling.
        // Only emergency-cap if explicitly enabled and dangerously close.
        if (enableThrottleGovernor &&
            selectedObstacleAheadM <=
            emergencyObstacleDistanceM)
        {
            recommendedThrottleCap =
                emergencyThrottleCap;
        }
        else
        {
            recommendedThrottleCap = 1f;
        }

        if (drawDebug)
        {
            Debug.DrawLine(
                roverBody.position + Vector3.up * 1.55f,
                nominalGuidanceWorld + Vector3.up * 1.55f,
                Color.yellow,
                0.15f
            );

            Debug.DrawLine(
                roverBody.position + Vector3.up * 1.75f,
                adjustedGuidanceWorld + Vector3.up * 1.75f,
                Color.magenta,
                0.15f
            );

            for (int i = 1;
                 i < plannedLocalWorld.Count;
                 i++)
            {
                Vector3 a =
                    roverBody.transform.TransformPoint(
                        plannedLocalWorld[i - 1]
                    );

                Vector3 b =
                    roverBody.transform.TransformPoint(
                        plannedLocalWorld[i]
                    );

                Debug.DrawLine(
                    a + Vector3.up * 0.35f,
                    b + Vector3.up * 0.35f,
                    Color.cyan,
                    0.15f
                );
            }
        }

        return adjustedGuidanceWorld;
    }

    private bool[,] BuildRawBlocked(
        float[] occupancy,
        int n)
    {
        bool[,] blocked =
            new bool[n, n];

        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                blocked[r, c] =
                    occupancy[
                        r * n + c
                    ] > 0.5f;
            }
        }

        return blocked;
    }

    private bool[,] InflateBlocked(
        bool[,] raw,
        int n,
        float cell,
        float radiusM)
    {
        bool[,] inflated =
            new bool[n, n];

        int cells =
            Mathf.CeilToInt(
                radiusM /
                Mathf.Max(1e-6f, cell)
            );

        float rSq =
            radiusM *
            radiusM;

        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                if (!raw[r, c])
                    continue;

                for (int dr = -cells;
                     dr <= cells;
                     dr++)
                {
                    for (int dc = -cells;
                         dc <= cells;
                         dc++)
                    {
                        int rr = r + dr;
                        int cc = c + dc;

                        if (rr < 0 || rr >= n ||
                            cc < 0 || cc >= n)
                        {
                            continue;
                        }

                        float dx =
                            dc * cell;

                        float dz =
                            dr * cell;

                        if (dx * dx +
                            dz * dz <= rSq)
                        {
                            inflated[rr, cc] =
                                true;
                        }
                    }
                }
            }
        }

        return inflated;
    }

    private void ClearStartBubble(
        bool[,] blocked,
        Vector2Int start,
        int cells)
    {
        int n =
            blocked.GetLength(0);

        for (int dr = -cells;
             dr <= cells;
             dr++)
        {
            for (int dc = -cells;
                 dc <= cells;
                 dc++)
            {
                int rr =
                    start.x + dr;

                int cc =
                    start.y + dc;

                if (rr >= 0 && rr < n &&
                    cc >= 0 && cc < n)
                {
                    blocked[rr, cc] =
                        false;
                }
            }
        }
    }

    private Vector2Int FindNearestFree(
        bool[,] blocked,
        Vector2Int goal,
        int n)
    {
        if (!blocked[goal.x, goal.y])
            return goal;

        for (int radius = 1;
             radius < n;
             radius++)
        {
            for (int dr = -radius;
                 dr <= radius;
                 dr++)
            {
                for (int dc = -radius;
                     dc <= radius;
                     dc++)
                {
                    if (Mathf.Abs(dr) != radius &&
                        Mathf.Abs(dc) != radius)
                    {
                        continue;
                    }

                    int rr =
                        goal.x + dr;

                    int cc =
                        goal.y + dc;

                    if (rr < 0 || rr >= n ||
                        cc < 0 || cc >= n)
                    {
                        continue;
                    }

                    if (!blocked[rr, cc])
                    {
                        return new Vector2Int(
                            rr,
                            cc
                        );
                    }
                }
            }
        }

        return goal;
    }

    private bool AStar(
        bool[,] blocked,
        bool[,] rawBlocked,
        Vector2Int start,
        Vector2Int goal,
        int n,
        float cell,
        Vector2 nominalDir,
        int sidePreference,
        out List<Vector2Int> path)
    {
        path =
            null;

        float[,] g =
            new float[n, n];

        bool[,] closed =
            new bool[n, n];

        Vector2Int[,] parent =
            new Vector2Int[n, n];

        bool[,] haveParent =
            new bool[n, n];

        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
                g[r, c] = float.PositiveInfinity;
        }

        List<Node> open =
            new List<Node>();

        g[start.x, start.y] = 0f;

        open.Add(
            new Node(
                start.x,
                start.y,
                Heuristic(start, goal)
            )
        );

        int[] dR =
        {
            -1, -1, -1,
             0,       0,
             1,  1,  1
        };

        int[] dC =
        {
            -1, 0, 1,
            -1,    1,
            -1, 0, 1
        };

        int guard = 0;

        while (open.Count > 0 &&
               guard++ < 10000)
        {
            int bestIndex = 0;

            for (int i = 1;
                 i < open.Count;
                 i++)
            {
                if (open[i].f <
                    open[bestIndex].f)
                {
                    bestIndex = i;
                }
            }

            Node cur =
                open[bestIndex];

            open.RemoveAt(
                bestIndex
            );

            if (closed[cur.r, cur.c])
                continue;

            closed[cur.r, cur.c] =
                true;

            if (cur.r == goal.x &&
                cur.c == goal.y)
            {
                path =
                    Reconstruct(
                        parent,
                        haveParent,
                        start,
                        goal
                    );

                return
                    path != null &&
                    path.Count >= 2;
            }

            for (int k = 0;
                 k < 8;
                 k++)
            {
                int rr =
                    cur.r + dR[k];

                int cc =
                    cur.c + dC[k];

                if (rr < 0 || rr >= n ||
                    cc < 0 || cc >= n)
                {
                    continue;
                }

                if (blocked[rr, cc] ||
                    closed[rr, cc])
                {
                    continue;
                }

                // Prevent diagonal corner-cutting through inflated obstacles.
                if (dR[k] != 0 &&
                    dC[k] != 0)
                {
                    if (blocked[cur.r, cc] ||
                        blocked[rr, cur.c])
                    {
                        continue;
                    }
                }

                float step =
                    (dR[k] == 0 ||
                     dC[k] == 0)
                    ? 1f
                    : 1.41421356f;

                float extra =
                    NearObstacleCost(
                        rawBlocked,
                        rr,
                        cc,
                        n
                    ) *
                    nearObstaclePenalty;

                if (preferCommittedSide &&
                    sidePreference != 0)
                {
                    Vector3 local =
                        CellToLocal(
                            new Vector2Int(
                                rr,
                                cc
                            ),
                            n,
                            lidar.gridWidthMeters
                        );

                    Vector2 left =
                        new Vector2(
                            -nominalDir.y,
                            nominalDir.x
                        );

                    float lateral =
                        Vector2.Dot(
                            new Vector2(
                                local.x,
                                local.z
                            ),
                            left
                        );

                    int cellSide =
                        Mathf.Abs(lateral) < 0.15f
                        ? 0
                        : (lateral > 0f ? +1 : -1);

                    if (cellSide != 0 &&
                        cellSide != sidePreference)
                    {
                        extra +=
                            oppositeSidePenalty;
                    }
                }

                float candidate =
                    g[cur.r, cur.c] +
                    step +
                    extra;

                if (candidate <
                    g[rr, cc])
                {
                    g[rr, cc] =
                        candidate;

                    parent[rr, cc] =
                        new Vector2Int(
                            cur.r,
                            cur.c
                        );

                    haveParent[rr, cc] =
                        true;

                    float f =
                        candidate +
                        Heuristic(
                            new Vector2Int(
                                rr,
                                cc
                            ),
                            goal
                        );

                    open.Add(
                        new Node(
                            rr,
                            cc,
                            f
                        )
                    );
                }
            }
        }

        return false;
    }

    private float NearObstacleCost(
        bool[,] raw,
        int r,
        int c,
        int n)
    {
        int maxRing = 3;

        for (int ring = 1;
             ring <= maxRing;
             ring++)
        {
            for (int dr = -ring;
                 dr <= ring;
                 dr++)
            {
                for (int dc = -ring;
                     dc <= ring;
                     dc++)
                {
                    if (Mathf.Abs(dr) != ring &&
                        Mathf.Abs(dc) != ring)
                    {
                        continue;
                    }

                    int rr =
                        r + dr;

                    int cc =
                        c + dc;

                    if (rr < 0 || rr >= n ||
                        cc < 0 || cc >= n)
                    {
                        continue;
                    }

                    if (raw[rr, cc])
                    {
                        return
                            (maxRing - ring + 1) /
                            (float)maxRing;
                    }
                }
            }
        }

        return 0f;
    }

    private List<Vector2Int> Reconstruct(
        Vector2Int[,] parent,
        bool[,] haveParent,
        Vector2Int start,
        Vector2Int goal)
    {
        List<Vector2Int> rev =
            new List<Vector2Int>();

        Vector2Int cur =
            goal;

        rev.Add(cur);

        int guard = 0;

        while (cur != start &&
               guard++ < 4096)
        {
            if (!haveParent[
                    cur.x,
                    cur.y
                ])
            {
                return null;
            }

            cur =
                parent[
                    cur.x,
                    cur.y
                ];

            rev.Add(cur);
        }

        rev.Reverse();

        return rev;
    }

    private float Heuristic(
        Vector2Int a,
        Vector2Int b)
    {
        float dr =
            a.x - b.x;

        float dc =
            a.y - b.y;

        return
            Mathf.Sqrt(
                dr * dr +
                dc * dc
            );
    }

    private Vector2Int LocalToCell(
        Vector2 local,
        int n,
        float width)
    {
        float half =
            width * 0.5f;

        int col =
            Mathf.FloorToInt(
                ((local.x + half) /
                 width) *
                n
            );

        int row =
            Mathf.FloorToInt(
                ((half - local.y) /
                 width) *
                n
            );

        row =
            Mathf.Clamp(
                row,
                0,
                n - 1
            );

        col =
            Mathf.Clamp(
                col,
                0,
                n - 1
            );

        return
            new Vector2Int(
                row,
                col
            );
    }

    private Vector3 CellToLocal(
        Vector2Int rc,
        int n,
        float width)
    {
        float half =
            width * 0.5f;

        float x =
            ((rc.y + 0.5f) /
             n) *
            width -
            half;

        float z =
            half -
            ((rc.x + 0.5f) /
             n) *
            width;

        return
            new Vector3(
                x,
                0f,
                z
            );
    }

    private Vector3 PointAlongLocalPath(
        List<Vector3> path,
        float distanceM)
    {
        if (path == null ||
            path.Count == 0)
        {
            return
                new Vector3(
                    0f,
                    0f,
                    Mathf.Max(
                        1f,
                        distanceM
                    )
                );
        }

        if (path.Count == 1)
            return path[0];

        float remaining =
            Mathf.Max(
                0f,
                distanceM
            );

        for (int i = 1;
             i < path.Count;
             i++)
        {
            Vector3 a =
                path[i - 1];

            Vector3 b =
                path[i];

            float seg =
                Vector3.Distance(
                    a,
                    b
                );

            if (seg >= remaining)
            {
                float t =
                    seg > 1e-6f
                    ? remaining / seg
                    : 0f;

                return
                    Vector3.Lerp(
                        a,
                        b,
                        t
                    );
            }

            remaining -= seg;
        }

        return
            path[
                path.Count - 1
            ];
    }

    private float CorridorFreeMargin(
        bool[,] raw,
        int n,
        float cell,
        Vector2 dir,
        float horizon)
    {
        float nearest =
            float.PositiveInfinity;

        Vector2 d =
            dir.normalized;

        float width =
            lidar.gridWidthMeters;

        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                if (!raw[r, c])
                    continue;

                Vector3 local3 =
                    CellToLocal(
                        new Vector2Int(
                            r,
                            c
                        ),
                        n,
                        width
                    );

                Vector2 p =
                    new Vector2(
                        local3.x,
                        local3.z
                    );

                float along =
                    Vector2.Dot(
                        p,
                        d
                    );

                if (along < 0f ||
                    along > horizon)
                {
                    continue;
                }

                Vector2 q =
                    d *
                    along;

                float lateral =
                    Vector2.Distance(
                        p,
                        q
                    );

                nearest =
                    Mathf.Min(
                        nearest,
                        lateral
                    );
            }
        }

        if (float.IsPositiveInfinity(nearest))
            return 99f;

        return
            nearest -
            HardRadius -
            0.5f * cell;
    }

    private float PathFreeMargin(
        bool[,] raw,
        int n,
        float cell,
        List<Vector3> path)
    {
        if (path == null ||
            path.Count < 2)
        {
            return 99f;
        }

        float width =
            lidar.gridWidthMeters;

        float nearest =
            float.PositiveInfinity;

        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                if (!raw[r, c])
                    continue;

                Vector3 p3 =
                    CellToLocal(
                        new Vector2Int(
                            r,
                            c
                        ),
                        n,
                        width
                    );

                Vector2 p =
                    new Vector2(
                        p3.x,
                        p3.z
                    );

                for (int i = 1;
                     i < path.Count;
                     i++)
                {
                    Vector2 a =
                        new Vector2(
                            path[i - 1].x,
                            path[i - 1].z
                        );

                    Vector2 b =
                        new Vector2(
                            path[i].x,
                            path[i].z
                        );

                    Vector2 ab =
                        b - a;

                    float lenSq =
                        Mathf.Max(
                            1e-6f,
                            ab.sqrMagnitude
                        );

                    float t =
                        Mathf.Clamp01(
                            Vector2.Dot(
                                p - a,
                                ab
                            ) /
                            lenSq
                        );

                    Vector2 q =
                        a +
                        t * ab;

                    nearest =
                        Mathf.Min(
                            nearest,
                            Vector2.Distance(
                                p,
                                q
                            )
                        );
                }
            }
        }

        if (float.IsPositiveInfinity(nearest))
            return 99f;

        return
            nearest -
            HardRadius -
            0.5f * cell;
    }

    private float FirstObstacleAheadDistance(
        bool[,] raw,
        int n,
        float cell,
        Vector2 dir)
    {
        float first =
            float.PositiveInfinity;

        Vector2 d =
            dir.sqrMagnitude > 1e-8f
            ? dir.normalized
            : Vector2.up;

        float width =
            lidar.gridWidthMeters;

        float collisionBand =
            HardRadius +
            0.5f * cell;

        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                if (!raw[r, c])
                    continue;

                Vector3 p3 =
                    CellToLocal(
                        new Vector2Int(
                            r,
                            c
                        ),
                        n,
                        width
                    );

                Vector2 p =
                    new Vector2(
                        p3.x,
                        p3.z
                    );

                float along =
                    Vector2.Dot(
                        p,
                        d
                    );

                if (along <= 0f)
                    continue;

                Vector2 q =
                    d * along;

                float lateral =
                    Vector2.Distance(
                        p,
                        q
                    );

                if (lateral <=
                    collisionBand)
                {
                    first =
                        Mathf.Min(
                            first,
                            along
                        );
                }
            }
        }

        return
            float.IsPositiveInfinity(first)
            ? 99f
            : first;
    }

    private void ReturnTowardNominal(
        Vector3 nominalGuidanceWorld)
    {
        recommendedThrottleCap = 1f;
        selectedObstacleAheadM = 99f;
        localPlanFound = false;
        localPlanCellCount = 0;

        // Once local avoidance is no longer required, hand the ORIGINAL current
        // moving A* target straight back to SAC. Do not chase an old world-space
        // target and do not make release depend on rover speed.
        adjustedGuidanceWorld =
            nominalGuidanceWorld;

        currentTargetOffsetM = 0f;

        haveFilteredGuidance = false;
        filteredGuidanceBearingDeg = 0f;
        filteredGuidanceDistanceM = 0f;
    }
}
