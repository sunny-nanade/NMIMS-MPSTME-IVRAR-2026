using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Runtime-only dynamic rock injector for the V1.5f Unity test.
///
/// It DOES NOT regenerate/replan A*. Instead it introduces a small number of
/// previously unseen rocks near the already-safe A* corridor while the rover
/// is driving. The placements are deliberately chosen to:
///
/// - appear well before the 6 m moving A* target reaches them;
/// - enter the 12 m LiDAR field with several seconds of warning;
/// - avoid sharp A* corners, where guidance + avoidance can fight each other;
/// - partially intrude into the rover corridor rather than hard-blocking the
///   path centerline by default;
/// - be separated along the route so recovery/re-acquisition can finish before
///   the next challenge.
///
/// This is a robustness / reactive-avoidance test. Dynamic rocks should NOT be
/// exported into the static obstacle-aware global path.
/// </summary>
public class RuntimePathRockSpawner : MonoBehaviour
{
    [Header("Master Toggle")]
    [Tooltip("Turn OFF before Play for the normal static V1.5f benchmark.")]
    public bool enableRuntimeRocks = false;

    [Header("References")]
    public Terrain terrain;
    public Transform rover;
    public GameObject rockPrefab;

    [Header("A* Path")]
    [Tooltip("Must match the CSV used by GlobalPathRenderer and UnityUdpBridge.")]
    public string globalPathCsvFileName = "global_path_v15f_rocks_seed71.csv";

    [Header("Random Challenge")]
    [Range(0, 7)]
    public int randomRockCount = 4;

    public int randomSeed = 7171;

    [Header("Where Along the Route")]
    [Tooltip("No dynamic rock before this much A* arc length has elapsed.")]
    public float minPathDistanceFromStartM = 22.0f;

    [Tooltip("Keep the last part of the mission free so the rover can settle and finish.")]
    public float minPathDistanceFromGoalM = 16.0f;

    [Tooltip("Minimum A* arc-length separation between dynamic rocks.")]
    public float minRockSpacingAlongPathM = 17.0f;

    [Header("When the Rock Appears")]
    [Tooltip("Rock is instantiated this many metres of A* arc length ahead of the rover. 14 m means it exists before entering a 12 m LiDAR and well before the 6 m target reaches it.")]
    public float spawnLeadDistanceM = 14.0f;

    [Tooltip("Never create a rock if the trigger was missed and it would appear this close to the rover.")]
    public float minimumSafeSpawnAheadM = 9.0f;

    [Tooltip("Wait briefly after Play starts before enabling runtime injection.")]
    public float startupDelaySeconds = 2.0f;

    [Header("Avoid A* Corners")]
    [Tooltip("Dynamic obstacles are placed only on relatively straight path sections, avoiding a guidance/avoidance conflict at sharp corners.")]
    public float curvatureProbeDistanceM = 3.0f;

    [Range(5f, 45f)]
    public float maxAllowedPathTurnDeg = 18.0f;

    [Header("Rock Size")]
    [Tooltip("Dynamic rocks are intentionally moderate; use the static field for the largest boulders.")]
    public float minRockRadiusM = 0.45f;

    public float maxRockRadiusM = 0.90f;

    [Range(1f, 4f)]
    public float smallRockBias = 1.6f;

    [Header("Partial Path Intrusion")]
    [Tooltip("Rock centers are offset from the A* centreline. These values make the obstacle intrude into the rover footprint corridor while leaving a clear side to pass.")]
    public float minLateralOffsetM = 1.25f;

    public float maxLateralOffsetM = 1.80f;

    [Tooltip("Probability of putting the rock exactly on the centreline. Keep 0 for the first V1.5f robustness test.")]
    [Range(0f, 1f)]
    public float centerlineBlockProbability = 0.0f;

    [Header("Placement Safety")]
    public string obstacleLayerName = "Obstacle";

