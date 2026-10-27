using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// V1.5f PyBullet-inspired procedural rock field for Unity.
///
/// Key properties:
/// 1) Rocks are distributed over the WHOLE terrain using stratified sectors.
/// 2) Each sector receives a quota, preventing large empty regions.
/// 3) Within each sector positions are still random, so the result does not look
///    like a grid.
/// 4) Rock radii match the V1.5f physical PyBullet range (~0.38..1.35 m).
/// 5) Start/goal, boundaries and steep terrain are protected.
/// 6) Every rock is placed on the Obstacle layer recursively.
/// 7) Rocks are snapped robustly to the terrain using collider/render bounds and
///    multiple terrain-height samples under the footprint.
///
/// IMPORTANT WORKFLOW:
/// Generate rocks FIRST -> export V15fNavSceneExporter -> regenerate A* path.
/// </summary>
public class AutoRockSpawner : MonoBehaviour
{
    [Header("References")]
    public Terrain terrain;
    public GameObject rockPrefab;
    public Transform roverStart;
    public Transform goal;

    [Header("Layer")]
    public string obstacleLayerName = "Obstacle";

    [Header("V1.5f Rock Population")]
    [Tooltip("36 is close to the V1.5f physical-rock regime while remaining below the 40-component cap used in PyBullet.")]
    [Range(16, 40)]
    public int rockCount = 36;

    [Tooltip("Keep fixed for repeatable model/controller comparisons.")]
    public int randomSeed = 71;

    [Tooltip("Approximate physical PyBullet radius range.")]
    public float minRockRadiusM = 0.38f;

    [Tooltip("Approximate physical PyBullet radius range.")]
    public float maxRockRadiusM = 1.35f;

    [Tooltip(">1 biases the population toward smaller rocks while retaining occasional larger boulders.")]
    [Range(1f, 4f)]
    public float smallRockBias = 1.65f;

    [Header("Whole-Terrain Coverage")]
    [Tooltip("4x4 macro-sectors gives natural coverage without visible grid regularity. Each sector gets an almost equal rock quota.")]
    [Range(2, 8)]
    public int coverageRows = 4;

    [Range(2, 8)]
    public int coverageColumns = 4;

    [Tooltip("Fraction of each sector kept available for random placement. 0.92 leaves a small internal border and hides the sector structure.")]
    [Range(0.55f, 0.98f)]
    public float sectorUsableFraction = 0.92f;

    [Tooltip("Candidate attempts for each required rock inside its assigned sector.")]
    public int attemptsPerRockInSector = 180;

    [Header("Spacing / Navigation")]
    [Tooltip("Minimum centre-to-centre distance between rock objects.")]
    public float minCenterSpacingM = 3.8f;

    [Tooltip("Additional required visible surface gap between two rocks.")]
    public float minSurfaceGapM = 0.65f;

    [Tooltip("Keep rocks away from terrain boundaries.")]
    public float edgeMarginM = 4.0f;

    [Tooltip("Keep the initial rover area clear.")]
    public float startClearanceM = 6.0f;

    [Tooltip("Keep the final-goal area clear.")]
    public float goalClearanceM = 6.0f;

    [Tooltip("Reject candidate points above this terrain slope for stable placement.")]
    [Range(5f, 40f)]
    public float maxSpawnSlopeDeg = 22.0f;

    [Header("Appearance")]
    public bool randomYaw = true;
    public bool randomTilt = true;

    [Range(0f, 15f)]
    public float maxTiltDeg = 7.0f;

    [Tooltip("Small size jitter around the requested physical radius.")]
    [Range(0f, 0.20f)]
    public float visualScaleJitter = 0.06f;

    [Header("Terrain Snap")]
    [Tooltip("Samples a 3x3 footprint under each rock and lifts the rock enough that it does not sink into uneven terrain.")]
    public bool robustTerrainSnap = true;

    [Tooltip("Small intentional embedding after snap, to avoid visually floating rocks.")]
    [Range(0f, 0.08f)]
    public float terrainEmbedM = 0.025f;

    [Header("Runtime Information")]
    [SerializeField] private int generatedRockCount = 0;
    [SerializeField] private float generatedMeanRadiusM = 0f;
    [SerializeField] private float generatedMinimumPairSpacingM = 0f;
    [SerializeField] private int generatedMinimumSectorCount = 0;
    [SerializeField] private int generatedMaximumSectorCount = 0;

