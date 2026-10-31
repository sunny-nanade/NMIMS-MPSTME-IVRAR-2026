using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

[Serializable]
public class UnitySensorPacket
{
    public int seq;
    public float sim_time;
    public float[] position;
    public float[] forward_xz;
    public float roll_deg;
    public float pitch_deg;
    public float[] local_velocity;
    public float yaw_rate_rad_s;
    public float[] goal_xz;
    public float[] goal_local_xz;
    public float last_steer;
    public float last_throttle;
    public int wheel_contacts;
    public float slip;
    public int lidar_hits;
    public float lidar_min_m;
    public float[] occupancy_32x32;
}

[Serializable]
public class PythonActionPacket
{
    public int seq;
    public float steer;
    public float throttle;
    public bool ok;
    public string message;
}

public class UnityUdpBridge : MonoBehaviour
{
    [Header("References")]
    public RoverDrive roverDrive;
    public LidarGridSensor lidar;
    public Rigidbody roverBody;
    public Transform goal;

    [Tooltip("Optional V1.5f runtime-obstacle guidance deflector. Leave unassigned to disable.")]
    public DynamicGuidanceAvoidance dynamicGuidanceAvoidance;

    [Header("FINAL Phase-1 dynamic local-A* supervisor")]
    [Tooltip("For runtime obstacles only: while V4 local A* has a valid detour, use the same deterministic moving-target tracker that produced the successful V1.5f teacher demonstrations. Static/global navigation remains Direct SAC.")]
    public bool useDeterministicLocalTrackerDuringDynamicPlan = true;

    [Tooltip("1.0 = exact teacher steering during a dynamic local-A* detour. Recommended final Phase-1 setting.")]
    [Range(0f, 1f)]
    public float localTrackerSteerAuthority = 1.0f;

    [Tooltip("1.0 = exact teacher speed/throttle target during a dynamic local-A* detour.")]
    [Range(0f, 1f)]
    public float localTrackerThrottleAuthority = 1.0f;

    [Tooltip("Very-close obstacle distance at which the deterministic local tracker limits forward throttle, but does not dead-stop.")]
    public float localTrackerVeryCloseObstacleM = 1.80f;

    [Range(0.15f, 0.60f)]
    public float localTrackerVeryCloseThrottleCap = 0.34f;

    [Header("Dynamic local-A* teacher-consistent tracking")]
    [Tooltip("When the runtime local A* is active, gently bias SAC toward the same moving-target steering rule used by the successful V1.5f teacher demos. This is NOT the old safety shield.")]
    public bool enableDynamicTeacherTrackingAssist = true;

    [Tooltip("Blend toward the teacher steer when SAC already turns the same way.")]
    [Range(0f, 1f)]
    public float teacherSteerBlendSameDirection = 0.30f;

    [Tooltip("Stronger blend only when SAC steers opposite to the active local-A* target.")]
    [Range(0f, 1f)]
    public float teacherSteerBlendOppositeDirection = 0.72f;

    [Tooltip("Maximum steering change the teacher assist may add in one policy command.")]
    [Range(0.1f, 1f)]
    public float teacherSteerMaxCorrection = 0.80f;

    [Tooltip("Ignore tiny moving-target errors; SAC remains fully direct.")]
    [Range(0f, 30f)]
    public float teacherAssistMinBearingDeg = 10.0f;

    [Tooltip("Optional small throttle blend toward the original V1.5f teacher speed controller while a SAFE local detour exists.")]
    public bool enableDynamicTeacherThrottleBlend = true;

    [Range(0f, 0.6f)]
    public float teacherThrottleBlend = 0.22f;

    [Tooltip("Never force teacher throttle once a detected obstacle is closer than this.")]
    public float teacherThrottleDisableBelowLidarM = 2.40f;

    [Header("A* moving guidance")]
    public bool useMovingPathGuidance = true;
    public string globalPathCsvFileName = "global_path.csv";
    [Range(2f, 20f)] public float pathLookaheadMeters = 6f;

    [Tooltip("V1.5f was trained with a 6 m moving A* lookahead. Keep enabled for an exact policy-input test.")]
    public bool forceV15fLookahead = true;

    [Range(2f, 20f)]
    public float v15fLookaheadMeters = 6f;
    public bool drawGuidanceDebug = true;

    [Header("V1.5f policy test")]
    [Tooltip("When checked, apply the V1.5f SAC steer/throttle directly. This is the scientifically clean first Unity transfer test.")]
    public bool v15fDirectPolicyMode = true;

    [Tooltip("When direct-policy mode is OFF, the legacy A* + SAC + local-avoidance hybrid below is used.")]
    public bool useWeightedHybridControl = true;

    [Header("V1.5f deployment safety shield")]
    [Tooltip("Keep SAC as the primary controller, but allow a footprint-aware collision shield to intervene near rocks.")]
    public bool useSafetyShieldInDirectMode = true;

