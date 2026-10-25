using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// IVRAR Group 03 - Collaborative Multi-User VR Emergency Evacuation Simulator
/// NMIMS MPSTME - Yashika, Moksh, Bhavi, Preet
/// 
/// EvacuationEgressManager:
/// Integrates Helbing Social Force Model for 300 pedestrian agents,
/// Beer-Lambert smoke optical density and walking speed attenuation:
/// v = v0 * exp(-alpha_ext * rho_smoke),
/// dynamic volumetric smoke spread, glowing red fire hazards across floors 4 and 2,
/// aggressive electrical light flickering, and NFPA 72/101 compliant emergency strobe beacons.
/// </summary>
public class EvacuationEgressManager : MonoBehaviour
{
    public static EvacuationEgressManager Instance { get; private set; }

    // =========================================================================
    // 1. INSPECTOR FIELDS: AGENT SPAWNING & REQUISITE REFERENCES
    // =========================================================================

    [Tooltip("Human character prefab (male/female student pedestrian)")]
    public GameObject studentPrefab;

    [Header("Student Appearance Variations (BroModel)")]
    [Tooltip("Array of 18 different student facial expressions (Face_01 to Face_18)")]
    public Material[] faceMaterials;

    [Tooltip("Spawn zone transforms located across the top floor (Floor 5)")]
    public Transform[] topFloorSpawnZones;

    [Tooltip("Target stairwell exit portals (East & West stairwells / emergency exits)")]
    public Transform[] stairwellExitPortals;

    [Tooltip("Reference to the VR Player / Camera Rig to enable physical crowd-player collision & avoidance")]
    public Transform playerTransform;

    // =========================================================================
    // 2. CONFIGURABLE PARAMETERS & SOCIAL FORCE CONSTANTS
    // =========================================================================

    [Header("Crowd Simulation Parameters")]
    [Tooltip("Total number of human student agents to instantiate (default = 300)")]
    public int agentCount = 300;

    [Tooltip("Nominal unimpeded pedestrian desired walking speed v_0 in m/s (default = 1.25 m/s)")]
    public float desiredSpeed = 1.25f;

    [Tooltip("Relaxation time tau in seconds required to adapt velocity to desired velocity (default = 0.5s)")]
    public float relaxationTimeTau = 0.5f;

    [Tooltip("Agent personal radius r_i in meters (default = 0.3m)")]
    public float agentRadius = 0.3f;

    [Tooltip("Agent average physical body mass in kg (default = 75 kg)")]
    public float agentMass = 75.0f;

    [Header("Helbing Social Force Constants")]
    [Tooltip("Social repulsive force amplitude A in Newtons (default = 2000.0 N)")]
    public float repulsiveAmplitudeA = 2000.0f;

    [Tooltip("Social repulsive force spatial decay scale B in meters (default = 0.08 m)")]
    public float repulsiveScaleB = 0.08f;

    [Tooltip("Physical body compression / elastic constant k in kg/s^2 (default = 120000.0)")]
    public float elasticConstantK = 120000.0f;

    [Tooltip("Sliding friction coefficient kappa in kg/(m*s) (default = 240000.0)")]
    public float slidingFrictionKappa = 240000.0f;

    [Header("Smoke Extinction & Speed Attenuation (Beer-Lambert)")]
    [Tooltip("Smoke extinction coefficient alpha_ext for Beer-Lambert speed reduction (strictly 0.45)")]
    public float alpha_ext = 0.45f;

    [Tooltip("Threshold distance to exit portal in meters for successful evacuation (default = 1.0m)")]
    public float evacuationArrivalThreshold = 1.0f;

    [Header("Wall & Corridor Obstacle Repulsion")]
    [Tooltip("LayerMask containing physical walls, corridor obstacles, and structural boundaries")]
    public LayerMask wallObstacleMask = ~0;

    [Tooltip("Effective sensory distance for detecting walls in meters")]
    public float wallRepulsionDistance = 1.0f;

    [Tooltip("Wall repulsive force amplitude A_wall in Newtons")]
    public float wallRepulsiveAmplitude = 1800.0f;

    [Tooltip("Wall repulsive decay scale B_wall in meters")]
    public float wallRepulsiveScale = 0.10f;

    // =========================================================================
    // 3. VOLUMETRIC SMOKE SIMULATION & HAZARD LOCATIONS (FLOORS 4 & 2)
    // =========================================================================

    [Header("Smoke Simulation & Physics")]
    [Range(0.0f, 1.0f)]
    [Tooltip("Expanding volumetric smoke density rho in [0.0, 1.0]")]
    [SerializeField] private float smokeDensity = 0.35f;

    [Tooltip("Beer-Lambert extinction coefficient beta in m^-1 (strictly 0.45 per C107 spec)")]
    public float beerLambertExtinctionCoeff = 0.45f;

    [Tooltip("Yashika's crowd smoke sensitivity / deceleration constant alpha")]
    public float alphaCrowdSensitivity = 1.2f;

    [Tooltip("Nominal unimpeded pedestrian egress walking speed v0 in m/s (NBC 2016 / NFPA 101)")]
    public float nominalCrowdSpeed = 1.35f;

    [Tooltip("Hazard ignition location (Floor 4 / 3 near East Stairwell: x=7.0, y=9.0, z=0.0)")]
    public Vector3 hazardLocation = new Vector3(7.0f, 9.0f, 0.0f);

    [Tooltip("Floor 2 Hazard ignition location (Floor 2 near West Stairwell: x=-6.5, y=6.0, z=0.0)")]
    public Vector3 hazardLocationFloor2 = new Vector3(-6.5f, 6.0f, 0.0f);

    [Tooltip("Maximum effective radial dispersion of smoke in meters")]
    public float hazardRadius = 18.0f;

    [Header("Dynamic Growth")]
    [Tooltip("Enable automated dynamic smoke propagation over time")]
    public bool autoPropagateSmoke = true;

    [Tooltip("Rate of smoke density expansion per second")]
    public float smokeGrowthRate = 0.025f;

    [Header("Floor 4 / 3 Particle Systems")]
    public ParticleSystem smokeParticleSystem;
    public ParticleSystem fireParticleSystem;
    public ParticleSystem fireCoreGlowParticleSystem;
    public ParticleSystem embersParticleSystem;

    [Header("Floor 4 / 3 Lighting & Physical Meshes")]
    public Light fireFlickerLight;
    public Light floor3RedAmbientLight;
    public Light eastStairStrobeRed;
    public Light westStairStrobeGreen;
    public Light[] floor3CorridorLights;
    public Renderer[] glowingRedDebrisRenderers;
    public Renderer glowingEmberFloorRenderer;

    [Header("Floor 2 Fire Particle Systems")]
    public ParticleSystem smokeParticleSystem2;
    public ParticleSystem fireParticleSystem2;
    public ParticleSystem fireCoreGlowParticleSystem2;
    public ParticleSystem embersParticleSystem2;

    [Header("Floor 2 Lighting & Physical Meshes")]
    public Light fireFlickerLight2;
    public Light floor2RedAmbientLight;
    public Light[] floor2CorridorLights;
    public Renderer[] glowingRedDebrisRenderers2;
    public Renderer glowingEmberFloorRenderer2;

    [Header("Emergency Exit Strobes (NFPA 72 Compliant)")]
    public float strobeFrequency = 1.5f;
    public float strobeIntensityMax = 5.0f;

    [Header("Aggressive Light Flickering")]
    [Tooltip("Multiplier for electrical and fire light flicker speed")]
    public float flickerSpeed = 16.0f;
    [Tooltip("Probability of sudden electrical brownout / flicker dip per frame")]
    [Range(0f, 0.2f)]
    public float electricalDropoutChance = 0.06f;

    [Header("Crowd Telemetry")]
    [SerializeField] private int activeAgentCount = 0;
    [SerializeField] private int evacuatedAgentCount = 0;

    // =========================================================================
    // INTERNAL AGENT MANAGEMENT DATA STRUCTURE
    // =========================================================================

    public enum EgressStage
    {
        InRoom_MovingToCorridor = 0,
        InCorridor_MovingToStairPortal = 1,
        InDoorLanding_MovingToNorthLanding = 2,
        InFlight_DescendingToLowerLanding = 3,
        InLowerLanding_TraversingToNextFlight = 4,
        InGroundCorridor_MovingToMainExit = 5,
        PassingThroughMainDoor = 6,
        EvacuatedInPlaza = 7
    }

    // Known Hostel Floor Walking Surface Elevations (Y coordinates of top of floor slabs)
    // Story 0 (Ground): ~0.125m, Story 1 (Floor 1): ~3.025m, Story 2 (Floor 2): ~6.025m,
    // Story 3 (Floor 3): ~9.025m, Story 4 (Floor 4): ~12.025m, Story 5 (Roof): ~15.025m
    public static readonly float[] FloorElevations = new float[] { 0.125f, 3.025f, 6.025f, 9.025f, 12.025f, 15.025f };