    private struct Candidate
    {
        public Vector3 world;
        public float radius;
        public int sector;

        public Candidate(Vector3 world, float radius, int sector)
        {
            this.world = world;
            this.radius = radius;
            this.sector = sector;
        }
    }

    [ContextMenu("Generate V1.5f Coverage Rock Field")]
    public void GenerateRocks()
    {
        if (!ValidateSetup())
            return;

        ClearGeneratedRocks();

        int obstacleLayer = LayerMask.NameToLayer(obstacleLayerName);
        if (obstacleLayer < 0)
        {
            Debug.LogError(
                "AutoRockSpawner: layer '" + obstacleLayerName +
                "' does not exist. Create the Unity layer first."
            );
            return;
        }

        TerrainData td = terrain.terrainData;
        Vector3 terrainPos = terrain.transform.position;
        Vector3 terrainSize = td.size;

        int rows = Mathf.Max(2, coverageRows);
        int cols = Mathf.Max(2, coverageColumns);
        int sectorCount = rows * cols;

        if (rockCount < sectorCount)
        {
            Debug.LogError(
                "AutoRockSpawner: rockCount (" + rockCount +
                ") must be >= coverageRows*coverageColumns (" +
                sectorCount + ") so every sector can receive coverage."
            );
            return;
        }

        if (terrainSize.x <= 2f * edgeMarginM ||
            terrainSize.z <= 2f * edgeMarginM)
        {
            Debug.LogError("AutoRockSpawner: edgeMarginM is too large.");
            return;
        }

        System.Random rng = new System.Random(randomSeed);

        // Give every sector an almost-equal quota.
        // 36 rocks over 16 sectors -> 2 each, with four sectors receiving 3.
        int[] quota = BuildBalancedSectorQuota(
            rockCount,
            sectorCount,
            rng
        );

        List<Candidate> accepted =
            new List<Candidate>(rockCount);

        int[] actualPerSector =
            new int[sectorCount];

        int failedSectorPlacements = 0;

        for (int sector = 0; sector < sectorCount; sector++)
        {
            for (int n = 0; n < quota[sector]; n++)
            {
                bool placed = false;

                for (int attempt = 0;
                     attempt < Mathf.Max(20, attemptsPerRockInSector);
                     attempt++)
                {
                    float radius = SampleRockRadius(rng);

                    if (!TrySampleSectorPoint(
                            sector,
                            rows,
                            cols,
                            radius,
                            terrainPos,
                            terrainSize,
                            td,
                            rng,
                            accepted,
                            out Vector3 world))
                    {
                        continue;
                    }

                    accepted.Add(
                        new Candidate(
                            world,
                            radius,
                            sector
                        )
                    );

                    actualPerSector[sector]++;
                    placed = true;
                    break;
                }

                if (!placed)
                    failedSectorPlacements++;
            }
        }

        // If steep terrain or start/goal exclusion prevented some sector quota,
        // fill the shortfall globally, always preferring the least-populated
        // sectors first. This preserves coverage rather than creating one empty side.
        int fallbackAttempts = 0;
        int fallbackMaxAttempts =
            Mathf.Max(2000, rockCount * 300);

        while (accepted.Count < rockCount &&
               fallbackAttempts < fallbackMaxAttempts)
        {
            fallbackAttempts++;

            int sector =
                ChooseLeastPopulatedSector(
                    actualPerSector,
                    rng
                );

            float radius = SampleRockRadius(rng);

            if (!TrySampleSectorPoint(
                    sector,
                    rows,
                    cols,
                    radius,
                    terrainPos,
                    terrainSize,
                    td,
                    rng,
                    accepted,
                    out Vector3 world))
            {
                continue;
            }

            accepted.Add(
                new Candidate(
                    world,
                    radius,
                    sector
                )
            );

            actualPerSector[sector]++;
        }

        GameObject parent =
            new GameObject("Generated_Rocks");

        parent.transform.SetParent(transform);
        parent.transform.localPosition = Vector3.zero;
        parent.transform.localRotation = Quaternion.identity;
        parent.transform.localScale = Vector3.one;

        float radiusSum = 0f;

        for (int i = 0; i < accepted.Count; i++)
        {
            Candidate c = accepted[i];

            GameObject rock = Instantiate(
                rockPrefab,
                c.world,
                Quaternion.identity,
                parent.transform
            );

            rock.name =
                "Rock_" +
                (i + 1).ToString("00");

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

            // Scale using actual collider footprint if possible.
            float currentRadius =
                GetHorizontalPhysicalRadius(rock);

            if (currentRadius > 1e-4f)
            {
                float jitter =
                    1f + Range(
                        rng,
                        -visualScaleJitter,
                        visualScaleJitter
                    );

                float scaleFactor =
                    (c.radius / currentRadius) *
                    jitter;

                rock.transform.localScale *=
                    scaleFactor;
            }
            else
            {
                Debug.LogWarning(
                    rock.name +
                    ": no usable collider/renderer bounds; " +
                    "prefab scale left unchanged."
                );
            }

            Physics.SyncTransforms();

            SnapRockToTerrain(rock);

            radiusSum += c.radius;
        }

        generatedRockCount =
            accepted.Count;

        generatedMeanRadiusM =
            accepted.Count > 0
            ? radiusSum / accepted.Count
            : 0f;

        generatedMinimumPairSpacingM =
            ComputeMinimumPairSpacing(
                accepted
            );

        generatedMinimumSectorCount =
            int.MaxValue;

        generatedMaximumSectorCount =
            0;

        for (int i = 0; i < actualPerSector.Length; i++)
        {
            generatedMinimumSectorCount =
                Mathf.Min(
                    generatedMinimumSectorCount,
                    actualPerSector[i]
                );

            generatedMaximumSectorCount =
                Mathf.Max(
                    generatedMaximumSectorCount,
                    actualPerSector[i]
                );
        }

        if (actualPerSector.Length == 0)
            generatedMinimumSectorCount = 0;

        Debug.Log(
            "V1.5f COVERAGE ROCK FIELD GENERATED\n" +
            "Requested rocks: " + rockCount + "\n" +
            "Generated rocks: " + generatedRockCount + "\n" +
            "Seed: " + randomSeed + "\n" +
            "Coverage grid: " + rows + " x " + cols + "\n" +
            "Rocks/sector min..max: " +
                generatedMinimumSectorCount + ".." +
                generatedMaximumSectorCount + "\n" +
            "Radius target range: " +
                minRockRadiusM.ToString("F2") + ".." +
                maxRockRadiusM.ToString("F2") + " m\n" +
            "Mean target radius: " +
                generatedMeanRadiusM.ToString("F2") + " m\n" +
            "Minimum centre spacing: " +
                generatedMinimumPairSpacingM.ToString("F2") + " m\n" +
            "Sector placements missed before fallback: " +
                failedSectorPlacements + "\n" +
            "Fallback attempts: " +
                fallbackAttempts + "\n" +
            "NEXT: V15fNavSceneExporter -> export JSON -> regenerate A*."
        );

        if (generatedRockCount < rockCount)
        {
            Debug.LogWarning(
                "Generated only " +
                generatedRockCount + "/" +
                rockCount +
                " rocks. First try increasing attemptsPerRockInSector. " +
                "Only then consider reducing minCenterSpacingM slightly."
            );
        }

        if (generatedMinimumSectorCount == 0)
        {
            Debug.LogWarning(
                "At least one terrain sector is empty. " +
                "Do NOT generate the final A* yet; increase attempts or inspect that sector."
            );
        }
    }

