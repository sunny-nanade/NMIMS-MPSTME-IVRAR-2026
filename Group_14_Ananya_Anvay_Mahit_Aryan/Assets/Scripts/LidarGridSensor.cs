using System.Collections.Generic;
using UnityEngine;

public class LidarGridSensor : MonoBehaviour
{
    private struct RememberedHit
    {
        public Vector3 worldPoint;
        public float expiresAt;

        public RememberedHit(Vector3 point, float expiry)
        {
            worldPoint = point;
            expiresAt = expiry;
        }
    }

    [Header("LiDAR")]
    [Range(16, 360)]
    public int rayCount = 144;

    public float maxRange = 16f;

    [Tooltip("Final V0.1 vertical beam angles in degrees.")]
    public float[] verticalAngles = new float[]
    {
        0f,
        -10f,
        -20f,
        -30f
    };

    [Header("Layers")]
    [Tooltip("Rocks and other hazards that should enter the SAC obstacle map.")]
    public LayerMask obstacleMask;

    [Tooltip("Terrain/ground that should BLOCK LiDAR line-of-sight but must NOT enter the obstacle map.")]
    public LayerMask groundMask;

    public bool drawRays = true;

    [Header("World-memory")]
    [Tooltip("How long a detected obstacle remains in memory.")]
    [Range(0f, 3f)]
    public float memorySeconds = 0.75f;

    public int maxRememberedPoints = 4096;

    [Header("Debug")]
    public bool debugScanEveryFrame = false;

    [Header("32 x 32 SAC occupancy grid")]
    public int gridSize = 32;

    [Tooltip("Must match the trained RoverEnv local_span_m.")]
    public float gridWidthMeters = 24f;

    [Tooltip("Assign the Rover ROOT. Do not assign the LiDAR transform here.")]
    public Transform gridReference;

    public bool dilateOneCell = true;

    [Header("V1.5f obstacle-transfer assist")]
    [Tooltip("Rasterize the full XZ footprint of every rock collider hit by LiDAR, not only one surface point.")]
    public bool rasterizeDetectedColliderFootprint = true;

    [Range(0f, 1.5f)]
    public float colliderFootprintInflationMeters = 0.10f;

    [Tooltip("Deployment safety assist: make nearby rock colliders visible even if a sparse LiDAR ring passes over/under them.")]
    public bool useNearFieldColliderAssist = true;

    [Range(2f, 10f)]
    public float nearFieldAssistRadius = 4.5f;

    [Header("Runtime Information")]
    [HideInInspector] public float[] occupancy;
    [HideInInspector] public int hitCount;
    [HideInInspector] public int groundOcclusionCount;
    [HideInInspector] public float minHitDistance;
    [HideInInspector] public int rememberedPointCount;

    private readonly List<RememberedHit> memory = new List<RememberedHit>();

    private void Awake()
    {
        occupancy = new float[gridSize * gridSize];
    }

    private void OnEnable()
    {
        memory.Clear();
        rememberedPointCount = 0;
    }

    private void Update()
    {
        if (debugScanEveryFrame)
            Scan();
    }

    public float[] Scan()
    {
        if (occupancy == null || occupancy.Length != gridSize * gridSize)
            occupancy = new float[gridSize * gridSize];

        System.Array.Clear(occupancy, 0, occupancy.Length);

        hitCount = 0;
        groundOcclusionCount = 0;
        minHitDistance = maxRange;

        float now = Time.time;

        PruneExpiredMemory(now);

        Vector3 origin = transform.position;

        int lineOfSightMask =
            obstacleMask.value | groundMask.value;

        HashSet<int> seenObstacleColliders =
            new HashSet<int>();

        for (int ring = 0; ring < verticalAngles.Length; ring++)
        {
            float pitch = verticalAngles[ring];

            for (int i = 0; i < rayCount; i++)
            {
                float yaw =
                    i * (360f / rayCount);

                Vector3 localDirection =
                    Quaternion.Euler(
                        pitch,
                        yaw,
                        0f
                    ) * Vector3.forward;

                Vector3 worldDirection =
                    transform.TransformDirection(
                        localDirection
                    ).normalized;

                bool detected =
                    Physics.Raycast(
                        origin,
                        worldDirection,
                        out RaycastHit hit,
                        maxRange,
                        lineOfSightMask,
                        QueryTriggerInteraction.Ignore
                    );

                if (!detected)
                {
                    if (drawRays)
                    {
                        Debug.DrawRay(
                            origin,
                            worldDirection * maxRange,
                            Color.green,
                            0.15f
                        );
                    }

                    continue;
                }

                int hitLayer =
                    hit.collider.gameObject.layer;

                if (LayerIsInMask(
                    hitLayer,
                    obstacleMask))
                {
                    hitCount++;

                    minHitDistance =
                        Mathf.Min(
                            minHitDistance,
                            hit.distance
                        );

                    RememberHit(
                        hit.point,
                        now
                    );

                    if (rasterizeDetectedColliderFootprint &&
                        hit.collider != null &&
                        seenObstacleColliders.Add(hit.collider.GetInstanceID()))
                    {
                        MarkColliderFootprint(hit.collider);
                    }

                    if (drawRays)
                    {
                        Debug.DrawLine(
                            origin,
                            hit.point,
                            Color.red,
                            0.15f
                        );
                    }
                }
                else if (LayerIsInMask(
                    hitLayer,
                    groundMask))
                {
                    // Ground blocks the beam, but is NOT added to the
                    // SAC obstacle channel.
                    groundOcclusionCount++;

                    if (drawRays)
                    {
                        Debug.DrawLine(
                            origin,
                            hit.point,
                            Color.yellow,
                            0.15f
                        );
                    }
                }
            }
        }

        // Near-field deployment assist.  This is deliberately separate from
        // the long-range ray pattern: it prevents low/irregular rocks from becoming
        // invisible simply because all four LiDAR rings pass over/under them.
        if (useNearFieldColliderAssist)
        {
            Transform reference =
                gridReference != null
                ? gridReference
                : transform;

            Collider[] nearby =
                Physics.OverlapSphere(
                    reference.position,
                    nearFieldAssistRadius,
                    obstacleMask,
                    QueryTriggerInteraction.Ignore
                );

            foreach (Collider c in nearby)
            {
                if (c != null)
                    MarkColliderFootprint(c);
            }
        }

        // Re-project remembered WORLD obstacle points into the
        // rover's CURRENT local frame.
        for (int i = 0; i < memory.Count; i++)
        {
            MarkLocalPoint(
                memory[i].worldPoint
            );
        }

        rememberedPointCount =
            memory.Count;

        return occupancy;
    }