    [System.Serializable]
    public class ManagedAgent
    {
        public int id;
        public GameObject gameObject;
        public Transform transform;
        public Vector3 position;
        public Vector3 velocity;
        public float radius;
        public float mass;
        public Transform targetExitPortal;
        public bool isEvacuated;
        public Animator animator;
        public int speedHash;
        public bool hasSpeedParam;

        // Multi-story vertical stair traversal & boundary tracking
        public int currentFloor; // Starts on top residential floor (Floor 4)
        public int stairwellSide; // 0 = East (+X), 1 = West (-X)
        public EgressStage stage;
        public Vector3 currentWaypoint;
        public float currentTargetY;
        public bool hasExitedBuilding;
    }

    private List<ManagedAgent> _agents = new List<ManagedAgent>();
    private Collider[] _wallColliderBuffer = new Collider[16];
    private static readonly Collider[] _stepOverlapBuffer = new Collider[16];

    // High-performance 2D Spatial Hash Grid (zero GC allocation)
    private const float CELL_SIZE = 2.5f;
    private const int GRID_W = 64;
    private const int GRID_H = 64;
    private const int GRID_OFFSET_X = 32;
    private const int GRID_OFFSET_Z = 32;
    private int[] _spatialGridHead = new int[GRID_W * GRID_H];
    private int[] _spatialGridNext = new int[500];
    private int _simulationFrame = 0;

    private float _nextFlickerTime = 0f;
    private const float FLICKER_INTERVAL = 0.04f; // 25 Hz update for flickering lights

    private float _strobeTimer = 0f;
    private Color _initialAmbientColor;
    private float _initialFogDensity;
    private float[] _corridorLightBaseIntensities;
    private float[] _corridorLight2BaseIntensities;
    private MaterialPropertyBlock _propBlock;

    // Public properties
    public float SmokeDensity
    {
        get => smokeDensity;
        set => smokeDensity = Mathf.Clamp01(value);
    }

    public float BeerLambertExtinctionCoeff => beerLambertExtinctionCoeff;
    public int ActiveAgentCount => activeAgentCount;
    public int EvacuatedAgentCount => evacuatedAgentCount;

    // =========================================================================
    // UNITY LIFECYCLE: AWAKE, START, UPDATE, FIXEDUPDATE
    // =========================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _initialAmbientColor = RenderSettings.ambientLight;
        _initialFogDensity = RenderSettings.fogDensity;
        _propBlock = new MaterialPropertyBlock();

        // Auto-find VR Player Camera Rig if not manually assigned
        if (playerTransform == null)
        {
            var rig = GameObject.Find("[BuildingBlock] Camera Rig");
            if (rig != null) playerTransform = rig.transform;
            else if (Camera.main != null) playerTransform = Camera.main.transform;
        }

        var allLights = FindObjectsByType<Light>();

        // Auto-find Floor 3/4 corridor ceiling lights if not explicitly wired in Inspector
        if (floor3CorridorLights == null || floor3CorridorLights.Length == 0)
        {
            var f3List = new List<Light>();
            foreach (var l in allLights)
            {
                if (l != fireFlickerLight && l != floor3RedAmbientLight && l != fireFlickerLight2 && l != floor2RedAmbientLight && l != eastStairStrobeRed && l != westStairStrobeGreen)
                {
                    if (l.transform.position.y >= 8.5f && l.transform.position.y <= 13.5f)
                    {
                        f3List.Add(l);
                    }
                }
            }
            floor3CorridorLights = f3List.ToArray();
        }

        // Auto-find Floor 2 corridor ceiling lights if not explicitly wired in Inspector
        if (floor2CorridorLights == null || floor2CorridorLights.Length == 0)
        {
            var f2List = new List<Light>();
            foreach (var l in allLights)
            {
                if (l != fireFlickerLight && l != floor3RedAmbientLight && l != fireFlickerLight2 && l != floor2RedAmbientLight && l != eastStairStrobeRed && l != westStairStrobeGreen)
                {
                    if (l.transform.position.y >= 5.5f && l.transform.position.y < 8.5f)
                    {
                        f2List.Add(l);
                    }
                }
            }
            floor2CorridorLights = f2List.ToArray();
        }

        // Cache base intensities for Floor 3/4 corridor lights
        if (floor3CorridorLights != null && floor3CorridorLights.Length > 0)
        {
            _corridorLightBaseIntensities = new float[floor3CorridorLights.Length];
            for (int i = 0; i < floor3CorridorLights.Length; i++)
            {
                if (floor3CorridorLights[i] != null)
                {
                    _corridorLightBaseIntensities[i] = floor3CorridorLights[i].intensity;
                }
            }
        }