    [ContextMenu("Clear Generated Rocks")]
    public void ClearGeneratedRocks()
    {
        Transform oldParent =
            transform.Find("Generated_Rocks");

        if (oldParent == null)
            return;

        if (Application.isPlaying)
            Destroy(oldParent.gameObject);
        else
            DestroyImmediate(oldParent.gameObject);
    }

    [ContextMenu("Randomize Seed + Generate")]
    public void RandomizeSeedAndGenerate()
    {
        randomSeed =
            unchecked(
                (int)DateTime.UtcNow.Ticks
            );

        GenerateRocks();
    }

    private int[] BuildBalancedSectorQuota(
        int total,
        int sectors,
        System.Random rng
    )
    {
        int[] quota =
            new int[sectors];

        int baseCount =
            total / sectors;

        int remainder =
            total % sectors;

        for (int i = 0; i < sectors; i++)
            quota[i] = baseCount;

        List<int> order =
            new List<int>(sectors);

        for (int i = 0; i < sectors; i++)
            order.Add(i);

        Shuffle(order, rng);

        for (int i = 0; i < remainder; i++)
            quota[order[i]]++;

        return quota;
    }

    private int ChooseLeastPopulatedSector(
        int[] actual,
        System.Random rng
    )
    {
        int best =
            int.MaxValue;

        for (int i = 0; i < actual.Length; i++)
            best = Mathf.Min(best, actual[i]);

        List<int> choices =
            new List<int>();

        for (int i = 0; i < actual.Length; i++)
        {
            if (actual[i] == best)
                choices.Add(i);
        }

        if (choices.Count == 0)
            return rng.Next(actual.Length);

        return choices[
            rng.Next(choices.Count)
        ];
    }