    [Tooltip("Also apply the same safety shield after the A*+SAC hybrid blend.")]
    public bool useSafetyShieldInHybridMode = true;

    [Range(0.25f, 1f)]
    public float safetySteerBlend = 0.85f;

    [Tooltip("Minimum swept-footprint lookahead used by the safety shield.")]
    public float minimumSafetySweepMeters = 3.0f;

    [Tooltip("Maximum dynamic lookahead. Kept inside the 12 m front half of the SAC local grid.")]
    public float maximumSafetySweepMeters = 7.0f;

    [Tooltip("Adds speed * seconds to the footprint sweep so it grows automatically at speed.")]
    public float safetyLookaheadSeconds = 2.5f;

    [Tooltip("Extra distance beyond the rover hard radius even when nearly stationary.")]
    public float safetyBaseExtraMeters = 0.75f;

    [Header("Weighted hybrid (legacy / safety-assisted)")]
    [Range(0f, 1f)] public float pathSteerWeight = 0.65f;
    [Range(0f, 1f)] public float sacSteerWeight = 0.25f;
    [Range(0f, 1f)] public float avoidanceSteerWeight = 0.45f;
    public float pathBearingForFullSteerDeg = 38f;

    [Header("FOOTPRINT-AWARE clearance")]
    [Tooltip("Same conservative circular rover footprint used by the PyBullet planner.")]
    public float roverFootprintRadius = 1.95f;

    [Tooltip("Extra clearance outside the wheel/chassis envelope.")]
    public float safetyMargin = 0.35f;

    [Tooltip("Start strong avoidance when predicted swept-footprint clearance falls below this.")]
    public float avoidanceClearance = 1.6f;

    [Tooltip("Emergency throttle cap when predicted clearance falls below this.")]
    public float emergencyClearance = 0.45f;

    [Tooltip("How far ahead of the rover center to sweep the footprint along the intended path.")]
    public float sweptPathLookahead = 4.5f;

    [Tooltip("Positive-throttle cap inside the avoidance clearance band.")]
    [Range(0f, 1f)] public float closeObstacleThrottleCap = 0.55f;

    [Tooltip("Positive-throttle cap at emergency clearance. Must still allow the rover to turn while moving.")]
    [Range(0f, 1f)] public float emergencyThrottleCap = 0.16f;

    [Tooltip("If the final shielded path has positive clearance but the rover is almost stopped, permit a small crawl so Ackermann steering can actually change heading.")]
    public bool useSafetyCrawlFloor = true;

    [Range(0f, 1f)] public float safetyCrawlThrottleFloor = 0.24f;

    [Range(0f, 0.25f)] public float safetyCrawlMaxSpeedMps = 0.10f;

    [Tooltip("Only apply crawl floor when the final predicted clearance is at least this much outside the hard rover footprint.")]
    public float safetyCrawlMinClearance = 0.20f;

    [Header("Anti-hesitation")]
    public bool useClearPathThrottleFloor = true;
    [Range(0f, 1f)] public float clearPathThrottleFloor = 0.22f;
    [Range(0f, 1f)] public float throttleFloorMaxPathSteer = 0.45f;

    [Header("UDP localhost")]
    public string pythonIp = "127.0.0.1";
    public int pythonPort = 5005;
    public int unityReceivePort = 5006;

    [Header("Policy timing")]
    public float policyHz = 8.333333f;
    public float actionTimeoutSeconds = 1.0f;

    [Header("Status")]
    public bool bridgeOnline;
    public bool waitingForRoverToSettle;
    public int lastSentSeq;
    public int lastReceivedSeq;
    public float lastActionAge;

    [Header("Moving-goal debug")]
    public bool pathLoaded;
    public int nearestPathIndex;
    public int guidancePathIndex;
    public float guidanceDistanceMeters;
    public Vector3 currentGuidanceWorld;

    [Header("Dynamic-guidance debug")]
    public bool dynamicGuidanceActive;
    public float dynamicGuidanceOffsetMeters;
    public float dynamicNominalClearanceMeters = 99f;
    public float dynamicSelectedClearanceMeters = 99f;
    public float dynamicThrottleCap = 1f;
    public float teacherTargetSteer;
    public float teacherAppliedSteerCorrection;
    public float teacherTargetThrottle;
    public bool deterministicLocalTrackerActive;

    [Header("Hybrid debug")]
    public float rawSacSteer;
    public float rawSacThrottle;
    public float pathSteer;
    public float avoidanceSteer;
    public float obstacleRisk;
    public float predictedMinClearance = 99f;
    public float finalSteer;
    public float finalThrottle;

    private UdpClient sender;
    private UdpClient receiver;
    private IPEndPoint pythonEndpoint;
    private float nextSendTime;
    private float lastActionTime;
    private bool wasSettled;