        // Cache base intensities for Floor 2 corridor lights
        if (floor2CorridorLights != null && floor2CorridorLights.Length > 0)
        {
            _corridorLight2BaseIntensities = new float[floor2CorridorLights.Length];
            for (int i = 0; i < floor2CorridorLights.Length; i++)
            {
                if (floor2CorridorLights[i] != null)
                {
                    _corridorLight2BaseIntensities[i] = floor2CorridorLights[i].intensity;
                }
            }
        }
    }

    private void Start()
    {
        // 1. Synchronize extinction coefficients
        if (Mathf.Abs(alpha_ext - beerLambertExtinctionCoeff) > 0.001f)
        {
            alpha_ext = beerLambertExtinctionCoeff;
        }

        // 2. Validate and initialize Top Floor Spawn Zones & Exit Portals
        ValidateEnvironmentPortals();

        // 3. Instantiate exactly 300 student agents distributed across topFloorSpawnZones
        SpawnCrowdAgents();

        // 4. Initialize 3D Fire Proximity Audio on Floor 3 and Floor 2 Hazards
        InitializeFireProximityAudio();
    }

    private void Update()
    {
        // 1. Dynamic smoke density propagation
        if (autoPropagateSmoke && smokeDensity < 1.0f)
        {
            smokeDensity = Mathf.Clamp01(smokeDensity + smokeGrowthRate * Time.deltaTime);
        }

        // 2. Synchronize particle systems to smoke density
        UpdateParticleSystems();

        // 3. Apply Beer-Lambert visual fog and ambient lighting attenuation
        UpdateOpticalAttenuation();

        // 4. Update aggressive fire flicker, glowing red fire emission, and electrical ceiling lights (throttled to 25 Hz for optimal GPU performance)
        if (Time.time >= _nextFlickerTime)
        {
            _nextFlickerTime = Time.time + FLICKER_INTERVAL;
            UpdateAggressiveFlicker();
        }

        // 5. Update emergency exit strobes
        UpdateStrobes();
    }

    private void FixedUpdate()
    {
        // 6. Execute full Helbing Social Force physics simulation step
        SimulateHelbingSocialForces(Time.fixedDeltaTime);
    }

    // =========================================================================
    // 2. AGENT INITIALIZATION & SPAWNING LOGIC
    // =========================================================================

    private void ValidateEnvironmentPortals()
    {
        // 1. Auto-detect student prefab (prioritize BroModel, avoid Chuby)
        if (studentPrefab == null || studentPrefab.name.ToLower().Contains("chuby"))
        {
#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("BroModel t:Prefab");
            if (guids != null && guids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                studentPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Debug.Log($"[EvacuationEgressManager] Auto-assigned BroModel character prefab: {path}");
            }
#endif
        }

        // Auto-load 18 face materials from Low Poly Bros asset if not populated
#if UNITY_EDITOR
        if (faceMaterials == null || faceMaterials.Length == 0)
        {
            var fList = new List<Material>();
            for (int f = 1; f <= 18; f++)
            {
                string fPath = $"Assets/Low Poly Bros/Materials/Face_{f:D2}.mat";
                Material m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(fPath);
                if (m != null) fList.Add(m);
            }
            faceMaterials = fList.ToArray();
            Debug.Log($"[EvacuationEgressManager] Loaded {faceMaterials.Length} student face materials.");
        }
#endif

        // 2. Ensure top floor spawn zones exist (Top floor is Floor_4 in NBC 2016 5-story hostel)
        if (topFloorSpawnZones == null || topFloorSpawnZones.Length == 0)
        {
            var detectedZones = new List<Transform>();

            // Automatically search for Floor_4 (the top residential story at y=12m)
            GameObject floor4 = GameObject.Find("Floor_4") ?? GameObject.Find("HostelBuilding/Floor_4");
            if (floor4 != null)
            {
                detectedZones.Add(floor4.transform);
                foreach (Transform child in floor4.transform)
                {
                    if (child.name.Contains("Room") || child.name.Contains("Slab") || child.name.Contains("Corr"))
                    {
                        detectedZones.Add(child);
                    }
                }
            }

            var allObjects = FindObjectsByType<GameObject>();
            foreach (var go in allObjects)
            {
                if (go.name.ToLower().Contains("spawn") || go.name.ToLower().Contains("topfloor"))
                {
                    detectedZones.Add(go.transform);
                }
            }

            if (detectedZones.Count > 0)
            {
                topFloorSpawnZones = detectedZones.ToArray();
                Debug.Log($"[EvacuationEgressManager] Auto-detected {topFloorSpawnZones.Length} top-floor spawn anchors on Floor_4.");
            }
            else
            {
                // Fallback top floor spawn anchors (Floor 4 elevation y = 12.0m)
                GameObject fallbackZone1 = new GameObject("Fallback_TopFloorSpawn_East");
                fallbackZone1.transform.position = new Vector3(6.0f, 12.0f, 5.0f);
                GameObject fallbackZone2 = new GameObject("Fallback_TopFloorSpawn_West");
                fallbackZone2.transform.position = new Vector3(-6.0f, 12.0f, -5.0f);
                GameObject fallbackZone3 = new GameObject("Fallback_TopFloorSpawn_Central");
                fallbackZone3.transform.position = new Vector3(0.0f, 12.0f, 0.0f);

                topFloorSpawnZones = new Transform[] { fallbackZone1.transform, fallbackZone2.transform, fallbackZone3.transform };
            }
        }

        // 3. Ensure stairwell exit portals exist
        if (stairwellExitPortals == null || stairwellExitPortals.Length == 0)
        {
            var detectedPortals = new List<Transform>();
            if (eastStairStrobeRed != null) detectedPortals.Add(eastStairStrobeRed.transform);
            if (westStairStrobeGreen != null) detectedPortals.Add(westStairStrobeGreen.transform);

            if (detectedPortals.Count == 0)
            {
                GameObject eastPortal = new GameObject("Fallback_ExitPortal_East");
                eastPortal.transform.position = new Vector3(7.5f, 0.0f, 0.0f);
                GameObject westPortal = new GameObject("Fallback_ExitPortal_West");
                westPortal.transform.position = new Vector3(-7.5f, 0.0f, 0.0f);

                stairwellExitPortals = new Transform[] { eastPortal.transform, westPortal.transform };
            }
            else
            {
                stairwellExitPortals = detectedPortals.ToArray();
            }
        }
    }

    private void SpawnCrowdAgents()
    {
        _agents.Clear();
        activeAgentCount = 0;
        evacuatedAgentCount = 0;

        int numZones = (topFloorSpawnZones != null && topFloorSpawnZones.Length > 0) ? topFloorSpawnZones.Length : 0;
        int numPortals = stairwellExitPortals.Length;

        // Container transform for clean hierarchy - placed at root level so it is immediately visible in the Hierarchy window
        GameObject agentsParent = new GameObject("CrowdSimulation_Agents");
        agentsParent.transform.SetParent(null);

        // Pre-allocate spatial grid lookup array
        if (_spatialGridNext.Length < agentCount)
        {
            _spatialGridNext = new int[agentCount + 50];
        }

        // Check if custom zones have actual world positions (not all co-located at 0,0,0)
        bool useCustomZones = (numZones > 0);
        if (useCustomZones)
        {
            float maxPosSqr = 0f;
            foreach (var z in topFloorSpawnZones)
            {
                if (z != null) maxPosSqr = Mathf.Max(maxPosSqr, z.position.sqrMagnitude);
            }
            if (maxPosSqr < 1.0f) useCustomZones = false;
        }

        for (int i = 0; i < agentCount; i++)
        {
            Vector3 spawnPosition;

            if (useCustomZones)
            {
                Transform spawnZone = topFloorSpawnZones[i % numZones];
                Vector3 zoneCenter = spawnZone.position;
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float distance = UnityEngine.Random.Range(0.2f, 2.5f);
                Vector3 offset = new Vector3(Mathf.Cos(angle) * distance, 0.0f, Mathf.Sin(angle) * distance);
                spawnPosition = zoneCenter + offset;
            }
            else
            {
                // Intelligent Floor 4 distribution:
                // 1/3 along Central Corridor (Z between -0.6 and +0.6, X between -5.5 and +5.5)
                // 1/3 inside North Dorm Rooms (Z between 1.4 and 2.7, X between -5.0 and +5.0)
                // 1/3 inside South Dorm Rooms (Z between -2.7 and -1.4, X between -5.0 and +5.0)
                float floorElevation = 12.05f;
                int section = i % 3;
                float spawnX = UnityEngine.Random.Range(-5.5f, 5.5f);
                float spawnZ;

                if (section == 0)
                {
                    spawnZ = UnityEngine.Random.Range(-0.6f, 0.6f);
                }
                else if (section == 1)
                {
                    spawnZ = UnityEngine.Random.Range(1.4f, 2.7f);
                }
                else
                {
                    spawnZ = UnityEngine.Random.Range(-2.7f, -1.4f);
                }

                spawnPosition = new Vector3(spawnX, floorElevation, spawnZ);
            }

            // Target exit portal selection
            Transform targetPortal = stairwellExitPortals[i % numPortals];
            if (numPortals > 1)
            {
                float d1 = Vector3.Distance(new Vector3(spawnPosition.x, 0f, spawnPosition.z), new Vector3(stairwellExitPortals[0].position.x, 0f, stairwellExitPortals[0].position.z));
                float d2 = Vector3.Distance(new Vector3(spawnPosition.x, 0f, spawnPosition.z), new Vector3(stairwellExitPortals[1].position.x, 0f, stairwellExitPortals[1].position.z));
                targetPortal = (d1 <= d2) ? stairwellExitPortals[0] : stairwellExitPortals[1];
            }

            GameObject agentObj;
            if (studentPrefab != null)
            {
                agentObj = Instantiate(studentPrefab, spawnPosition, Quaternion.identity, agentsParent.transform);
                RandomizeBroModelAppearance(agentObj, i);
            }
            else
            {
                // Fallback capsule visualizer if prefab is not assigned
                agentObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                agentObj.transform.position = spawnPosition;
                agentObj.transform.SetParent(agentsParent.transform);
                var col = agentObj.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }

            agentObj.name = $"StudentAgent_{i:D3}";

            // Optimize animator: CullUpdateTransforms ensures off-screen / occluded characters do not cost skeletal computation
            Animator anim = agentObj.GetComponentInChildren<Animator>();
            bool hasSpeedParam = false;
            int speedHash = Animator.StringToHash("Speed");
            if (anim != null)
            {
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                // Ensure controller is wired
                if (anim.runtimeAnimatorController == null)
                {
#if UNITY_EDITOR
                    anim.runtimeAnimatorController = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                        "Assets/ResilientLogicGames/ChubyCharacterFree/Animations/Animator.controller"
                    );
#endif
                }

                foreach (var param in anim.parameters)
                {
                    if (param.nameHash == speedHash)
                    {
                        hasSpeedParam = true;
                        break;
                    }
                }

                // Start the Walk animation state directly with a staggered phase
                if (!hasSpeedParam && anim.HasState(0, Animator.StringToHash("Walk")))
                {
                    anim.Play("Walk", 0, UnityEngine.Random.Range(0f, 1f));
                }
            }

            int stairSide = (spawnPosition.x >= 0f) ? 0 : 1;
            EgressStage initStage = (Mathf.Abs(spawnPosition.z) > 1.15f)
                ? EgressStage.InRoom_MovingToCorridor
                : EgressStage.InCorridor_MovingToStairPortal;

            ManagedAgent agent = new ManagedAgent
            {
                id = i,
                gameObject = agentObj,
                transform = agentObj.transform,
                position = spawnPosition,
                velocity = Vector3.zero,
                radius = agentRadius,
                mass = agentMass,
                targetExitPortal = targetPortal,
                isEvacuated = false,
                animator = anim,
                speedHash = speedHash,
                hasSpeedParam = hasSpeedParam,
                currentFloor = 4,
                stairwellSide = stairSide,
                stage = initStage,
                currentTargetY = spawnPosition.y,
                hasExitedBuilding = false
            };

            _agents.Add(agent);
            activeAgentCount++;
        }

        Debug.Log($"[EvacuationEgressManager] Successfully spawned {agentCount} pedestrian agents! Look at 'CrowdSimulation_Agents' in the Hierarchy, or select 'StudentAgent_000' and press 'F' in the Scene view to focus.");
    }

    /// <summary>
    /// Randomizes each spawned student's face, clothes outfit, and hairstyle across the Low Poly Bros asset.
    /// Provides over 3,400 unique student appearance combinations so the crowd looks diverse and realistic.
    /// </summary>
    private void RandomizeBroModelAppearance(GameObject agentObj, int index)
    {
        if (agentObj == null) return;

        // 1. Gather all SkinnedMeshRenderers for clothes & hair
        SkinnedMeshRenderer[] allSmrs = agentObj.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        var outfitMeshes = new List<SkinnedMeshRenderer>();
        var hairMeshes = new List<SkinnedMeshRenderer>();

        foreach (var smr in allSmrs)
        {
            string smrName = smr.gameObject.name;

            if (smrName.StartsWith("Hair"))
            {
                hairMeshes.Add(smr);
            }
            else if (smrName.StartsWith("Bro.") && !smrName.Contains("Face"))
            {
                outfitMeshes.Add(smr);
            }
            else if (smrName == "Bro")
            {
                outfitMeshes.Add(smr);
            }
        }

        // 2. Randomize Outfit: Pick ONE clothing outfit out of 24 variations
        if (outfitMeshes.Count > 0)
        {
            int chosenOutfitIdx = (index * 7 + 11) % outfitMeshes.Count;
            for (int o = 0; o < outfitMeshes.Count; o++)
            {
                outfitMeshes[o].gameObject.SetActive(o == chosenOutfitIdx);
            }
        }

        // 3. Randomize Hairstyle: Pick ONE hair mesh out of 8 variations
        if (hairMeshes.Count > 0)
        {
            int chosenHairIdx = (index * 13 + 5) % hairMeshes.Count;
            for (int h = 0; h < hairMeshes.Count; h++)
            {
                hairMeshes[h].gameObject.SetActive(h == chosenHairIdx);
            }
        }

        // 4. Randomize Face Material: Assign one of 18 expressive face textures
        Renderer[] allRenderers = agentObj.GetComponentsInChildren<Renderer>(true);
        Renderer faceRenderer = null;
        foreach (var r in allRenderers)
        {
            if (r.gameObject.name.Contains("Face"))
            {
                faceRenderer = r;
                break;
            }
        }

        if (faceRenderer != null && faceMaterials != null && faceMaterials.Length > 0)
        {
            int chosenFaceIdx = (index * 3 + 7) % faceMaterials.Length;
            faceRenderer.sharedMaterial = faceMaterials[chosenFaceIdx];
        }
    }

    // =========================================================================
    // 3. HELBING SOCIAL FORCE PHYSICS ENGINE (SPATIAL HASH GRID OPTIMIZED)
    // =========================================================================

    private void SimulateHelbingSocialForces(float dt)
    {
        if (dt <= 0.0f || _agents.Count == 0) return;

        int totalAgents = _agents.Count;
        _simulationFrame++;

        // 1. Build Spatial Hash Grid (O(N) zero-allocation cell partitioning)
        Array.Fill(_spatialGridHead, -1);
        if (_spatialGridNext.Length < totalAgents)
        {
            _spatialGridNext = new int[totalAgents + 50];
        }

        for (int a = 0; a < totalAgents; a++)
        {
            ManagedAgent ag = _agents[a];
            if (ag.isEvacuated || ag.gameObject == null || !ag.gameObject.activeSelf) continue;

            int gx = Mathf.Clamp((int)(ag.position.x / CELL_SIZE) + GRID_OFFSET_X, 0, GRID_W - 1);
            int gz = Mathf.Clamp((int)(ag.position.z / CELL_SIZE) + GRID_OFFSET_Z, 0, GRID_H - 1);
            int cellIdx = gz * GRID_W + gx;

            _spatialGridNext[a] = _spatialGridHead[cellIdx];
            _spatialGridHead[cellIdx] = a;
        }

        float interactionCutoffSqr = 3.0f * 3.0f;

        // 2. Compute Helbing Social Forces for each active agent
        for (int i = 0; i < totalAgents; i++)
        {
            ManagedAgent agentI = _agents[i];
            if (agentI.isEvacuated || agentI.gameObject == null || !agentI.gameObject.activeSelf)
            {
                continue;
            }

            // --- 1. UPDATE MULTI-FLOOR EGRESS STAGE & HIERARCHICAL WAYPOINT ---
            UpdateAgentEgressStageAndWaypoint(agentI, dt);

            if (agentI.isEvacuated || agentI.gameObject == null || !agentI.gameObject.activeSelf)
            {
                continue;
            }

            // Local smoke density sample rho_smoke around agent position
            float rho_smoke = GetSmokeDensityAt(agentI.position);

            // Dynamic Beer-Lambert Walking Speed Attenuation:
            // modified_speed = desiredSpeed * exp(-alpha_ext * rho_smoke)
            float modifiedSpeed = desiredSpeed * Mathf.Exp(-alpha_ext * rho_smoke);
            modifiedSpeed = Mathf.Max(0.25f, modifiedSpeed);

            // Direction pointing towards current stage waypoint
            Vector3 toWaypoint = agentI.currentWaypoint - agentI.position;
            toWaypoint.y = 0.0f; // Planar 2D displacement for horizontal driving
            float distToWp = toWaypoint.magnitude;
            Vector3 e_i = (distToWp > 0.001f) ? (toWaypoint / distToWp) : agentI.transform.forward;
            e_i.y = 0.0f;
            if (e_i.sqrMagnitude > 0.001f) e_i.Normalize();

            Vector3 desiredVelocityVector = e_i * modifiedSpeed;

            // --- a) DESIRED DRIVING FORCE: f_i^0 = mass * (v_desired - v_i) / tau ---
            Vector3 netForce = agentI.mass * (desiredVelocityVector - agentI.velocity) / relaxationTimeTau;
            netForce.y = 0.0f;

            // --- b) INTERPERSONAL REPULSIVE & FRICTION FORCES (f_ij) VIA SPATIAL HASH GRID ---
            int agGx = Mathf.Clamp((int)(agentI.position.x / CELL_SIZE) + GRID_OFFSET_X, 0, GRID_W - 1);
            int agGz = Mathf.Clamp((int)(agentI.position.z / CELL_SIZE) + GRID_OFFSET_Z, 0, GRID_H - 1);

            for (int ox = -1; ox <= 1; ox++)
            {
                int nx = agGx + ox;
                if (nx < 0 || nx >= GRID_W) continue;

                for (int oz = -1; oz <= 1; oz++)
                {
                    int nz = agGz + oz;
                    if (nz < 0 || nz >= GRID_H) continue;

                    int neighborCellIdx = nz * GRID_W + nx;
                    int j = _spatialGridHead[neighborCellIdx];

                    while (j != -1)
                    {
                        if (j != i)
                        {
                            ManagedAgent agentJ = _agents[j];
                            if (!agentJ.isEvacuated && agentJ.gameObject != null && agentJ.gameObject.activeSelf)
                            {
                                // STRICT SAME-FLOOR PROXIMITY FILTER:
                                // Agents on different floors or flights DO NOT horizontally repel each other!
                                if (Mathf.Abs(agentI.position.y - agentJ.position.y) <= 2.0f)
                                {
                                    float dx = agentI.position.x - agentJ.position.x;
                                    float dz = agentI.position.z - agentJ.position.z;
                                    float distSqr = dx * dx + dz * dz;

                                    if (distSqr <= interactionCutoffSqr)
                                    {
                                        if (distSqr < 0.000025f)
                                        {
                                            dx = UnityEngine.Random.Range(-0.02f, 0.02f);
                                            dz = UnityEngine.Random.Range(-0.02f, 0.02f);
                                            distSqr = dx * dx + dz * dz;
                                        }

                                        float d_ij = Mathf.Sqrt(distSqr);
                                        float nx_unit = dx / d_ij;
                                        float nz_unit = dz / d_ij;
                                        Vector3 normalVector = new Vector3(nx_unit, 0.0f, nz_unit);
                                        Vector3 tangentVector = new Vector3(-nz_unit, 0.0f, nx_unit);

                                        float r_ij = agentI.radius + agentJ.radius; // 0.6m
                                        float overlap = r_ij - d_ij; // g(r_ij - d_ij)

                                        // 1. Psychological exponential repulsive force: A * exp((r_ij - d_ij) / B) * n_ij
                                        float f_psychological = repulsiveAmplitudeA * Mathf.Exp(overlap / repulsiveScaleB);
                                        Vector3 f_ij = normalVector * f_psychological;

                                        // 2. Physical contact forces active during overlap (g(x) > 0)
                                        if (overlap > 0.0f)
                                        {
                                            float f_elastic = elasticConstantK * overlap;
                                            f_ij += normalVector * f_elastic;

                                            Vector3 relativeVelocity = agentJ.velocity - agentI.velocity;
                                            float deltaVt = Vector3.Dot(relativeVelocity, tangentVector);
                                            Vector3 f_friction = tangentVector * (slidingFrictionKappa * overlap * deltaVt);
                                            f_ij += f_friction;
                                        }

                                        netForce += f_ij;
                                    }
                                }
                            }
                        }
                        j = _spatialGridNext[j];
                    }
                }
            }

            // --- c) VR PLAYER-AGENT INTERACTION (Real-time physical avoidance & crowd navigation) ---
            if (playerTransform != null)
            {
                float pdx = agentI.position.x - playerTransform.position.x;
                float pdz = agentI.position.z - playerTransform.position.z;
                float pDistSqr = pdx * pdx + pdz * pdz;

                // Only compute if within sensory radius (3.0m) and on the same vertical story
                if (pDistSqr < 9.0f && Mathf.Abs(agentI.position.y - playerTransform.position.y) < 2.5f)
                {
                    float pDist = Mathf.Max(0.04f, Mathf.Sqrt(pDistSqr));
                    float pOverlap = (agentI.radius + 0.35f) - pDist;
                    Vector3 pNormal = new Vector3(pdx / pDist, 0.0f, pdz / pDist);

                    // Psychological repulsion away from player
                    float f_pPsych = repulsiveAmplitudeA * Mathf.Exp(pOverlap / repulsiveScaleB);
                    Vector3 f_pTotal = pNormal * f_pPsych;

                    // Physical body compression force if player and agent touch
                    if (pOverlap > 0.0f)
                    {
                        f_pTotal += pNormal * (elasticConstantK * pOverlap);
                    }

                    netForce += f_pTotal;
                }
            }

            // --- d) WALL / OBSTACLE BOUNDARY REPULSION (High-Speed BVH Raycasts & Centering Forces) ---
            Vector3 wallForce = ComputeWallObstacleRepulsion(agentI);
            netForce += wallForce;

            // --- NEWTONIAN NUMERICAL INTEGRATION ---
            // Acceleration = Net Force / mass
            Vector3 acceleration = netForce / agentI.mass;
            acceleration.y = 0.0f;

            // Velocity += Acceleration * deltaTime
            agentI.velocity += acceleration * dt;

            // Dynamic speed clamping to prevent unphysical explosive velocities
            float maxAllowedSpeed = modifiedSpeed * 2.8f;
            if (agentI.velocity.magnitude > maxAllowedSpeed)
            {
                agentI.velocity = agentI.velocity.normalized * maxAllowedSpeed;
            }

            // --- CONTINUOUS COLLISION SWEEP TEST (WALL/DOOR TUNNELING PREVENTION) ---
            Vector3 displacement = agentI.velocity * dt;
            float dispDist = displacement.magnitude;

            if (dispDist > 0.0001f)
            {
                Vector3 dispDir = displacement / dispDist;
                Vector3 castOrigin = agentI.position + Vector3.up * 0.5f;
                float castRadius = agentI.radius * 0.8f;

                if (Physics.SphereCast(castOrigin, castRadius, dispDir, out RaycastHit sweepHit, dispDist + 0.04f, wallObstacleMask, QueryTriggerInteraction.Ignore))
                {
                    bool isExitPortal = IsMainExitDoorPortal(sweepHit.point, sweepHit.collider);
                    if (!isExitPortal)
                    {
                        // Stop flush at obstacle surface and deflect remaining velocity along wall normal
                        float safeDist = Mathf.Max(0.0f, sweepHit.distance - 0.02f);
                        agentI.position += dispDir * safeDist;

                        Vector3 wallNormal = sweepHit.normal;
                        wallNormal.y = 0.0f;
                        if (wallNormal.sqrMagnitude > 0.001f)
                        {
                            wallNormal.Normalize();
                            agentI.velocity = Vector3.ProjectOnPlane(agentI.velocity, wallNormal);
                        }
                        else
                        {
                            agentI.velocity = Vector3.zero;
                        }
                    }
                    else
                    {
                        agentI.position += displacement;
                    }
                }
                else
                {
                    agentI.position += displacement;
                }
            }

            // --- e) AIRTIGHT WALL & FLOOR BOUNDARY ENVELOPE ENFORCEMENT ---
            EnforceAgentBuildingBoundaries(agentI, dt);

            // Rotate agent character transform to face movement direction
            if (agentI.velocity.sqrMagnitude > 0.02f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(agentI.velocity.normalized, Vector3.up);
                agentI.transform.rotation = Quaternion.Slerp(agentI.transform.rotation, targetRotation, 12.0f * dt);
            }

            // Staggered animator parameter update to cut Animator hash load by 50%
            if (agentI.animator != null && agentI.hasSpeedParam && ((i + _simulationFrame) % 2 == 0))
            {
                agentI.animator.SetFloat(agentI.speedHash, agentI.velocity.magnitude);
            }
        }
    }

    /// <summary>
    /// Computes physical boundary repulsions keeping agents inside corridors and away from walls.
    /// Incorporates corridor centerline guiding forces and stairwell channel containment.
    /// Utilizes 5 targeted hardware-accelerated BVH raycasts to avoid expensive mesh collider closest-point solver stalls.
    /// </summary>
    private Vector3 ComputeWallObstacleRepulsion(ManagedAgent agent)
    {
        Vector3 wallForce = Vector3.zero;
        Vector3 origin = agent.position + Vector3.up * 0.5f;

        Vector3 fwd = agent.velocity.sqrMagnitude > 0.04f ? agent.velocity.normalized : agent.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        else fwd.Normalize();

        Vector3 right = new Vector3(-fwd.z, 0f, fwd.x);

        // 1. 5 targeted sensory ray directions: Forward, 45-deg Left, 45-deg Right, 90-deg Left, 90-deg Right
        for (int r = 0; r < 5; r++)
        {
            Vector3 rayDir;
            switch (r)
            {
                case 0: rayDir = fwd; break;
                case 1: rayDir = (fwd - right * 0.707f).normalized; break;
                case 2: rayDir = (fwd + right * 0.707f).normalized; break;
                case 3: rayDir = -right; break;
                default: rayDir = right; break;
            }

            if (Physics.Raycast(origin, rayDir, out RaycastHit hit, wallRepulsionDistance, wallObstacleMask, QueryTriggerInteraction.Ignore))
            {
                float dist = hit.distance;
                Vector3 wallNormal = hit.normal;
                wallNormal.y = 0f;
                if (wallNormal.sqrMagnitude > 0.001f)
                {
                    wallNormal.Normalize();
                    float wallOverlap = agent.radius - dist;
                    float f_rep = wallRepulsiveAmplitude * Mathf.Exp(wallOverlap / wallRepulsiveScale);

                    if (wallOverlap > 0f)
                    {
                        f_rep += elasticConstantK * wallOverlap;
                        float normalVel = Vector3.Dot(agent.velocity, -wallNormal);
                        if (normalVel > 0f)
                        {
                            agent.velocity += wallNormal * normalVel;
                        }
                    }

                    wallForce += wallNormal * f_rep;
                }
            }
        }

        // 2. Analytical Corridor Boundary Guidance:
        // In the central corridor (|Z| <= 1.15m), guide agents naturally along the corridor centerline
        if (Mathf.Abs(agent.position.z) <= 1.15f &&
            (agent.stage == EgressStage.InCorridor_MovingToStairPortal || agent.stage == EgressStage.InGroundCorridor_MovingToMainExit))
        {
            if (agent.position.z > 0.70f)
            {
                wallForce += Vector3.back * (wallRepulsiveAmplitude * 0.5f);
            }
            else if (agent.position.z < -0.70f)
            {
                wallForce += Vector3.forward * (wallRepulsiveAmplitude * 0.5f);
            }
        }

        // 3. Analytical Stair Flight Channel Containment:
        // Along the stair flights, keep agents centered in the 1.4m flight channel:
        if (agent.stage == EgressStage.InFlight_DescendingToLowerLanding)
        {
            if (agent.stairwellSide == 0) // East flight (channel centered at X = 11.15m)
            {
                if (agent.position.x < 10.65f)
                {
                    wallForce += Vector3.right * (wallRepulsiveAmplitude * 0.6f);
                }
                else if (agent.position.x > 11.65f)
                {
                    wallForce += Vector3.left * (wallRepulsiveAmplitude * 0.6f);
                }
            }
            else // West flight (channel centered at X = -11.15m)
            {
                if (agent.position.x > -10.65f)
                {
                    wallForce += Vector3.left * (wallRepulsiveAmplitude * 0.6f);
                }
                else if (agent.position.x < -11.65f)
                {
                    wallForce += Vector3.right * (wallRepulsiveAmplitude * 0.6f);
                }
            }
        }

        return wallForce;
    }

    /// <summary>
    /// Samples the exact vertical height of the current stair step or ramp surface under an agent or player.
    /// Continuously queries the physical BoxCollider bounds (bounds.max.y) of the stair step objects
    /// (e.g. EastStep_... / WestStep_...) or floor slabs so agents smoothly climb and descend steps
    /// without clipping through empty space or floating/falling through.
    /// </summary>
    public static bool SampleStairStepHeight(Vector3 samplePos, LayerMask mask, out float stepSurfaceY, out Collider hitCollider)
    {
        stepSurfaceY = samplePos.y;
        hitCollider = null;

        // 1. Primary downward raycast from 0.5m above the query position
        Vector3 rayOrigin = samplePos + Vector3.up * 0.5f;
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 2.2f, mask, QueryTriggerInteraction.Ignore))
        {
            hitCollider = hit.collider;
            if (hitCollider != null)
            {
                // If it is a BoxCollider (such as individual step colliders or landing slabs),
                // query bounds.max.y for the exact horizontal top surface of the step tread!
                if (hitCollider is BoxCollider boxCol)
                {
                    string colName = hitCollider.gameObject.name.ToLower();
                    if (colName.Contains("step") || colName.Contains("landing") || colName.Contains("slab") || colName.Contains("flight"))
                    {
                        stepSurfaceY = boxCol.bounds.max.y;
                        return true;
                    }
                }

                // For sloped ramps or mesh colliders, use exact contact point
                stepSurfaceY = hit.point.y;
                return true;
            }
        }

        // 2. Secondary fallback: OverlapSphere query to catch step tread edges if the raycast missed the tread
        int overlapCount = Physics.OverlapSphereNonAlloc(samplePos + Vector3.up * 0.15f, 0.40f, _stepOverlapBuffer, mask, QueryTriggerInteraction.Ignore);
        float highestStepY = float.MinValue;
        Collider bestCol = null;

        for (int i = 0; i < overlapCount; i++)
        {
            Collider col = _stepOverlapBuffer[i];
            if (col == null) continue;

            string cName = col.gameObject.name.ToLower();
            if (cName.Contains("step") || cName.Contains("landing") || cName.Contains("ramp") || cName.Contains("slab"))
            {
                float topY = col.bounds.max.y;
                if (topY <= samplePos.y + 0.45f && topY > highestStepY)
                {
                    highestStepY = topY;
                    bestCol = col;
                }
            }
        }

        if (bestCol != null && highestStepY > float.MinValue)
        {
            stepSurfaceY = highestStepY;
            hitCollider = bestCol;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Strict exit restriction portal check:
    /// The ONLY authorized egress exit from the hostel is the Ground Floor Main Entrance Double Door.
    /// Location: Ground floor (Y <= 1.5m), X in [-1.6m, +1.6m], Z in [-5.6m, -5.1m].
    /// Any movement outside the building envelope on upper floors or through solid walls is strictly blocked.
    /// </summary>
    public static bool IsMainExitDoorPortal(Vector3 point, Collider col = null)
    {
        // Must be on Ground floor level (elevation Y <= 1.5m)
        if (point.y > 1.5f) return false;

        // Must be within the doorway lateral opening width
        if (Mathf.Abs(point.x) > 1.6f) return false;

        // Must be at the south entrance threshold
        if (point.z > -5.0f || point.z < -5.8f) return false;

        return true;
    }

    /// <summary>
    /// Updates hierarchical egress navigation stage, target waypoint, and physical stair slope tracking
    /// for each simulated pedestrian agent across the 5 stories of the hostel building.
    /// Enforces strict stairway alignment to prevent clipping through empty space/voids between floors.
    /// </summary>
    private void UpdateAgentEgressStageAndWaypoint(ManagedAgent ag, float dt)
    {
        float signX = (ag.stairwellSide == 0) ? 1.0f : -1.0f;
        float floorY = FloorElevations[ag.currentFloor];

        switch (ag.stage)
        {
            case EgressStage.InRoom_MovingToCorridor:
                // Guide agent out through the room door into the central corridor
                // North rooms are at Z in [1.4, 2.7], door threshold at Z ~ 0.95m
                // South rooms are at Z in [-2.7, -1.4], door threshold at Z ~ -0.95m
                float targetZ = (ag.position.z > 0f) ? 0.75f : -0.75f;
                ag.currentWaypoint = new Vector3(ag.position.x, floorY, targetZ);
                ag.currentTargetY = floorY;

                if (Mathf.Abs(ag.position.z) <= 1.0f)
                {
                    ag.stage = EgressStage.InCorridor_MovingToStairPortal;
                }
                break;

            case EgressStage.InCorridor_MovingToStairPortal:
                // Move along central corridor towards assigned stairwell archway entrance
                // East archway portal: X = 7.5m, Z = 0.0m
                // West archway portal: X = -7.5m, Z = 0.0m
                ag.currentWaypoint = new Vector3(signX * 7.5f, floorY, 0.0f);
                ag.currentTargetY = floorY;

                // When near stairwell archway
                if (Mathf.Abs(ag.position.x) >= 7.3f)
                {
                    ag.stage = EgressStage.InDoorLanding_MovingToNorthLanding;
                }
                break;

            case EgressStage.InDoorLanding_MovingToNorthLanding:
                // STRICT STAIRWAY ALIGNMENT:
                // Step 1: Move towards North Landing (X = +/-9.75m, Z = 2.1m)
                // Step 2: Once at North Landing (|X| >= 9.4m, Z >= 1.8m), target the flight top entry at (X = +/-11.2m, Z = 1.65m)
                // This prevents agents from cutting diagonally across the stairwell void!
                if (Mathf.Abs(ag.position.x) < 10.5f && ag.position.z < 1.8f)
                {
                    ag.currentWaypoint = new Vector3(signX * 9.75f, floorY, 2.1f);
                }
                else
                {
                    // Align squarely with the top step tread (Step 15 at X = +/-11.2m, Z = 1.65m)
                    ag.currentWaypoint = new Vector3(signX * 11.2f, floorY, 1.65f);
                }
                ag.currentTargetY = floorY;

                // When squarely aligned with the flight entrance tread
                if (ag.position.z >= 1.55f && Mathf.Abs(ag.position.x) >= 10.75f)
                {
                    ag.stage = EgressStage.InFlight_DescendingToLowerLanding;
                }
                break;

            case EgressStage.InFlight_DescendingToLowerLanding:
                // Enter stair flight channel (X = +/-11.2m) and walk South along the step tread centerline towards Z = -1.85m
                float lowerFloorY = FloorElevations[Mathf.Max(0, ag.currentFloor - 1)];
                ag.currentWaypoint = new Vector3(signX * 11.2f, lowerFloorY, -1.85f);

                // Sample exact stair step BoxCollider bounds under the agent's feet
                float stepSurfaceY;
                if (SampleStairStepHeight(ag.position, wallObstacleMask, out stepSurfaceY, out Collider hitCol))
                {
                    ag.currentTargetY = stepSurfaceY;
                }
                else
                {
                    // Fallback to theoretical slope elevation along the flight
                    float tRamp = Mathf.Clamp01((1.59375f - ag.position.z) / 3.1875f);
                    ag.currentTargetY = Mathf.Lerp(floorY, lowerFloorY, tRamp);
                }

                // Step-climbing and smooth vertical stair tracking (snapping to step surface)
                ag.position.y = Mathf.MoveTowards(ag.position.y, ag.currentTargetY, 6.0f * dt);

                // When arrived at bottom of flight (South landing Z <= -1.65m)
                if (ag.position.z <= -1.65f)
                {
                    ag.currentFloor--;
                    ag.stage = EgressStage.InLowerLanding_TraversingToNextFlight;
                }
                break;

            case EgressStage.InLowerLanding_TraversingToNextFlight:
                float currentLandingY = FloorElevations[ag.currentFloor];
                ag.currentTargetY = currentLandingY;
                ag.position.y = Mathf.MoveTowards(ag.position.y, currentLandingY, 6.0f * dt);

                if (ag.currentFloor > 0)
                {
                    // On intermediate floor (Floor 3, 2, or 1):
                    // Agent stepped off the bottom of the flight at Z = -1.65m, X = +/-11.2m.
                    // Step 1: Clear the flight channel laterally to South Landing (X = +/-9.75m, Z = -2.1m)
                    if (Mathf.Abs(ag.position.x) > 10.3f)
                    {
                        ag.currentWaypoint = new Vector3(signX * 9.75f, currentLandingY, -2.1f);
                    }
                    else if (ag.position.z < 1.8f)
                    {
                        // Step 2: Traverse landing northward past DoorLanding (Z = 0) towards North Landing (Z = +2.1m)
                        ag.currentWaypoint = new Vector3(signX * 9.75f, currentLandingY, 2.1f);
                    }
                    else
                    {
                        // Step 3: Align with top of next downward flight (X = +/-11.2m, Z = 1.65m)
                        ag.currentWaypoint = new Vector3(signX * 11.2f, currentLandingY, 1.65f);
                    }

                    // Once aligned on the top step of the next flight
                    if (ag.position.z >= 1.55f && Mathf.Abs(ag.position.x) >= 10.75f)
                    {
                        ag.stage = EgressStage.InFlight_DescendingToLowerLanding;
                    }
                }
                else
                {
                    // Arrived on Ground Floor!
                    // Move from Ground South Landing (Z = -2.1m) to Ground Door Landing (Z = 0, X = +/-9.0m)
                    ag.currentWaypoint = new Vector3(signX * 9.0f, FloorElevations[0], 0.0f);

                    if (ag.position.z >= -0.3f)
                    {
                        ag.stage = EgressStage.InGroundCorridor_MovingToMainExit;
                    }
                }
                break;

            case EgressStage.InGroundCorridor_MovingToMainExit:
                // Move towards Ground Floor central corridor foyer, then to Main Entrance Double Door (X=0, Z=-5.4m)
                ag.currentTargetY = FloorElevations[0];
                ag.position.y = FloorElevations[0];

                if (Mathf.Abs(ag.position.x) > 1.8f)
                {
                    // Head to corridor central axis near entrance foyer
                    ag.currentWaypoint = new Vector3(0.0f, FloorElevations[0], -2.5f);
                }
                else
                {
                    // Directly target Main Entrance Double Door portal
                    ag.currentWaypoint = new Vector3(0.0f, FloorElevations[0], -5.4f);

                    // Panic hardware auto-opening: ensure entrance door swings open as agents approach
                    if (Mathf.Abs(ag.position.z - (-5.4f)) <= 3.5f)
                    {
                        if (HostelEntranceDoor.Instance != null && !HostelEntranceDoor.Instance.isOpen)
                        {
                            HostelEntranceDoor.Instance.OpenDoor();
                        }
                    }

                    if (ag.position.z <= -4.8f && Mathf.Abs(ag.position.x) <= 1.6f)
                    {
                        ag.stage = EgressStage.PassingThroughMainDoor;
                    }
                }
                break;

            case EgressStage.PassingThroughMainDoor:
                // Pass through the open main door portal out onto the Exterior Assembly Plaza
                ag.currentTargetY = FloorElevations[0];
                ag.currentWaypoint = new Vector3(0.0f, FloorElevations[0], -8.0f);
                ag.hasExitedBuilding = true;

                if (ag.position.z <= -5.8f)
                {
                    ag.stage = EgressStage.EvacuatedInPlaza;
                    ag.isEvacuated = true;
                    ag.gameObject.SetActive(false);
                    activeAgentCount--;
                    evacuatedAgentCount++;
                }
                break;

            case EgressStage.EvacuatedInPlaza:
                break;
        }
    }

    /// <summary>
    /// Strict Boundary Collision & Airtight Wall Containment for Pedestrian Agents:
    /// Characters cannot phase or glitch through exterior walls, floors, or ceilings.
    /// Exiting the hostel is strictly permitted ONLY via the designated Ground-Floor Main Entrance Double Door (Z <= -5.4m, X in [-1.6m, +1.6m]).
    /// </summary>
    private void EnforceAgentBuildingBoundaries(ManagedAgent ag, float dt)
    {
        Vector3 pos = ag.position;

        // Ground Floor Exit Portal Doorway: X in [-1.6m, 1.6m], Y <= 1.5m, Z around -5.4m
        bool isAtGroundExitPortal = (ag.currentFloor == 0 && Mathf.Abs(pos.x) <= 1.6f && pos.z <= -5.1f);

        // Exterior Assembly Plaza (outside building at Z < -5.35m)
        if (pos.z < -5.35f)
        {
            if (isAtGroundExitPortal || ag.hasExitedBuilding)
            {
                ag.hasExitedBuilding = true;
                pos.x = Mathf.Clamp(pos.x, -7.5f, 7.5f);
                pos.z = Mathf.Clamp(pos.z, -18.0f, -5.35f);
                pos.y = FloorElevations[0];
                ag.position = pos;
                ag.transform.position = pos;
                return;
            }
            else
            {
                // Attempted to exit through solid exterior wall on upper floors or outside the main entrance!
                // Rigidly block and push back inside!
                pos.z = -5.25f;
                if (ag.velocity.z < 0f) ag.velocity = new Vector3(ag.velocity.x, 0f, 0f);
            }
        }
        else
        {
            if (ag.currentFloor > 0)
            {
                ag.hasExitedBuilding = false;
            }
        }

        // Airtight Exterior Walls:
        // North Wall: Z = +5.4m (clamped to 5.25m)
        if (pos.z > 5.25f)
        {
            pos.z = 5.25f;
            if (ag.velocity.z > 0f) ag.velocity = new Vector3(ag.velocity.x, 0f, 0f);
        }

        // South Wall: Z = -5.4m (clamped to -5.25m unless at the ground exit doorway)
        if (pos.z < -5.25f && !isAtGroundExitPortal)
        {
            pos.z = -5.25f;
            if (ag.velocity.z < 0f) ag.velocity = new Vector3(ag.velocity.x, 0f, 0f);
        }

        // East Outer Wall: X = +12.0m (clamped to 11.85m)
        if (pos.x > 11.85f)
        {
            pos.x = 11.85f;
            if (ag.velocity.x > 0f) ag.velocity = new Vector3(0f, 0f, ag.velocity.z);
        }

        // West Outer Wall: X = -12.0m (clamped to -11.85m)
        if (pos.x < -11.85f)
        {
            pos.x = -11.85f;
            if (ag.velocity.x < 0f) ag.velocity = new Vector3(0f, 0f, ag.velocity.z);
        }

        // Vertical floor locking & height clamping:
        if (ag.stage == EgressStage.InFlight_DescendingToLowerLanding)
        {
            // Height is bounded between the lower floor and upper floor elevations
            float lowerFloorY = FloorElevations[Mathf.Max(0, ag.currentFloor - 1)];
            float upperFloorY = FloorElevations[ag.currentFloor];
            pos.y = Mathf.Clamp(pos.y, lowerFloorY, upperFloorY);
        }
        else
        {
            // Outside of active flights, strictly locked to current floor slab
            pos.y = FloorElevations[ag.currentFloor];
        }

        ag.position = pos;
        ag.transform.position = pos;
    }

    // =========================================================================
    // 4. PARTICLE SYSTEMS, LIGHTING, STROBES & OPTICAL ATTENUATION
    // =========================================================================

    private void UpdateParticleSystems()
    {
        // Floor 4 / 3 Particles
        UpdateSingleHazardParticles(smokeParticleSystem, fireParticleSystem, fireCoreGlowParticleSystem, embersParticleSystem);
        // Floor 2 Particles
        UpdateSingleHazardParticles(smokeParticleSystem2, fireParticleSystem2, fireCoreGlowParticleSystem2, embersParticleSystem2);
    }

    private void UpdateSingleHazardParticles(ParticleSystem smokePs, ParticleSystem firePs, ParticleSystem corePs, ParticleSystem embersPs)
    {
        if (smokePs != null)
        {
            var emission = smokePs.emission;
            emission.rateOverTime = Mathf.Lerp(6.0f, 75.0f, smokeDensity);

            var main = smokePs.main;
            float alpha = Mathf.Lerp(0.15f, 0.90f, smokeDensity);
            main.startColor = new Color(0.18f, 0.17f, 0.16f, alpha);
            main.startSize = Mathf.Lerp(1.5f, 4.2f, smokeDensity);
            main.startLifetime = Mathf.Lerp(4.0f, 9.0f, smokeDensity);
        }

        if (firePs != null)
        {
            var emission = firePs.emission;
            emission.rateOverTime = Mathf.Lerp(25.0f, 65.0f, Mathf.Clamp01(smokeDensity * 1.5f + 0.2f));
        }

        if (corePs != null)
        {
            var emission = corePs.emission;
            emission.rateOverTime = Mathf.Lerp(15.0f, 40.0f, smokeDensity + 0.3f);
        }

        if (embersPs != null)
        {
            var emission = embersPs.emission;
            emission.rateOverTime = Mathf.Lerp(8.0f, 35.0f, smokeDensity);
        }
    }

    private void UpdateOpticalAttenuation()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;

        // Linear extinction formula: mu = beta * rho
        float targetFogDensity = Mathf.Lerp(0.003f, 0.075f, smokeDensity * (beerLambertExtinctionCoeff / 0.45f));
        RenderSettings.fogDensity = targetFogDensity;

        // Shift fog color towards dense particulate smoke
        Color clearFogColor = new Color(0.68f, 0.70f, 0.74f);
        Color denseSmokeColor = new Color(0.14f, 0.12f, 0.11f);
        RenderSettings.fogColor = Color.Lerp(clearFogColor, denseSmokeColor, smokeDensity);

        // Attenuate ambient lighting
        Color clearAmbient = new Color(0.35f, 0.35f, 0.38f);
        Color smokyAmbient = new Color(0.06f, 0.05f, 0.04f);
        RenderSettings.ambientLight = Color.Lerp(clearAmbient, smokyAmbient, smokeDensity);
    }

    private void UpdateAggressiveFlicker()
    {
        float t = Time.time * flickerSpeed;

        // --- FLOOR 4 / 3 FIRE & LIGHTING ---
        if (fireFlickerLight != null)
        {
            float n1 = Mathf.PerlinNoise(t * 0.7f, 0.0f);
            float n2 = Mathf.PerlinNoise(0.0f, t * 1.5f);
            float n3 = Mathf.Sin(t * 2.3f) * 0.2f;
            float combinedNoise = (n1 * 0.6f + n2 * 0.4f + n3);

            fireFlickerLight.intensity = Mathf.Lerp(2.2f, 7.5f, Mathf.Clamp01(combinedNoise));
            fireFlickerLight.range = Mathf.Lerp(9.0f, 15.0f, n1);
            fireFlickerLight.color = Color.Lerp(new Color(1.0f, 0.02f, 0.0f), new Color(1.0f, 0.12f, 0.02f), n2);
        }

        if (floor3RedAmbientLight != null)
        {
            float redPulse = 3.0f + Mathf.Sin(Time.time * 6.0f) * 1.2f + (Mathf.PerlinNoise(t * 0.5f, 5.0f) * 1.5f);
            floor3RedAmbientLight.intensity = redPulse;
        }

        FlickerCorridorCeilingLights(floor3CorridorLights, _corridorLightBaseIntensities, t, 0f);
        UpdateDebrisGlow(glowingRedDebrisRenderers, t, 0f);
        UpdateFloorBedGlow(glowingEmberFloorRenderer, 0f);

        // --- FLOOR 2 FIRE & LIGHTING ---
        if (fireFlickerLight2 != null)
        {
            float n1 = Mathf.PerlinNoise(t * 0.75f, 7.3f);
            float n2 = Mathf.PerlinNoise(3.1f, t * 1.4f);
            float n3 = Mathf.Cos(t * 2.1f) * 0.2f;
            float combinedNoise = (n1 * 0.6f + n2 * 0.4f + n3);

            fireFlickerLight2.intensity = Mathf.Lerp(2.2f, 7.5f, Mathf.Clamp01(combinedNoise));
            fireFlickerLight2.range = Mathf.Lerp(9.0f, 15.0f, n1);
            fireFlickerLight2.color = Color.Lerp(new Color(1.0f, 0.02f, 0.0f), new Color(1.0f, 0.12f, 0.02f), n2);
        }

        if (floor2RedAmbientLight != null)
        {
            float redPulse = 3.0f + Mathf.Sin(Time.time * 5.5f + 1.2f) * 1.2f + (Mathf.PerlinNoise(t * 0.5f, 12.0f) * 1.5f);
            floor2RedAmbientLight.intensity = redPulse;
        }

        FlickerCorridorCeilingLights(floor2CorridorLights, _corridorLight2BaseIntensities, t, 41.5f);
        UpdateDebrisGlow(glowingRedDebrisRenderers2, t, 19.3f);
        UpdateFloorBedGlow(glowingEmberFloorRenderer2, 2.0f);
    }

    private void FlickerCorridorCeilingLights(Light[] lights, float[] baseIntensities, float t, float offset)
    {
        if (lights == null) return;

        for (int i = 0; i < lights.Length; i++)
        {
            var light = lights[i];
            if (light == null) continue;

            float baseInt = (baseIntensities != null && i < baseIntensities.Length)
                ? baseIntensities[i]
                : 1.0f;

            float lightNoise = Mathf.PerlinNoise(t * 0.9f + (i * 17.3f) + offset, t * 0.3f);
            float flickerFactor = Mathf.Lerp(0.2f, 1.2f, lightNoise);

            if (UnityEngine.Random.value < electricalDropoutChance)
            {
                flickerFactor = UnityEngine.Random.value < 0.3f ? 0.02f : 1.8f;
            }

            float smokeDimming = Mathf.Lerp(1.0f, 0.45f, smokeDensity);
            light.intensity = baseInt * flickerFactor * smokeDimming;
        }
    }

    private void UpdateDebrisGlow(Renderer[] debrisRenderers, float t, float offset)
    {
        if (debrisRenderers == null) return;

        float heatPulse = 3.5f + Mathf.Sin(Time.time * 8.0f + offset) * 1.5f + Mathf.PerlinNoise(t * 0.4f, 10.0f + offset) * 2.0f;
        Color emissionColor = new Color(1.0f, 0.05f, 0.01f) * heatPulse;

        foreach (var rend in debrisRenderers)
        {
            if (rend != null)
            {
                rend.GetPropertyBlock(_propBlock);
                _propBlock.SetColor("_EmissionColor", emissionColor);
                rend.SetPropertyBlock(_propBlock);
            }
        }
    }

    private void UpdateFloorBedGlow(Renderer emberFloorRend, float phaseOffset)
    {
        if (emberFloorRend != null)
        {
            float floorPulse = 2.5f + Mathf.Sin(Time.time * 5.0f + phaseOffset) * 1.0f;
            Color floorEmission = new Color(1.0f, 0.08f, 0.01f) * floorPulse;
            emberFloorRend.GetPropertyBlock(_propBlock);
            _propBlock.SetColor("_EmissionColor", floorEmission);
            emberFloorRend.SetPropertyBlock(_propBlock);
        }
    }

    private void UpdateStrobes()
    {
        _strobeTimer += Time.deltaTime * strobeFrequency;
        bool strobeState = (Mathf.Sin(_strobeTimer * Mathf.PI * 2.0f) > 0.15f);
        float strobeVal = strobeState ? strobeIntensityMax : 0.05f;

        if (eastStairStrobeRed != null)
        {
            eastStairStrobeRed.intensity = strobeVal;
        }

        if (westStairStrobeGreen != null)
        {
            westStairStrobeGreen.intensity = strobeVal;
        }
    }

    // =========================================================================
    // MATHEMATICAL MODELS & SPEED ATTENUATION GETTERS (YASHIKA'S LOGIC)
    // =========================================================================

    public float GetAttenuatedSpeed(float v0, float rho, float alpha = -1.0f)
    {
        if (alpha < 0f) alpha = alpha_ext;
        float clampedRho = Mathf.Clamp01(rho);
        return v0 * Mathf.Exp(-alpha * clampedRho);
    }

    public float GetCurrentCrowdSpeed(float v0 = -1.0f)
    {
        if (v0 <= 0f) v0 = desiredSpeed;
        return GetAttenuatedSpeed(v0, smokeDensity, alpha_ext);
    }

    public float GetSmokeDensityAt(Vector3 worldPosition)
    {
        float rho1 = ComputeLocalSmoke(worldPosition, hazardLocation);
        float rho2 = ComputeLocalSmoke(worldPosition, hazardLocationFloor2);
        return Mathf.Clamp01(Mathf.Max(rho1, rho2));
    }

    private float ComputeLocalSmoke(Vector3 worldPosition, Vector3 origin)
    {
        float dx = worldPosition.x - origin.x;
        float dz = worldPosition.z - origin.z;
        float planarDist = Mathf.Sqrt(dx * dx + dz * dz);

        float dy = worldPosition.y - origin.y;
        float verticalFactor;
        if (dy < -1.5f)
        {
            verticalFactor = Mathf.Clamp01(1.0f - (Mathf.Abs(dy) / 6.0f));
        }
        else if (dy >= -1.5f && dy <= 3.5f)
        {
            verticalFactor = 1.0f;
        }
        else
        {
            verticalFactor = Mathf.Clamp01(0.85f - ((dy - 3.5f) / 6.0f));
        }

        float radialFactor = Mathf.Clamp01(1.0f - (planarDist / hazardRadius));
        float diffusion = Mathf.Pow(radialFactor, 1.5f) * verticalFactor;

        return Mathf.Clamp01(smokeDensity * diffusion);
    }

    public float GetCrowdSpeedAtPosition(Vector3 agentPosition, float v0 = -1.0f)
    {
        if (v0 <= 0f) v0 = desiredSpeed;
        float localRho = GetSmokeDensityAt(agentPosition);
        return GetAttenuatedSpeed(v0, localRho, alpha_ext);
    }

    public float GetTransmittance(float distance, float rho)
    {
        float extinction = beerLambertExtinctionCoeff * Mathf.Clamp01(rho);
        return Mathf.Exp(-extinction * Mathf.Max(0.0f, distance));
    }

    public float GetOpticalDensity(float distance, float rho)
    {
        float t = GetTransmittance(distance, rho);
        return t > 0.00001f ? -Mathf.Log10(t) : 5.0f;
    }

    public float GetVisibilityDistance(float rho, float thresholdTransmittance = 0.05f)
    {
        float extinction = beerLambertExtinctionCoeff * Mathf.Clamp01(rho);
        if (extinction <= 0.001f) return 100.0f;
        return -Mathf.Log(thresholdTransmittance) / extinction;
    }

    // =========================================================================
    // 6. FIRE PROXIMITY AUDIO SYSTEM
    // =========================================================================

    private void InitializeFireProximityAudio()
    {
        // 1. Floor 3 Fire Hazard (East Stairwell: 7.0, 9.0, 0.0)
        SetupHazardFireAudio(gameObject, hazardLocation, "Floor 3 Fire Audio");

        // 2. Floor 2 Fire Hazard (West Stairwell: -6.5, 6.0, 0.0)
        GameObject f2Hazard = GameObject.Find("Floor2_FireSmokeHazard_WestStairwell");
        if (f2Hazard != null)
        {
            SetupHazardFireAudio(f2Hazard, hazardLocationFloor2, "Floor 2 Fire Audio");
        }
        else
        {
            f2Hazard = new GameObject("Floor2_FireSmokeHazard_WestStairwell");
            f2Hazard.transform.position = hazardLocationFloor2;
            SetupHazardFireAudio(f2Hazard, hazardLocationFloor2, "Floor 2 Fire Audio");
        }

        // 3. Scan for any other active fire hazards or particle systems in the scene
        var allParticleSystems = FindObjectsByType<ParticleSystem>();
        foreach (var ps in allParticleSystems)
        {
            string pName = ps.gameObject.name.ToLower();
            if (pName.Contains("fire") && !pName.Contains("ember") && !pName.Contains("flicker") && !pName.Contains("smoke"))
            {
                if (ps.GetComponent<FireProximityAudio>() == null && ps.GetComponentInParent<FireProximityAudio>() == null)
                {
                    SetupHazardFireAudio(ps.gameObject, ps.transform.position, ps.gameObject.name);
                }
            }
        }
    }

    private void SetupHazardFireAudio(GameObject hazardObj, Vector3 pos, string label)
    {
        if (hazardObj == null) return;
        var proximityAudio = hazardObj.GetComponent<FireProximityAudio>();
        if (proximityAudio == null)
        {
            proximityAudio = hazardObj.AddComponent<FireProximityAudio>();
        }
        proximityAudio.playerTransform = playerTransform;
        proximityAudio.EnsureAudioClipLoaded();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        RenderSettings.ambientLight = _initialAmbientColor;
        RenderSettings.fogDensity = _initialFogDensity;
    }
}