    [Tooltip("Avoid spawning the new rock on top of an existing static/dynamic obstacle.")]
    public LayerMask obstacleMask;

    [Tooltip("Extra gap from an existing collider during placement.")]
    public float existingObstacleGapM = 0.60f;

    [Range(5f, 40f)]
    public float maxPlacementSlopeDeg = 22.0f;

    [Tooltip("If the initially chosen side is occupied, try the opposite side automatically.")]
    public bool tryOppositeSideIfBlocked = true;

    [Header("Appearance / Terrain Snap")]
    public bool randomYaw = true;
    public bool randomTilt = true;

    [Range(0f, 15f)]
    public float maxTiltDeg = 7.0f;

    [Range(0f, 0.15f)]
    public float visualScaleJitter = 0.04f;

    [Tooltip("Snap using collider bounds and a 3x3 terrain sample under the final footprint.")]
    public bool robustTerrainSnap = true;

    [Range(0f, 0.08f)]
    public float terrainEmbedM = 0.025f;

    [Header("Debug")]
    public bool logEvents = true;

    [Tooltip("Show planned event positions in Scene view before they spawn.")]
    public bool drawPlannedPositions = false;

    public Color plannedGizmoColor = new Color(1f, 0.2f, 1f, 0.65f);

    [Header("Runtime Information")]
    [SerializeField] private bool pathLoaded;
    [SerializeField] private int plannedRockCount;
    [SerializeField] private int spawnedRockCount;
    [SerializeField] private float roverArcLengthM;
    [SerializeField] private float nextRockAheadM = -1f;
    [SerializeField] private string lastEvent = "";

    private readonly List<Vector2> pathXZ = new List<Vector2>();
    private readonly List<float> cumulative = new List<float>();

    private class RockEvent
    {
        public float pathS;
        public int preferredSide;  // +1 / -1
        public float lateralOffset;
        public float radius;
        public bool spawned;
        public bool skipped;
        public Vector3 plannedWorld;
    }

    private readonly List<RockEvent> events = new List<RockEvent>();

    private GameObject runtimeParent;
    private System.Random rng;
    private float totalPathLengthM;
    private bool initialized;

    private void Start()
    {
        if (!enableRuntimeRocks)
        {
            lastEvent = "Runtime rocks disabled";
            return;
        }

        if (!ValidateReferences())
        {
            enableRuntimeRocks = false;
            return;
        }

        rng = new System.Random(randomSeed);

        if (!LoadPath())
        {
            enableRuntimeRocks = false;
            return;
        }

        BuildChallengeSchedule();

        runtimeParent = new GameObject("Runtime_Path_Rocks");
        runtimeParent.transform.SetParent(transform);
        runtimeParent.transform.localPosition = Vector3.zero;
        runtimeParent.transform.localRotation = Quaternion.identity;
        runtimeParent.transform.localScale = Vector3.one;

        initialized = true;

        if (logEvents)
        {
            Debug.Log(
                "V1.5f RUNTIME ROCK INJECTOR READY\n" +
                "Planned rocks: " + plannedRockCount + "\n" +
                "Seed: " + randomSeed + "\n" +
                "A* path length: " + totalPathLengthM.ToString("F1") + " m\n" +
                "Spawn lead: " + spawnLeadDistanceM.ToString("F1") + " m\n" +
                "Lateral offset: " +
                    minLateralOffsetM.ToString("F2") + ".." +
                    maxLateralOffsetM.ToString("F2") + " m"
            );
        }
    }