    private readonly List<Vector2> globalPath = new List<Vector2>();
    private readonly List<float> globalPathCumulative = new List<float>();
    private float[] lastOccupancy;
    private Vector3 lastGuidanceLocal;
    private int lastProjectionSegment;
    private bool haveProjection;

    private float HardRadius => roverFootprintRadius + safetyMargin;

    private void Start()
    {
        sender = new UdpClient();
        receiver = new UdpClient(unityReceivePort);
        receiver.Client.Blocking = false;
        pythonEndpoint = new IPEndPoint(IPAddress.Parse(pythonIp), pythonPort);
        lastActionTime = Time.time;
        wasSettled = false;

        if (forceV15fLookahead)
            pathLookaheadMeters = v15fLookaheadMeters;

        LoadGlobalPath();
    }

    private void Update()
    {
        if (roverDrive == null) return;

        waitingForRoverToSettle = !roverDrive.IsSettled;

        if (waitingForRoverToSettle)
        {
            bridgeOnline = false;
            roverDrive.SetAction(0f, 0f);
            return;
        }

        if (!wasSettled)
        {
            wasSettled = true;
            lastActionTime = Time.time;
            nextSendTime = 0f;
        }

        ReceiveLatestAction();

        lastActionAge = Time.time - lastActionTime;
        bridgeOnline = lastActionAge <= actionTimeoutSeconds;

        if (!bridgeOnline)
            roverDrive.SetAction(0f, 0f);

        if (Time.time >= nextSendTime)
        {
            nextSendTime = Time.time + 1f / Mathf.Max(1f, policyHz);
            SendObservation();
        }
    }

    private void LoadGlobalPath()
    {
        globalPath.Clear();
        globalPathCumulative.Clear();
        pathLoaded = false;
        haveProjection = false;
        lastProjectionSegment = 0;

        string path = Path.Combine(Application.streamingAssetsPath, globalPathCsvFileName);

        if (!File.Exists(path))
        {
            Debug.LogWarning("A* guidance disabled: CSV not found at " + path);
            return;
        }

        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] parts = line.Split(',');
            if (parts.Length < 2) continue;

            if (!float.TryParse(parts[0].Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float x)) continue;

            if (!float.TryParse(parts[1].Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float z)) continue;