    private static bool LayerIsInMask(
        int layer,
        LayerMask mask)
    {
        return
            (mask.value &
            (1 << layer)) != 0;
    }

    private void RememberHit(
        Vector3 worldPoint,
        float now)
    {
        if (memorySeconds <= 0f)
        {
            MarkLocalPoint(
                worldPoint
            );

            return;
        }

        if (memory.Count >=
            Mathf.Max(
                1,
                maxRememberedPoints))
        {
            int removeCount =
                Mathf.Max(
                    1,
                    memory.Count -
                    maxRememberedPoints +
                    1
                );

            memory.RemoveRange(
                0,
                removeCount
            );
        }

        memory.Add(
            new RememberedHit(
                worldPoint,
                now + memorySeconds
            )
        );
    }

    private void PruneExpiredMemory(
        float now)
    {
        int write = 0;

        for (int read = 0;
             read < memory.Count;
             read++)
        {
            RememberedHit h =
                memory[read];

            if (h.expiresAt > now)
            {
                if (write != read)
                    memory[write] = h;

                write++;
            }
        }

        if (write < memory.Count)
        {
            memory.RemoveRange(
                write,
                memory.Count - write
            );
        }
    }

    private void MarkLocalPoint(
        Vector3 worldPoint)
    {
        Transform reference =
            gridReference != null
            ? gridReference
            : transform;

        Vector3 local =
            reference.InverseTransformPoint(
                worldPoint
            );

        float half =
            gridWidthMeters * 0.5f;

        // Same image convention as the trained rover observation:
        // row 0   = front
        // row 31  = back
        // col 0   = left
        // col 31  = right
        float col01 =
            (local.x + half) /
            gridWidthMeters;

        float row01 =
            (half - local.z) /
            gridWidthMeters;

        int col =
            Mathf.FloorToInt(
                col01 * gridSize
            );

        int row =
            Mathf.FloorToInt(
                row01 * gridSize
            );

        if (col < 0 ||
            col >= gridSize ||
            row < 0 ||
            row >= gridSize)
        {
            return;
        }

        SetCell(
            col,
            row
        );

        if (dilateOneCell)
        {
            for (int dr = -1;
                 dr <= 1;
                 dr++)
            {
                for (int dc = -1;
                     dc <= 1;
                     dc++)
                {
                    SetCell(
                        col + dc,
                        row + dr
                    );
                }
            }
        }
    }

    private void MarkColliderFootprint(Collider c)
    {
        if (c == null) return;

        Transform reference =
            gridReference != null
            ? gridReference
            : transform;

        Bounds b = c.bounds;

        Vector3 localCenter =
            reference.InverseTransformPoint(b.center);

        // World AABB radius is conservative for arbitrarily rotated boulders.
        float radius =
            Mathf.Max(
                0.20f,
                Mathf.Max(b.extents.x, b.extents.z) +
                colliderFootprintInflationMeters
            );

        float half =
            gridWidthMeters * 0.5f;

        float cell =
            gridWidthMeters /
            Mathf.Max(1, gridSize);

        int centerCol =
            Mathf.FloorToInt(
                ((localCenter.x + half) / gridWidthMeters) *
                gridSize
            );

        int centerRow =
            Mathf.FloorToInt(
                ((half - localCenter.z) / gridWidthMeters) *
                gridSize
            );

        int rCells =
            Mathf.CeilToInt(radius / Mathf.Max(1e-6f, cell));

        float rSq = radius * radius;

        for (int dr = -rCells; dr <= rCells; dr++)
        {
            for (int dc = -rCells; dc <= rCells; dc++)
            {
                float dx = dc * cell;
                float dz = dr * cell;

                if (dx * dx + dz * dz <= rSq)
                {
                    SetCell(
                        centerCol + dc,
                        centerRow + dr
                    );
                }
            }
        }
    }

    private void SetCell(
        int col,
        int row)
    {
        if (col < 0 ||
            col >= gridSize ||
            row < 0 ||
            row >= gridSize)
        {
            return;
        }

        occupancy[
            row * gridSize + col
        ] = 1f;
    }

    [ContextMenu("Clear LiDAR Memory")]
    public void ClearMemory()
    {
        memory.Clear();

        rememberedPointCount = 0;

        if (occupancy != null)
        {
            System.Array.Clear(
                occupancy,
                0,
                occupancy.Length
            );
        }
    }
}
