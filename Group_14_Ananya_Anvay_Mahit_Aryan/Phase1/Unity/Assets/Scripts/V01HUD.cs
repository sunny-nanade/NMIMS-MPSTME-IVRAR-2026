using UnityEngine;

public class V01HUD : MonoBehaviour
{
    public Rigidbody roverBody;
    public RoverDrive roverDrive;
    public UnityUdpBridge bridge;
    public LidarGridSensor lidar;
    public LatencyGhost latency;
    public Transform goal;
    public GoalTrigger goalTrigger;

    private GUIStyle titleStyle;
    private GUIStyle labelStyle;

    private void EnsureStyles()
    {
        if (titleStyle != null) return;

        titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize = 20;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.normal.textColor = Color.white;

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 14;
        labelStyle.normal.textColor = Color.white;
    }

    private void OnGUI()
    {
        EnsureStyles();

        GUI.Box(new Rect(15, 15, 430, 690), "");
        GUI.Label(new Rect(30, 25, 390, 30), "Planetary Rover V1.5f Unity Test", titleStyle);

        float speed = roverBody != null ? roverBody.linearVelocity.magnitude : 0f;
        float dist = (roverBody != null && goal != null) ? Vector3.Distance(roverBody.position, goal.position) : 0f;

        int y = 65;
        Draw("Controller", "V1.5f SAC-CNN best (700k)", ref y);
        Draw("Global planner", "A* + 6 m moving target", ref y);
        Draw("Local sensor", "Unity Raycast LiDAR", ref y);
        Draw("Python bridge", bridge != null && bridge.bridgeOnline ? "ONLINE" : "WAITING", ref y);
        Draw("Policy mode", bridge != null && bridge.v15fDirectPolicyMode ? "DIRECT SAC" : "HYBRID", ref y);
        Draw("A* lookahead", bridge != null ? bridge.pathLookaheadMeters.ToString("F1") + " m" : "-", ref y);
        Draw("Goal distance", dist.ToString("F1") + " m", ref y);
        Draw("Speed", speed.ToString("F2") + " m/s", ref y);
        Draw("Steer", roverDrive != null ? roverDrive.currentSteer.ToString("+0.00;-0.00;0.00") : "0", ref y);
        Draw("Throttle", roverDrive != null ? roverDrive.currentThrottle.ToString("+0.00;-0.00;0.00") : "0", ref y);
        Draw("LiDAR hits", lidar != null ? lidar.hitCount.ToString() : "0", ref y);
        Draw("Status", goalTrigger != null && goalTrigger.success ? "GOAL SUCCESS" : "AUTONOMOUS", ref y);
        if (bridge != null)
        {
            Draw("Raw SAC", bridge.rawSacSteer.ToString("+0.00;-0.00;0.00") + " / " + bridge.rawSacThrottle.ToString("+0.00;-0.00;0.00"), ref y);
            Draw("Final cmd", bridge.finalSteer.ToString("+0.00;-0.00;0.00") + " / " + bridge.finalThrottle.ToString("+0.00;-0.00;0.00"), ref y);
            Draw("Dyn guide", bridge.dynamicGuidanceActive ? "ACTIVE" : "inactive", ref y);
            Draw("Dyn offset", bridge.dynamicGuidanceOffsetMeters.ToString("+0.00;-0.00;0.00") + " m", ref y);
            Draw("Nom clearance", bridge.dynamicNominalClearanceMeters > 90f ? "-" : bridge.dynamicNominalClearanceMeters.ToString("F2") + " m", ref y);
            Draw("Detour clearance", bridge.dynamicSelectedClearanceMeters > 90f ? "-" : bridge.dynamicSelectedClearanceMeters.ToString("F2") + " m", ref y);
            Draw("Dyn throttle cap", bridge.dynamicThrottleCap.ToString("F2"), ref y);
            Draw("Teacher steer", bridge.teacherTargetSteer.ToString("+0.00;-0.00;0.00"), ref y);
            Draw("Teacher correction", bridge.teacherAppliedSteerCorrection.ToString("+0.00;-0.00;0.00"), ref y);
            Draw("Teacher throttle", bridge.teacherTargetThrottle.ToString("+0.00;-0.00;0.00"), ref y);
            Draw("Local tracker", bridge.deterministicLocalTrackerActive ? "TEACHER TAKEOVER" : "SAC", ref y);
            Draw("Safety risk", bridge.obstacleRisk.ToString("F2"), ref y);
            Draw("Final clearance", bridge.predictedMinClearance > 90f ? "-" : bridge.predictedMinClearance.ToString("F2") + " m", ref y);
            Draw("Avoid steer", bridge.avoidanceSteer.ToString("+0.00;-0.00;0.00"), ref y);
        }

        if (latency != null)
        {
            GUI.Label(new Rect(30, y + 4, 300, 20), "Simulated telemetry latency: " + latency.latencySeconds.ToString("F1") + " s", labelStyle);
            latency.latencySeconds = GUI.HorizontalSlider(new Rect(30, y + 28, 280, 18), latency.latencySeconds, 1.5f, 5f);
        }
    }

    private void Draw(string key, string value, ref int y)
    {
        GUI.Label(new Rect(30, y, 145, 22), key + ":", labelStyle);
        GUI.Label(new Rect(175, y, 250, 22), value, labelStyle);
        y += 24;
    }
}