            globalPath.Add(new Vector2(x, z));
        }

        pathLoaded = globalPath.Count >= 2;

        if (pathLoaded)
        {
            globalPathCumulative.Add(0f);
            for (int i = 1; i < globalPath.Count; i++)
            {
                float next =
                    globalPathCumulative[i - 1] +
                    Vector2.Distance(globalPath[i - 1], globalPath[i]);

                globalPathCumulative.Add(next);
            }
        }
    }

    private Vector3 GetGuidanceWorld()
    {
        float lookahead =
            forceV15fLookahead ? v15fLookaheadMeters : pathLookaheadMeters;

        if (!useMovingPathGuidance || !pathLoaded || roverBody == null)
            return goal != null
                ? goal.position
                : roverBody.position + roverBody.transform.forward * lookahead;

        Vector2 roverXZ =
            new Vector2(roverBody.position.x, roverBody.position.z);

        // Match V1.5f/PyBullet semantics more closely than nearest-path-vertex:
        // project continuously onto an A* segment, compute arc-length s, then take
        // a target exactly lookahead metres farther along the polyline.
        int nSeg = globalPath.Count - 1;
        int lo = 0;
        int hi = nSeg;

        if (haveProjection)
        {
            lo = Mathf.Max(0, lastProjectionSegment - 10);
            hi = Mathf.Min(nSeg, lastProjectionSegment + 100);

            if (hi <= lo)
            {
                lo = 0;
                hi = nSeg;
            }
        }

        float bestSq = float.PositiveInfinity;
        float bestS = 0f;
        int bestSeg = 0;

        for (int i = lo; i < hi; i++)
        {
            Vector2 a = globalPath[i];
            Vector2 b = globalPath[i + 1];
            Vector2 ab = b - a;
            float segSq = Vector2.Dot(ab, ab);

            if (segSq <= 1e-8f)
                continue;

            float t =
                Mathf.Clamp01(Vector2.Dot(roverXZ - a, ab) / segSq);

            Vector2 q = a + t * ab;
            float sq = (roverXZ - q).sqrMagnitude;
            float s =
                globalPathCumulative[i] + t * Mathf.Sqrt(segSq);

            // Prefer the farther-along projection on near-ties, matching the
            // anti-oscillation idea in RoverEnv._route_guidance_features().
            bool better =
                sq < bestSq - 1e-5f ||
                (Mathf.Abs(sq - bestSq) <= 1e-5f && s > bestS);

            if (better)
            {
                bestSq = sq;
                bestS = s;
                bestSeg = i;
            }
        }

        lastProjectionSegment = bestSeg;
        haveProjection = true;
        nearestPathIndex = bestSeg;

        float total =
            globalPathCumulative[globalPathCumulative.Count - 1];

        float targetS =
            Mathf.Min(total, bestS + Mathf.Max(1f, lookahead));

        int targetSeg = bestSeg;

        while (
            targetSeg < nSeg - 1 &&
            globalPathCumulative[targetSeg + 1] < targetS)
        {
            targetSeg++;
        }

        float segStartS = globalPathCumulative[targetSeg];
        float segEndS = globalPathCumulative[targetSeg + 1];
        float denom = Mathf.Max(1e-6f, segEndS - segStartS);

        float frac =
            Mathf.Clamp01((targetS - segStartS) / denom);

        Vector2 targetXZ =
            Vector2.Lerp(
                globalPath[targetSeg],
                globalPath[targetSeg + 1],
                frac
            );

        guidancePathIndex = targetSeg;
        guidanceDistanceMeters =
            Vector2.Distance(roverXZ, targetXZ);

        currentGuidanceWorld =
            new Vector3(
                targetXZ.x,
                roverBody.position.y,
                targetXZ.y
            );

        return currentGuidanceWorld;
    }

    private void SendObservation()
    {
        if (roverBody == null || roverDrive == null || lidar == null || goal == null) return;

        float[] occ = lidar.Scan();
        lastOccupancy = occ;

        Vector3 localVel = roverBody.transform.InverseTransformDirection(roverBody.linearVelocity);
        Vector3 forward = roverBody.transform.forward;

        float yawRateLeftPositive =
            -Vector3.Dot(roverBody.angularVelocity, roverBody.transform.up);

        Vector3 finalGoalWorld = goal.position;

        Vector3 nominalGuidanceWorld =
            GetGuidanceWorld();

        Vector3 guidanceWorld =
            nominalGuidanceWorld;

        if (dynamicGuidanceAvoidance != null &&
            dynamicGuidanceAvoidance.enableDynamicGuidanceAssist)
        {
            guidanceWorld =
                dynamicGuidanceAvoidance.AdjustGuidance(
                    nominalGuidanceWorld,
                    occ
                );

            dynamicGuidanceActive =
                dynamicGuidanceAvoidance.assistActive;

            dynamicGuidanceOffsetMeters =
                dynamicGuidanceAvoidance.currentTargetOffsetM;

            dynamicNominalClearanceMeters =
                dynamicGuidanceAvoidance.nominalCorridorClearanceM;

            dynamicSelectedClearanceMeters =
                dynamicGuidanceAvoidance.selectedCorridorClearanceM;

            dynamicThrottleCap =
                dynamicGuidanceAvoidance.recommendedThrottleCap;
        }
        else
        {
            dynamicGuidanceActive = false;
            dynamicGuidanceOffsetMeters = 0f;
            dynamicNominalClearanceMeters = 99f;
            dynamicSelectedClearanceMeters = 99f;
            dynamicThrottleCap = 1f;
        }

        currentGuidanceWorld =
            guidanceWorld;

        Vector3 guidanceLocal =
            roverBody.transform.InverseTransformPoint(
                guidanceWorld
            );

        lastGuidanceLocal =
            guidanceLocal;

        if (drawGuidanceDebug &&
            (dynamicGuidanceAvoidance == null ||
             !dynamicGuidanceAvoidance.drawDebug))
        {
            Debug.DrawLine(
                roverBody.position + Vector3.up * 1.4f,
                guidanceWorld + Vector3.up * 1.4f,
                Color.magenta,
                0.15f
            );
        }

        Vector3 e = roverBody.rotation.eulerAngles;

        UnitySensorPacket p = new UnitySensorPacket
        {
            seq = ++lastSentSeq,
            sim_time = Time.time,
            position = new float[] { roverBody.position.x, roverBody.position.y, roverBody.position.z },
            forward_xz = new float[] { forward.x, forward.z },
            pitch_deg = NormalizeAngle(e.x),
            roll_deg = NormalizeAngle(e.z),
            local_velocity = new float[] { localVel.x, localVel.y, localVel.z },
            yaw_rate_rad_s = yawRateLeftPositive,
            goal_xz = new float[] { finalGoalWorld.x, finalGoalWorld.z },
            goal_local_xz = new float[] { guidanceLocal.x, guidanceLocal.z },
            last_steer = roverDrive.currentSteer,
            last_throttle = roverDrive.currentThrottle,
            wheel_contacts = CountWheelContacts(),
            slip = EstimateSlip(),
            lidar_hits = lidar.hitCount,
            lidar_min_m = lidar.hitCount > 0 ? lidar.minHitDistance : lidar.maxRange,
            occupancy_32x32 = occ
        };

        string json = JsonUtility.ToJson(p);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        sender.Send(bytes, bytes.Length, pythonEndpoint);
    }

    private void ApplyDynamicTeacherTracking(
        ref float steer,
        ref float throttle)
    {
        teacherTargetSteer = 0f;
        teacherAppliedSteerCorrection = 0f;
        teacherTargetThrottle = throttle;
        deterministicLocalTrackerActive = false;

        if (dynamicGuidanceAvoidance == null ||
            !dynamicGuidanceAvoidance.enableDynamicGuidanceAssist ||
            !dynamicGuidanceAvoidance.assistActive ||
            !dynamicGuidanceAvoidance.localPlanFound ||
            roverBody == null ||
            roverDrive == null)
        {
            return;
        }

        // Exact V1.5f teacher moving-target steering relationship.
        // Bridge convention: positive steer = rover-left.
        float bearingRad =
            Mathf.Atan2(
                -lastGuidanceLocal.x,
                Mathf.Max(
                    0.01f,
                    lastGuidanceLocal.z
                )
            );

        float bearingDeg =
            bearingRad *
            Mathf.Rad2Deg;

        float maxSteerRad =
            Mathf.Max(
                1f,
                roverDrive.maxSteerAngle
            ) *
            Mathf.Deg2Rad;

        float normalizedTarget =
            Mathf.Clamp(
                bearingRad /
                Mathf.Max(
                    1e-5f,
                    maxSteerRad
                ),
                -1f,
                1f
            );

        float teacherSteer =
            Mathf.Clamp(
                1.05f *
                normalizedTarget,
                -1f,
                1f
            );

        teacherTargetSteer =
            teacherSteer;

        // Same speed logic used by the successful V1.5f teacher, adapted to
        // measured Unity forward velocity.
        float currentForwardSpeed =
            Vector3.Dot(
                roverBody.linearVelocity,
                roverBody.transform.forward
            );

        float speedFraction =
            Mathf.Clamp(
                0.80f -
                0.43f *
                Mathf.Abs(teacherSteer),
                0.30f,
                0.82f
            );

        float maxSpeed =
            Mathf.Max(
                0.1f,
                roverDrive.maxSpeedMps
            );

        float desiredSpeed =
            speedFraction *
            maxSpeed;

        float teacherThrottle =
            Mathf.Clamp(
                0.40f +
                0.70f *
                (
                    (desiredSpeed -
                     currentForwardSpeed) /
                    maxSpeed
                ),
                0.22f,
                0.88f
            );

        teacherTargetThrottle =
            teacherThrottle;

        // FINAL Phase-1 deployment rule:
        // Static/global route = SAC.
        // Unseen runtime obstacle with a valid local A* = deterministic
        // teacher-consistent tracker. This avoids asking an out-of-distribution
        // feed-forward SAC state to solve emergency local path tracking.
        if (useDeterministicLocalTrackerDuringDynamicPlan)
        {
            deterministicLocalTrackerActive = true;

            float oldSteer =
                steer;

            steer =
                Mathf.Lerp(
                    steer,
                    teacherSteer,
                    Mathf.Clamp01(
                        localTrackerSteerAuthority
                    )
                );

            teacherAppliedSteerCorrection =
                steer - oldSteer;

            throttle =
                Mathf.Lerp(
                    throttle,
                    teacherThrottle,
                    Mathf.Clamp01(
                        localTrackerThrottleAuthority
                    )
                );

            // If a physical obstacle is already very close, remain mobile but
            // reduce forward drive. Do not command zero, which caused the prior
            // deadlock. A valid local A* remains responsible for the direction.
            float lidarMin =
                lidar != null &&
                lidar.hitCount > 0
                ? lidar.minHitDistance
                : 99f;

            if (lidarMin <
                    localTrackerVeryCloseObstacleM &&
                throttle >
                    localTrackerVeryCloseThrottleCap)
            {
                throttle =
                    localTrackerVeryCloseThrottleCap;
            }

            steer =
                Mathf.Clamp(
                    steer,
                    -1f,
                    1f
                );

            throttle =
                Mathf.Clamp(
                    throttle,
                    -1f,
                    1f
                );

            return;
        }

        // Optional legacy blend mode retained for comparison only.
        if (!enableDynamicTeacherTrackingAssist ||
            Mathf.Abs(bearingDeg) <
                teacherAssistMinBearingDeg)
        {
            return;
        }

        bool oppositeDirection =
            Mathf.Abs(steer) > 0.05f &&
            Mathf.Sign(steer) !=
            Mathf.Sign(teacherSteer);

        float blend =
            oppositeDirection
            ? teacherSteerBlendOppositeDirection
            : teacherSteerBlendSameDirection;

        float proposed =
            Mathf.Lerp(
                steer,
                teacherSteer,
                Mathf.Clamp01(blend)
            );

        float correction =
            Mathf.Clamp(
                proposed - steer,
                -teacherSteerMaxCorrection,
                +teacherSteerMaxCorrection
            );

        steer =
            Mathf.Clamp(
                steer + correction,
                -1f,
                1f
            );

        teacherAppliedSteerCorrection =
            correction;

        if (!enableDynamicTeacherThrottleBlend)
            return;

        float minHit =
            lidar != null &&
            lidar.hitCount > 0
            ? lidar.minHitDistance
            : 99f;

        if (minHit >
            teacherThrottleDisableBelowLidarM)
        {
            throttle =
                Mathf.Lerp(
                    throttle,
                    teacherThrottle,
                    Mathf.Clamp01(
                        teacherThrottleBlend
                    )
                );
        }
    }

    private void ReceiveLatestAction()
    {
        try
        {
            while (receiver.Available > 0)
            {
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = receiver.Receive(ref remote);
                string json = Encoding.UTF8.GetString(data);
                PythonActionPacket a = JsonUtility.FromJson<PythonActionPacket>(json);

                if (a != null && a.ok && a.seq >= lastReceivedSeq)
                {
                    lastReceivedSeq = a.seq;

                    float steer = Mathf.Clamp(a.steer, -1f, 1f);
                    float throttle = Mathf.Clamp(a.throttle, -1f, 1f);

                    rawSacSteer = steer;
                    rawSacThrottle = throttle;

                    if (v15fDirectPolicyMode)
                    {
                        // SAC remains the primary controller.  The optional shield
                        // intervenes only when the predicted rover FOOTPRINT is at risk.
                        UpdateDirectPolicyDebug(steer);

                        if (useSafetyShieldInDirectMode)
                            ApplySafetyShield(ref steer, ref throttle);
                    }
                    else if (useWeightedHybridControl)
                    {
                        ApplyFootprintAwareHybrid(ref steer, ref throttle);

                        if (useSafetyShieldInHybridMode)
                            ApplySafetyShield(ref steer, ref throttle);
                    }

                    // Runtime dynamic-obstacle supervisor.
                    // With the final setting enabled, a VALID local-A* detour is
                    // tracked by the deterministic V1.5f teacher controller.
                    // Ordinary/static navigation remains Direct SAC.
                    ApplyDynamicTeacherTracking(
                        ref steer,
                        ref throttle
                    );

                    // Dynamic-guidance assist may still provide a throttle cap if
                    // explicitly enabled in that component.
                    if (dynamicGuidanceAvoidance != null &&
                        dynamicGuidanceAvoidance.enableDynamicGuidanceAssist &&
                        dynamicThrottleCap < 0.999f &&
                        throttle > dynamicThrottleCap)
                    {
                        throttle =
                            dynamicThrottleCap;
                    }

                    finalSteer = steer;
                    finalThrottle = throttle;

                    roverDrive.SetAction(steer, throttle);
                    lastActionTime = Time.time;
                }
                else if (a != null && !a.ok)
                {
                    Debug.LogWarning("Python bridge error: " + a.message);
                }
            }
        }
        catch (SocketException) { }
    }

    private void UpdateDirectPolicyDebug(float intendedSteer)
    {
        float bearingDeg =
            Mathf.Atan2(
                lastGuidanceLocal.x,
                Mathf.Max(0.01f, lastGuidanceLocal.z)
            ) * Mathf.Rad2Deg;

        // Positive SAC steer = rover-left; Unity local +X = rover-right.
        pathSteer =
            Mathf.Clamp(
                -bearingDeg / Mathf.Max(1f, pathBearingForFullSteerDeg),
                -1f,
                1f
            );

        AnalyzeSweptFootprint(
            intendedSteer,
            out predictedMinClearance,
            out obstacleRisk,
            out float leftRisk,
            out float rightRisk
        );

        avoidanceSteer = 0f;
    }

    private void ApplyFootprintAwareHybrid(ref float steer, ref float throttle)
    {
        float bearingDeg =
            Mathf.Atan2(
                lastGuidanceLocal.x,
                Mathf.Max(0.01f, lastGuidanceLocal.z)
            ) * Mathf.Rad2Deg;

        // Positive RoverDrive/SAC steering = LEFT; Unity local +X = RIGHT.
        pathSteer =
            Mathf.Clamp(
                -bearingDeg /
                Mathf.Max(1f, pathBearingForFullSteerDeg),
                -1f,
                1f
            );

        // First form the nominal A* + SAC command.  Obstacle avoidance is now
        // handled by the same safety shield used in direct-SAC mode.
        steer =
            Mathf.Clamp(
                pathSteerWeight * pathSteer +
                sacSteerWeight * rawSacSteer,
                -1f,
                1f
            );

        avoidanceSteer = 0f;

        if (useClearPathThrottleFloor &&
            Mathf.Abs(pathSteer) <= throttleFloorMaxPathSteer &&
            throttle > -0.05f)
        {
            throttle =
                Mathf.Max(
                    throttle,
                    clearPathThrottleFloor
                );
        }

        throttle =
            Mathf.Clamp(
                throttle,
                -1f,
                1f
            );
    }

    private void ApplySafetyShield(ref float steer, ref float throttle)
    {
        float baseSteer =
            Mathf.Clamp(steer, -1f, 1f);

        // 1) Evaluate the SAC/hybrid command as proposed.
        AnalyzeSweptFootprint(
            baseSteer,
            out float baseClearance,
            out float baseRisk,
            out float leftRisk,
            out float rightRisk
        );

        obstacleRisk = baseRisk;
        predictedMinClearance = baseClearance;
        avoidanceSteer = 0f;

        if (baseRisk > 0f)
        {
            float saferDirection;

            // Grid convention: negative lateral = rover-left.
            // Positive SAC/RoverDrive steer = rover-left.
            float diff =
                leftRisk - rightRisk;

            if (Mathf.Abs(diff) > 0.02f)
            {
                // More obstacle mass on left -> steer right (negative command).
                saferDirection =
                    diff > 0f ? -1f : +1f;
            }
            else
            {
                // Dead-ahead / nearly symmetric obstacle:
                // explicitly compare hard-left and hard-right sweeps.
                float leftClear =
                    EvaluateSweepClearance(+0.85f);

                float rightClear =
                    EvaluateSweepClearance(-0.85f);

                saferDirection =
                    leftClear >= rightClear ? +1f : -1f;
            }

            float blend =
                Mathf.Clamp01(
                    safetySteerBlend *
                    (0.25f + 0.75f * baseRisk)
                );

            avoidanceSteer =
                saferDirection * baseRisk;

            steer =
                Mathf.Lerp(
                    baseSteer,
                    saferDirection,
                    blend
                );
        }

        steer =
            Mathf.Clamp(
                steer,
                -1f,
                1f
            );

        // CRITICAL FIX:
        // Re-evaluate clearance AFTER the shield has changed steering.
        // The old code capped throttle using the unsafe ORIGINAL SAC sweep,
        // which could lock the rover at 0.06 throttle even after steering onto
        // a much safer path.
        AnalyzeSweptFootprint(
            steer,
            out float finalClearance,
            out float finalRisk,
            out _,
            out _
        );

        predictedMinClearance = finalClearance;

        // Use the larger risk only for debug visibility, but throttle is based
        // on the FINAL path clearance.
        obstacleRisk =
            Mathf.Max(
                baseRisk,
                finalRisk
            );

        if (finalClearance <= emergencyClearance)
        {
            if (throttle > emergencyThrottleCap)
                throttle = emergencyThrottleCap;
        }
        else if (finalClearance <= avoidanceClearance)
        {
            float t =
                Mathf.InverseLerp(
                    emergencyClearance,
                    avoidanceClearance,
                    finalClearance
                );

            float cap =
                Mathf.Lerp(
                    emergencyThrottleCap,
                    closeObstacleThrottleCap,
                    t
                );

            if (throttle > cap)
                throttle = cap;
        }

        // Ackermann steering cannot rotate the rover in place. If the rover is
        // almost stopped but the FINAL shielded path has some positive clearance,
        // permit a controlled crawl rather than remaining indefinitely at ~0 m/s.
        if (useSafetyCrawlFloor &&
            roverBody != null &&
            throttle > 0f &&
            finalClearance >= safetyCrawlMinClearance)
        {
            float forwardSpeed =
                Mathf.Abs(
                    Vector3.Dot(
                        roverBody.linearVelocity,
                        roverBody.transform.forward
                    )
                );

            if (forwardSpeed <= safetyCrawlMaxSpeedMps)
            {
                throttle =
                    Mathf.Max(
                        throttle,
                        safetyCrawlThrottleFloor
                    );
            }
        }

        throttle =
            Mathf.Clamp(
                throttle,
                -1f,
                1f
            );
    }


    private float EvaluateSweepClearance(float steer)
    {
        AnalyzeSweptFootprint(
            steer,
            out float clearance,
            out _,
            out _,
            out _
        );

        return clearance;
    }


    private void AnalyzeSweptFootprint(
        float intendedSteer,
        out float minClearance,
        out float risk,
        out float leftRisk,
        out float rightRisk)
    {
        minClearance = 99f;
        risk = 0f;
        leftRisk = 0f;
        rightRisk = 0f;

        if (lastOccupancy == null || lidar == null || lidar.gridSize <= 0)
            return;

        int n = lidar.gridSize;
        if (lastOccupancy.Length != n * n)
            return;

        float width = lidar.gridWidthMeters;
        float half = width * 0.5f;
        float cell = width / n;

        // Predict a short intended motion segment. This is deliberately simple:
        // it bends the endpoint toward the steering command so the OUTER wheels/
        // chassis swing are considered before the rover gets there.
        float steerAngleRad =
            intendedSteer *
            roverDrive.maxSteerAngle *
            Mathf.Deg2Rad;

        float signedForwardSpeed =
            roverBody != null
            ? Vector3.Dot(
                roverBody.linearVelocity,
                roverBody.transform.forward
              )
            : 0f;

        float dynamicLookahead =
            Mathf.Clamp(
                Mathf.Max(
                    minimumSafetySweepMeters,
                    HardRadius +
                    safetyBaseExtraMeters +
                    Mathf.Abs(signedForwardSpeed) *
                    safetyLookaheadSeconds
                ),
                minimumSafetySweepMeters,
                maximumSafetySweepMeters
            );

        float endForward =
            dynamicLookahead;

        // IMPORTANT SIGN FIX:
        // grid lateral < 0 = rover-left, while positive SAC steer = rover-left.
        // Therefore a positive steer must bend the swept path toward NEGATIVE
        // lateral. The previous +tan() sign predicted the opposite side.
        float endLateral =
            -Mathf.Tan(steerAngleRad) *
            dynamicLookahead *
            0.55f;

        Vector2 A = Vector2.zero; // rover center
        Vector2 B =
            new Vector2(
                endLateral,
                endForward
            );

        float hardRadius = HardRadius;

        // Only the forward half of the local map matters for imminent collision.
        for (int row = 0; row < n / 2; row++)
        {
            float forward =
                half - (row + 0.5f) * cell;

            if (forward <= 0f) continue;

            for (int col = 0; col < n; col++)
            {
                if (lastOccupancy[row * n + col] <= 0.5f)
                    continue;

                float lateral =
                    -half + (col + 0.5f) * cell;

                Vector2 P = new Vector2(lateral, forward);

                float distToSweptCenterline =
                    DistancePointToSegment(P, A, B);

                // Clearance is measured from the OUTSIDE of the conservative
                // wheel/chassis footprint, not from the rover center.
                float clearance =
                    distToSweptCenterline - hardRadius;

                if (clearance < minClearance)
                    minClearance = clearance;

                if (clearance <= avoidanceClearance)
                {
                    float localRisk =
                        1f - Mathf.InverseLerp(
                            emergencyClearance,
                            avoidanceClearance,
                            clearance
                        );

                    localRisk = Mathf.Clamp01(localRisk);
                    risk = Mathf.Max(risk, localRisk);

                    if (lateral < 0f)
                        leftRisk += 0.2f + 0.8f * localRisk;
                    else
                        rightRisk += 0.2f + 0.8f * localRisk;
                }
            }
        }
    }

    private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float denom = Vector2.Dot(ab, ab);

        if (denom < 1e-6f)
            return Vector2.Distance(p, a);

        float t = Mathf.Clamp01(
            Vector2.Dot(p - a, ab) / denom
        );

        Vector2 closest = a + t * ab;
        return Vector2.Distance(p, closest);
    }

    private int CountWheelContacts()
    {
        int n = 0;
        foreach (WheelCollider w in GetWheels())
            if (w != null && w.GetGroundHit(out WheelHit _)) n++;
        return n;
    }

    private float EstimateSlip()
    {
        WheelCollider[] wheels = GetWheels();
        float sum = 0f;
        int count = 0;

        foreach (WheelCollider w in wheels)
        {
            if (w == null) continue;

            Vector3 pointVelocity =
                roverBody.GetPointVelocity(w.transform.position);

            Vector3 rollingDirection =
                Quaternion.AngleAxis(w.steerAngle, w.transform.up) *
                w.transform.forward;

            float chassisRollingSpeed =
                Mathf.Abs(Vector3.Dot(pointVelocity, rollingDirection.normalized));

            float wheelRollingSpeed =
                Mathf.Abs(w.rpm * (2f * Mathf.PI / 60f) * w.radius);

            float denom =
                Mathf.Max(Mathf.Max(chassisRollingSpeed, wheelRollingSpeed), 0.15f);

            float slip =
                Mathf.Abs(wheelRollingSpeed - chassisRollingSpeed) / denom;

            sum += Mathf.Clamp01(slip);
            count++;
        }

        return count > 0 ? Mathf.Clamp01(sum / count) : 0f;
    }

    private WheelCollider[] GetWheels()
    {
        return new WheelCollider[]
        {
            roverDrive.frontLeft,
            roverDrive.frontRight,
            roverDrive.middleLeft,
            roverDrive.middleRight,
            roverDrive.rearLeft,
            roverDrive.rearRight
        };
    }

    private static float NormalizeAngle(float deg)
    {
        while (deg > 180f) deg -= 360f;
        while (deg < -180f) deg += 360f;
        return deg;
    }

    private void OnDestroy()
    {
        sender?.Close();
        receiver?.Close();
    }
}