    private void Update()
    {
        if (!enableRuntimeRocks ||
            !initialized ||
            rover == null ||
            Time.timeSinceLevelLoad < startupDelaySeconds)
        {
            return;
        }

        roverArcLengthM = ProjectArcLength(
            new Vector2(
                rover.position.x,
                rover.position.z
            )
        );

        nextRockAheadM = -1f;

        for (int i = 0; i < events.Count; i++)
        {
            RockEvent e = events[i];

            if (e.spawned || e.skipped)
                continue;

            float ahead =
                e.pathS - roverArcLengthM;

            if (nextRockAheadM < 0f ||
                ahead < nextRockAheadM)
            {
                nextRockAheadM = ahead;
            }

            if (ahead > spawnLeadDistanceM)
                continue;

            if (ahead < minimumSafeSpawnAheadM)
            {
                e.skipped = true;
                lastEvent =
                    "Skipped event " + (i + 1) +
                    " because trigger was late (" +
                    ahead.ToString("F1") + " m ahead)";

                if (logEvents)
                    Debug.LogWarning(lastEvent);

                continue;
            }

            TrySpawnEvent(e, i);
        }
    }

    private bool ValidateReferences()
    {
        if (terrain == null)
        {
            Debug.LogError("RuntimePathRockSpawner: assign Terrain_HiRISE.");
            return false;
        }

        if (rover == null)
        {
            Debug.LogError("RuntimePathRockSpawner: assign Rover root Transform.");
            return false;
        }

        if (rockPrefab == null)
        {
            Debug.LogError("RuntimePathRockSpawner: assign rock prefab.");
            return false;
        }

        int obstacleLayer =
            LayerMask.NameToLayer(obstacleLayerName);

        if (obstacleLayer < 0)
        {
            Debug.LogError(
                "RuntimePathRockSpawner: layer '" +
                obstacleLayerName + "' does not exist."
            );
            return false;
        }

        if (randomRockCount < 0 ||
            randomRockCount > 7)
        {
            Debug.LogError(
                "RuntimePathRockSpawner: randomRockCount must be 0..7."
            );
            return false;
        }

        if (minRockRadiusM <= 0f ||
            maxRockRadiusM < minRockRadiusM)
        {
            Debug.LogError("RuntimePathRockSpawner: invalid rock radius range.");
            return false;
        }

        return true;
    }

    private bool LoadPath()
    {
        pathXZ.Clear();
        cumulative.Clear();
        pathLoaded = false;

        string path =
            Path.Combine(
                Application.streamingAssetsPath,
                globalPathCsvFileName
            );

        if (!File.Exists(path))
        {
            Debug.LogError(
                "RuntimePathRockSpawner: A* CSV not found: " + path
            );
            return false;
        }

        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();

            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith("#") ||
                line.StartsWith("x", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] p = line.Split(',');

            if (p.Length < 2)
                continue;

            if (!float.TryParse(
                    p[0],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float x))
            {
                continue;
            }

            if (!float.TryParse(
                    p[1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float z))
            {
                continue;
            }

            pathXZ.Add(new Vector2(x, z));
        }

        if (pathXZ.Count < 2)
        {
            Debug.LogError(
                "RuntimePathRockSpawner: A* CSV has fewer than 2 usable points."
            );
            return false;
        }

        cumulative.Add(0f);

        float sum = 0f;

        for (int i = 1; i < pathXZ.Count; i++)
        {
            sum +=
                Vector2.Distance(
                    pathXZ[i - 1],
                    pathXZ[i]
                );

            cumulative.Add(sum);
        }

        totalPathLengthM = sum;
        pathLoaded = true;

        return true;
    }