    private bool TrySampleSectorPoint(
        int sector,
        int rows,
        int cols,
        float radius,
        Vector3 terrainPos,
        Vector3 terrainSize,
        TerrainData td,
        System.Random rng,
        List<Candidate> accepted,
        out Vector3 world
    )
    {
        world = Vector3.zero;

        int row =
            sector / cols;

        int col =
            sector % cols;

        float fullMinX =
            terrainPos.x +
            edgeMarginM;

        float fullMaxX =
            terrainPos.x +
            terrainSize.x -
            edgeMarginM;

        float fullMinZ =
            terrainPos.z +
            edgeMarginM;

        float fullMaxZ =
            terrainPos.z +
            terrainSize.z -
            edgeMarginM;

        float usableWidth =
            fullMaxX - fullMinX;

        float usableDepth =
            fullMaxZ - fullMinZ;

        float cellW =
            usableWidth / cols;

        float cellD =
            usableDepth / rows;

        float sectorMinX =
            fullMinX +
            col * cellW;

        float sectorMaxX =
            sectorMinX +
            cellW;

        float sectorMinZ =
            fullMinZ +
            row * cellD;

        float sectorMaxZ =
            sectorMinZ +
            cellD;

        // Pull candidate bounds slightly inward within every macro-sector,
        // then sample freely inside. This hides the sector/grid structure.
        float insetX =
            0.5f *
            cellW *
            (1f - sectorUsableFraction);

        float insetZ =
            0.5f *
            cellD *
            (1f - sectorUsableFraction);

        float x =
            Range(
                rng,
                sectorMinX + insetX,
                sectorMaxX - insetX
            );

        float z =
            Range(
                rng,
                sectorMinZ + insetZ,
                sectorMaxZ - insetZ
            );

        Vector3 candidate =
            new Vector3(
                x,
                0f,
                z
            );

        if (!StartGoalClear(candidate))
            return false;

        float nx =
            Mathf.Clamp01(
                (x - terrainPos.x) /
                Mathf.Max(1e-6f, terrainSize.x)
            );

        float nz =
            Mathf.Clamp01(
                (z - terrainPos.z) /
                Mathf.Max(1e-6f, terrainSize.z)
            );

        Vector3 normal =
            td.GetInterpolatedNormal(
                nx,
                nz
            );

        float slopeDeg =
            Vector3.Angle(
                normal,
                Vector3.up
            );

        if (slopeDeg > maxSpawnSlopeDeg)
            return false;

        for (int i = 0; i < accepted.Count; i++)
        {
            Candidate p =
                accepted[i];

            float dx =
                p.world.x - x;

            float dz =
                p.world.z - z;

            float centreDistance =
                Mathf.Sqrt(
                    dx * dx +
                    dz * dz
                );

            float required =
                Mathf.Max(
                    minCenterSpacingM,
                    p.radius +
                    radius +
                    minSurfaceGapM
                );

            if (centreDistance < required)
                return false;
        }

        float y =
            terrain.SampleHeight(candidate) +
            terrain.transform.position.y;

        candidate.y = y;
        world = candidate;

        return true;
    }