    private void BuildChallengeSchedule()
    {
        events.Clear();
        spawnedRockCount = 0;

        int wanted =
            Mathf.Clamp(
                randomRockCount,
                0,
                7
            );

        if (wanted == 0)
        {
            plannedRockCount = 0;
            return;
        }

        float usableStart =
            Mathf.Max(
                minPathDistanceFromStartM,
                spawnLeadDistanceM + 2f
            );

        float usableEnd =
            totalPathLengthM -
            minPathDistanceFromGoalM;

        if (usableEnd <= usableStart)
        {
            Debug.LogError(
                "RuntimePathRockSpawner: path is too short for the configured " +
                "start/goal buffers."
            );
            plannedRockCount = 0;
            return;
        }

        int attempts = 0;
        int maxAttempts = 4000;

        while (events.Count < wanted &&
               attempts < maxAttempts)
        {
            attempts++;

            float s =
                Range(
                    rng,
                    usableStart,
                    usableEnd
                );

            if (!IsFarEnoughFromOtherEvents(s))
                continue;

            if (!IsLocallyStraightEnough(s))
                continue;

            RockEvent e =
                new RockEvent();

            e.pathS = s;
            e.preferredSide =
                rng.NextDouble() < 0.5
                ? -1
                : +1;

            e.radius =
                SampleRockRadius();

            bool centerBlock =
                Next01(rng) <
                centerlineBlockProbability;

            e.lateralOffset =
                centerBlock
                ? 0f
                : Range(
                    rng,
                    minLateralOffsetM,
                    maxLateralOffsetM
                );

            Vector2 pathPoint =
                PointAtArc(s);

            Vector2 tangent =
                TangentAtArc(s);

            Vector2 lateral =
                new Vector2(
                    -tangent.y,
                    tangent.x
                );

            Vector2 candidate =
                pathPoint +
                lateral *
                e.preferredSide *
                e.lateralOffset;

            float y =
                terrain.SampleHeight(
                    new Vector3(
                        candidate.x,
                        0f,
                        candidate.y
                    )
                )
                + terrain.transform.position.y;

            e.plannedWorld =
                new Vector3(
                    candidate.x,
                    y,
                    candidate.y
                );

            events.Add(e);
        }

        events.Sort(
            (a, b) =>
                a.pathS.CompareTo(b.pathS)
        );

        plannedRockCount =
            events.Count;

        if (plannedRockCount < wanted)
        {
            Debug.LogWarning(
                "RuntimePathRockSpawner: planned only " +
                plannedRockCount + "/" + wanted +
                " rocks after curvature/spacing checks. " +
                "Reduce minRockSpacingAlongPathM slightly if you require more."
            );
        }
    }

    private bool IsFarEnoughFromOtherEvents(float s)
    {
        for (int i = 0; i < events.Count; i++)
        {
            if (Mathf.Abs(events[i].pathS - s) <
                minRockSpacingAlongPathM)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsLocallyStraightEnough(float s)
    {
        float probe =
            Mathf.Max(
                0.5f,
                curvatureProbeDistanceM
            );

        Vector2 a =
            TangentAtArc(
                Mathf.Max(0f, s - probe)
            );

        Vector2 b =
            TangentAtArc(
                Mathf.Min(
                    totalPathLengthM,
                    s + probe
                )
            );

        float angle =
            Vector2.Angle(a, b);

        return angle <=
               maxAllowedPathTurnDeg;
    }

    private void TrySpawnEvent(
        RockEvent e,
        int eventIndex
    )
    {
        Vector2 pathPoint =
            PointAtArc(e.pathS);

        Vector2 tangent =
            TangentAtArc(e.pathS);

        if (tangent.sqrMagnitude < 1e-6f)
        {
            SkipEvent(e, eventIndex, "invalid path tangent");
            return;
        }

        tangent.Normalize();

        Vector2 lateral =
            new Vector2(
                -tangent.y,
                tangent.x
            );

        int[] sideOrder =
            tryOppositeSideIfBlocked
            ? new int[] { e.preferredSide, -e.preferredSide }
            : new int[] { e.preferredSide };

        Vector3 chosen =
            Vector3.zero;

        bool found =
            false;

        int chosenSide =
            e.preferredSide;

        for (int s = 0; s < sideOrder.Length; s++)
        {
            int side =
                sideOrder[s];

            float offset =
                e.lateralOffset;

            Vector2 xz =
                pathPoint +
                lateral *
                side *
                offset;

            Vector3 world =
                new Vector3(
                    xz.x,
                    0f,
                    xz.y
                );

            float nx =
                Mathf.Clamp01(
                    (world.x -
                     terrain.transform.position.x) /
                    Mathf.Max(
                        1e-6f,
                        terrain.terrainData.size.x
                    )
                );

            float nz =
                Mathf.Clamp01(
                    (world.z -
                     terrain.transform.position.z) /
                    Mathf.Max(
                        1e-6f,
                        terrain.terrainData.size.z
                    )
                );

            Vector3 normal =
                terrain.terrainData.GetInterpolatedNormal(
                    nx,
                    nz
                );

            float slope =
                Vector3.Angle(
                    normal,
                    Vector3.up
                );

            if (slope > maxPlacementSlopeDeg)
                continue;

            float terrainY =
                terrain.SampleHeight(world) +
                terrain.transform.position.y;

            world.y = terrainY;

            float checkRadius =
                e.radius +
                Mathf.Max(
                    0f,
                    existingObstacleGapM
                );

            Collider[] hits =
                Physics.OverlapSphere(
                    world +
                    Vector3.up * e.radius,
                    checkRadius,
                    obstacleMask,
                    QueryTriggerInteraction.Ignore
                );

            if (hits != null &&
                hits.Length > 0)
            {
                continue;
            }

            chosen = world;
            chosenSide = side;
            found = true;
            break;
        }

        if (!found)
        {
            SkipEvent(
                e,
                eventIndex,
                "both lateral placements overlap existing obstacles or steep terrain"
            );
            return;
        }

        int obstacleLayer =
            LayerMask.NameToLayer(
                obstacleLayerName
            );

        GameObject rock =
            Instantiate(
                rockPrefab,
                chosen,
                Quaternion.identity,
                runtimeParent != null
                    ? runtimeParent.transform
                    : transform
            );

        rock.name =
            "RuntimeRock_" +
            (spawnedRockCount + 1).ToString("00");

        SetLayerRecursively(
            rock,
            obstacleLayer
        );

        float yaw =
            randomYaw
            ? Range(rng, 0f, 360f)
            : 0f;

        float tiltX =
            randomTilt
            ? Range(rng, -maxTiltDeg, maxTiltDeg)
            : 0f;

        float tiltZ =
            randomTilt
            ? Range(rng, -maxTiltDeg, maxTiltDeg)
            : 0f;

        rock.transform.rotation =
            Quaternion.Euler(
                tiltX,
                yaw,
                tiltZ
            );

        Physics.SyncTransforms();

        float currentRadius =
            GetHorizontalPhysicalRadius(
                rock
            );

        if (currentRadius > 1e-4f)
        {
            float jitter =
                1f + Range(
                    rng,
                    -visualScaleJitter,
                    visualScaleJitter
                );

            float scaleFactor =
                (e.radius / currentRadius) *
                jitter;

            rock.transform.localScale *=
                scaleFactor;
        }

        Physics.SyncTransforms();

        SnapRockToTerrain(
            rock
        );

        e.spawned = true;
        e.preferredSide = chosenSide;
        spawnedRockCount++;

        float ahead =
            e.pathS - roverArcLengthM;

        lastEvent =
            "Spawned " + rock.name +
            " " + ahead.ToString("F1") +
            " m ahead at path s=" +
            e.pathS.ToString("F1") +
            " m";

        if (logEvents)
        {
            Debug.Log(
                "V1.5f DYNAMIC ROCK SPAWNED\n" +
                "Event: " + (eventIndex + 1) +
                "/" + plannedRockCount + "\n" +
                "Name: " + rock.name + "\n" +
                "Path arc: " + e.pathS.ToString("F1") + " m\n" +
                "Ahead of rover: " + ahead.ToString("F1") + " m\n" +
                "Side: " + (chosenSide > 0 ? "left-of-path" : "right-of-path") + "\n" +
                "Lateral offset: " + e.lateralOffset.ToString("F2") + " m\n" +
                "Target radius: " + e.radius.ToString("F2") + " m"
            );
        }
    }

    private void SkipEvent(
        RockEvent e,
        int eventIndex,
        string reason
    )
    {
        e.skipped = true;

        lastEvent =
            "Skipped event " +
            (eventIndex + 1) +
            ": " + reason;

        if (logEvents)
            Debug.LogWarning(lastEvent);
    }

    private float ProjectArcLength(
        Vector2 point
    )
    {
        float bestSq =
            float.PositiveInfinity;

        float bestS =
            0f;

        for (int i = 0;
             i < pathXZ.Count - 1;
             i++)
        {
            Vector2 a =
                pathXZ[i];

            Vector2 b =
                pathXZ[i + 1];

            Vector2 ab =
                b - a;

            float lenSq =
                ab.sqrMagnitude;

            float t =
                lenSq > 1e-8f
                ? Mathf.Clamp01(
                    Vector2.Dot(
                        point - a,
                        ab
                    ) / lenSq
                  )
                : 0f;

            Vector2 q =
                a +
                t * ab;

            float sq =
                (point - q).sqrMagnitude;

            if (sq < bestSq)
            {
                bestSq = sq;

                float segLen =
                    Mathf.Sqrt(lenSq);

                bestS =
                    cumulative[i] +
                    t * segLen;
            }
        }

        return bestS;
    }

    private Vector2 PointAtArc(float s)
    {
        if (pathXZ.Count == 0)
            return Vector2.zero;

        if (s <= 0f)
            return pathXZ[0];

        if (s >= totalPathLengthM)
            return pathXZ[pathXZ.Count - 1];

        int i =
            FindSegmentForArc(s);

        float segStart =
            cumulative[i];

        float segEnd =
            cumulative[i + 1];

        float denom =
            Mathf.Max(
                1e-6f,
                segEnd - segStart
            );

        float t =
            Mathf.Clamp01(
                (s - segStart) /
                denom
            );

        return Vector2.Lerp(
            pathXZ[i],
            pathXZ[i + 1],
            t
        );
    }

    private Vector2 TangentAtArc(float s)
    {
        if (pathXZ.Count < 2)
            return Vector2.up;

        s =
            Mathf.Clamp(
                s,
                0f,
                totalPathLengthM
            );

        int i =
            FindSegmentForArc(s);

        Vector2 d =
            pathXZ[i + 1] -
            pathXZ[i];

        if (d.sqrMagnitude < 1e-8f)
            return Vector2.up;

        return d.normalized;
    }

    private int FindSegmentForArc(float s)
    {
        int lo = 0;
        int hi = cumulative.Count - 2;

        while (lo <= hi)
        {
            int mid =
                (lo + hi) / 2;

            if (s < cumulative[mid])
            {
                hi = mid - 1;
            }
            else if (s >
                     cumulative[mid + 1])
            {
                lo = mid + 1;
            }
            else
            {
                return mid;
            }
        }

        return Mathf.Clamp(
            lo,
            0,
            cumulative.Count - 2
        );
    }

    private float SampleRockRadius()
    {
        float u =
            Mathf.Clamp01(
                Next01(rng)
            );

        float biased =
            Mathf.Pow(
                u,
                Mathf.Max(
                    1f,
                    smallRockBias
                )
            );

        return Mathf.Lerp(
            minRockRadiusM,
            maxRockRadiusM,
            biased
        );
    }

    private float GetHorizontalPhysicalRadius(
        GameObject rock
    )
    {
        if (TryGetCombinedColliderBounds(
                rock,
                out Bounds c))
        {
            return Mathf.Max(
                c.extents.x,
                c.extents.z
            );
        }

        if (TryGetCombinedRendererBounds(
                rock,
                out Bounds r))
        {
            return Mathf.Max(
                r.extents.x,
                r.extents.z
            );
        }

        return 0f;
    }

    private void SnapRockToTerrain(
        GameObject rock
    )
    {
        Physics.SyncTransforms();

        Bounds bounds;

        if (!TryGetCombinedColliderBounds(
                rock,
                out bounds))
        {
            if (!TryGetCombinedRendererBounds(
                    rock,
                    out bounds))
            {
                return;
            }
        }

        float highestTerrainY =
            float.NegativeInfinity;

        if (robustTerrainSnap)
        {
            for (int iz = 0; iz < 3; iz++)
            {
                float z =
                    Mathf.Lerp(
                        bounds.min.z,
                        bounds.max.z,
                        iz / 2f
                    );

                for (int ix = 0;
                     ix < 3;
                     ix++)
                {
                    float x =
                        Mathf.Lerp(
                            bounds.min.x,
                            bounds.max.x,
                            ix / 2f
                        );

                    float y =
                        terrain.SampleHeight(
                            new Vector3(
                                x,
                                0f,
                                z
                            )
                        ) +
                        terrain.transform.position.y;

                    highestTerrainY =
                        Mathf.Max(
                            highestTerrainY,
                            y
                        );
                }
            }
        }
        else
        {
            highestTerrainY =
                terrain.SampleHeight(
                    rock.transform.position
                ) +
                terrain.transform.position.y;
        }

        if (!float.IsFinite(highestTerrainY))
            return;

        float targetBottom =
            highestTerrainY -
            terrainEmbedM;

        float correction =
            targetBottom -
            bounds.min.y;

        rock.transform.position +=
            Vector3.up *
            correction;

        Physics.SyncTransforms();
    }

    private bool TryGetCombinedColliderBounds(
        GameObject obj,
        out Bounds combined
    )
    {
        Collider[] colliders =
            obj.GetComponentsInChildren<Collider>(
                true
            );

        bool have =
            false;

        combined =
            new Bounds();

        foreach (Collider c in colliders)
        {
            if (c == null ||
                !c.enabled)
            {
                continue;
            }

            if (!have)
            {
                combined =
                    c.bounds;

                have =
                    true;
            }
            else
            {
                combined.Encapsulate(
                    c.bounds
                );
            }
        }

        return have;
    }

    private bool TryGetCombinedRendererBounds(
        GameObject obj,
        out Bounds combined
    )
    {
        Renderer[] renderers =
            obj.GetComponentsInChildren<Renderer>(
                true
            );

        bool have =
            false;

        combined =
            new Bounds();

        foreach (Renderer r in renderers)
        {
            if (r == null ||
                !r.enabled)
            {
                continue;
            }

            if (!have)
            {
                combined =
                    r.bounds;

                have =
                    true;
            }
            else
            {
                combined.Encapsulate(
                    r.bounds
                );
            }
        }

        return have;
    }

    private void SetLayerRecursively(
        GameObject obj,
        int layer
    )
    {
        obj.layer =
            layer;

        foreach (Transform child
                 in obj.transform)
        {
            SetLayerRecursively(
                child.gameObject,
                layer
            );
        }
    }

    private static float Next01(
        System.Random rng
    )
    {
        return (float)rng.NextDouble();
    }

    private static float Range(
        System.Random rng,
        float min,
        float max
    )
    {
        return Mathf.Lerp(
            min,
            max,
            Next01(rng)
        );
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawPlannedPositions ||
            events == null)
        {
            return;
        }

        Gizmos.color =
            plannedGizmoColor;

        for (int i = 0; i < events.Count; i++)
        {
            RockEvent e =
                events[i];

            if (e == null ||
                e.spawned ||
                e.skipped)
            {
                continue;
            }

            Gizmos.DrawWireSphere(
                e.plannedWorld +
                Vector3.up * 0.35f,
                Mathf.Max(
                    0.25f,
                    e.radius
                )
            );
        }
    }
}