    private bool StartGoalClear(
        Vector3 candidate
    )
    {
        Vector2 p =
            new Vector2(
                candidate.x,
                candidate.z
            );

        if (roverStart != null)
        {
            Vector2 s =
                new Vector2(
                    roverStart.position.x,
                    roverStart.position.z
                );

            if (Vector2.Distance(p, s) <
                startClearanceM)
            {
                return false;
            }
        }

        if (goal != null)
        {
            Vector2 g =
                new Vector2(
                    goal.position.x,
                    goal.position.z
                );

            if (Vector2.Distance(p, g) <
                goalClearanceM)
            {
                return false;
            }
        }

        return true;
    }

    private float SampleRockRadius(
        System.Random rng
    )
    {
        float u =
            Mathf.Clamp01(
                Next01(rng)
            );

        float biased =
            Mathf.Pow(
                u,
                Mathf.Max(1f, smallRockBias)
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
                out Bounds colliderBounds))
        {
            return Mathf.Max(
                colliderBounds.extents.x,
                colliderBounds.extents.z
            );
        }

        if (TryGetCombinedRendererBounds(
                rock,
                out Bounds rendererBounds))
        {
            return Mathf.Max(
                rendererBounds.extents.x,
                rendererBounds.extents.z
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
            // 3x3 samples across the world-space footprint.
            for (int iz = 0; iz < 3; iz++)
            {
                float tz =
                    iz / 2f;

                float z =
                    Mathf.Lerp(
                        bounds.min.z,
                        bounds.max.z,
                        tz
                    );

                for (int ix = 0;
                     ix < 3;
                     ix++)
                {
                    float tx =
                        ix / 2f;

                    float x =
                        Mathf.Lerp(
                            bounds.min.x,
                            bounds.max.x,
                            tx
                        );

                    float y =
                        terrain.SampleHeight(
                            new Vector3(
                                x,
                                0f,
                                z
                            )
                        )
                        + terrain.transform.position.y;

                    if (y > highestTerrainY)
                        highestTerrainY = y;
                }
            }
        }
        else
        {
            highestTerrainY =
                terrain.SampleHeight(
                    new Vector3(
                        rock.transform.position.x,
                        0f,
                        rock.transform.position.z
                    )
                )
                + terrain.transform.position.y;
        }

        if (!float.IsFinite(highestTerrainY))
            return;

        float targetBottomY =
            highestTerrainY -
            terrainEmbedM;

        float correction =
            targetBottomY -
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
            obj.GetComponentsInChildren<Collider>(true);

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
            obj.GetComponentsInChildren<Renderer>(true);

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

    private float ComputeMinimumPairSpacing(
        List<Candidate> rocks
    )
    {
        if (rocks.Count < 2)
            return 0f;

        float best =
            float.PositiveInfinity;

        for (int i = 0; i < rocks.Count; i++)
        {
            for (int j = i + 1;
                 j < rocks.Count;
                 j++)
            {
                float dx =
                    rocks[i].world.x -
                    rocks[j].world.x;

                float dz =
                    rocks[i].world.z -
                    rocks[j].world.z;

                float d =
                    Mathf.Sqrt(
                        dx * dx +
                        dz * dz
                    );

                if (d < best)
                    best = d;
            }
        }

        return best;
    }

    private bool ValidateSetup()
    {
        if (terrain == null)
        {
            Debug.LogError(
                "AutoRockSpawner: assign Terrain_HiRISE."
            );
            return false;
        }

        if (rockPrefab == null)
        {
            Debug.LogError(
                "AutoRockSpawner: assign the rock prefab."
            );
            return false;
        }

        if (minRockRadiusM <= 0f ||
            maxRockRadiusM <
            minRockRadiusM)
        {
            Debug.LogError(
                "AutoRockSpawner: invalid radius range."
            );
            return false;
        }

        if (sectorUsableFraction <= 0f ||
            sectorUsableFraction > 1f)
        {
            Debug.LogError(
                "AutoRockSpawner: sectorUsableFraction must be in (0,1]."
            );
            return false;
        }

        return true;
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

    private static void Shuffle<T>(
        IList<T> list,
        System.Random rng
    )
    {
        for (int i = list.Count - 1;
             i > 0;
             i--)
        {
            int j =
                rng.Next(i + 1);

            T temp =
                list[i];

            list[i] =
                list[j];

            list[j] =
                temp;
        }
    }
}
