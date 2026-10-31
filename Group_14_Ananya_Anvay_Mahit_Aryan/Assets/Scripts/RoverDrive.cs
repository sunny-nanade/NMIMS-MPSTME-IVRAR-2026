using UnityEngine;

public class RoverDrive : MonoBehaviour
{
    [Header("Wheel colliders")]
    public WheelCollider frontLeft;
    public WheelCollider frontRight;
    public WheelCollider middleLeft;
    public WheelCollider middleRight;
    public WheelCollider rearLeft;
    public WheelCollider rearRight;

    [Header("Visible wheel meshes (optional)")]
    public Transform frontLeftMesh;
    public Transform frontRightMesh;
    public Transform middleLeftMesh;
    public Transform middleRightMesh;
    public Transform rearLeftMesh;
    public Transform rearRightMesh;

    [Header("Drive")]
    public float maxMotorTorque = 450f;
    public float maxSpeedMps = 0.95f;
    public float brakeTorque = 900f;

    [Range(0.1f, 1f)]
    public float actionGain = 1.0f;

    [Tooltip("Use +1 normally. If manual positive throttle drives Rover root toward -Z, use -1.")]
    public float motorDirection = 1f;

    [Header("V1.5f velocity matching")]
    [Tooltip("PyBullet used wheel VELOCITY control with max rover speed ~0.95 m/s. Keep enabled for Unity transfer.")]
    public bool useVelocityServo = true;

    [Range(0.25f, 2.5f)]
    public float velocityServoGain = 1.15f;

    [Tooltip("Apply active braking above the trained speed envelope.")]
    public bool useOverspeedBrake = true;

    [Range(0f, 0.5f)]
    public float overspeedToleranceMps = 0.08f;

    [Range(0f, 3f)]
    public float overspeedBrakeMultiplier = 1.35f;

    [Header("Steering - PyBullet matched")]
    public float maxSteerAngle = 38f;

    [Tooltip("1.55 rad/s = 88.81 deg/s.")]
    public float steeringSlewRateDegPerSec = 88.81f;

    [Tooltip("Checked = positive SAC steer means rover-left.")]
    public bool sacPositiveIsLeft = true;

    [Header("Startup settling")]
    public float startupSettlingSeconds = 1.25f;

    [Header("Debug")]
    [Range(-1f, 1f)] public float currentSteer;
    [Range(-1f, 1f)] public float currentThrottle;

    [HideInInspector] public float appliedFrontLeftDeg;
    [HideInInspector] public float appliedFrontRightDeg;
    [HideInInspector] public float appliedRearLeftDeg;
    [HideInInspector] public float appliedRearRightDeg;

    private const float FrontX = 0.92f;
    private const float MiddleX = -0.24f;
    private const float RearX = -1.26f;
    private const float WheelY = 1.13f;

    private const float SteeringCenterX = 0.5f * (FrontX + RearX);
    private const float HalfWheelbase = 0.5f * (FrontX - RearX);

    private Rigidbody rb;
    private float enabledAt;

    public bool IsSettled => Time.time - enabledAt >= startupSettlingSeconds;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void OnEnable()
    {
        enabledAt = Time.time;
        currentSteer = 0f;
        currentThrottle = 0f;
    }

    public void SetAction(float steer, float throttle)
    {
        currentSteer = Mathf.Clamp(steer, -1f, 1f);
        currentThrottle = Mathf.Clamp(throttle, -1f, 1f);
    }

    public void EmergencyStop()
    {
        currentSteer = 0f;
        currentThrottle = 0f;
        SetBrakeAll(brakeTorque);
        SetMotorAll(0f);
    }

    private void FixedUpdate()
    {
        if (!AllWheelsAssigned())
            return;

        if (!IsSettled)
        {
            SetMotorAll(0f);
            SetBrakeAll(0f);
            SlewSteerAllToZero();
            SyncVisibleWheels();
            return;
        }

        float signedForwardSpeed =
            Vector3.Dot(rb.linearVelocity, transform.forward);

        float turnSpeedScale =
            1f - 0.38f * Mathf.Abs(currentSteer);

        float throttle =
            currentThrottle * actionGain * turnSpeedScale;

        float desiredForwardSpeed =
            throttle * maxSpeedMps;

        float speedError =
            desiredForwardSpeed - signedForwardSpeed;

        bool overspeed =
            Mathf.Abs(signedForwardSpeed) >
            maxSpeedMps + overspeedToleranceMps;

        // PyBullet training used VELOCITY_CONTROL, not raw open-loop torque.
        // A simple speed servo keeps Unity inside the same ~0.95 m/s envelope.
        float driveEffort;
        if (useVelocityServo)
        {
            driveEffort =
                Mathf.Clamp(
                    (speedError / Mathf.Max(0.10f, maxSpeedMps)) *
                    velocityServoGain,
                    -1f,
                    1f
                );
        }
        else
        {
            driveEffort = throttle;
        }

        ComputeAckermann(
            currentSteer,
            out float flTarget,
            out float frTarget,
            out float rlTarget,
            out float rrTarget,
            out float flSpeed,
            out float frSpeed,
            out float mlSpeed,
            out float mrSpeed,
            out float rlSpeed,
            out float rrSpeed
        );

        float maxDelta =
            steeringSlewRateDegPerSec * Time.fixedDeltaTime;

        frontLeft.steerAngle =
            Mathf.MoveTowards(frontLeft.steerAngle, flTarget, maxDelta);

        frontRight.steerAngle =
            Mathf.MoveTowards(frontRight.steerAngle, frTarget, maxDelta);

        rearLeft.steerAngle =
            Mathf.MoveTowards(rearLeft.steerAngle, rlTarget, maxDelta);

        rearRight.steerAngle =
            Mathf.MoveTowards(rearRight.steerAngle, rrTarget, maxDelta);

        middleLeft.steerAngle = 0f;
        middleRight.steerAngle = 0f;

        appliedFrontLeftDeg = frontLeft.steerAngle;
        appliedFrontRightDeg = frontRight.steerAngle;
        appliedRearLeftDeg = rearLeft.steerAngle;
        appliedRearRightDeg = rearRight.steerAngle;

        float commandedBrake = 0f;

        if (Mathf.Abs(currentThrottle) < 0.03f)
            commandedBrake = brakeTorque * 0.25f;

        if (useOverspeedBrake && overspeed)
        {
            float over =
                Mathf.Abs(signedForwardSpeed) -
                (maxSpeedMps + overspeedToleranceMps);

            float frac =
                Mathf.Clamp01(
                    over / Mathf.Max(0.25f, maxSpeedMps)
                );

            commandedBrake =
                Mathf.Max(
                    commandedBrake,
                    brakeTorque *
                    overspeedBrakeMultiplier *
                    frac
                );

            // Do not keep driving farther into overspeed.
            if (Mathf.Sign(driveEffort) == Mathf.Sign(signedForwardSpeed))
                driveEffort = 0f;
        }

        SetBrakeAll(commandedBrake);

        float baseMotor =
            motorDirection * driveEffort * maxMotorTorque;

        frontLeft.motorTorque = baseMotor * flSpeed;
        frontRight.motorTorque = baseMotor * frSpeed;
        middleLeft.motorTorque = baseMotor * mlSpeed;
        middleRight.motorTorque = baseMotor * mrSpeed;
        rearLeft.motorTorque = baseMotor * rlSpeed;
        rearRight.motorTorque = baseMotor * rrSpeed;

        SyncVisibleWheels();
    }

    private void ComputeAckermann(
        float steer,
        out float flDeg,
        out float frDeg,
        out float rlDeg,
        out float rrDeg,
        out float flSpeed,
        out float frSpeed,
        out float mlSpeed,
        out float mrSpeed,
        out float rlSpeed,
        out float rrSpeed)
    {
        steer = Mathf.Clamp(steer, -1f, 1f);

        if (Mathf.Abs(steer) < 1e-5f)
        {
            flDeg = frDeg = rlDeg = rrDeg = 0f;
            flSpeed = frSpeed = mlSpeed = mrSpeed = rlSpeed = rrSpeed = 1f;
            return;
        }

        float maxSteerRad = maxSteerAngle * Mathf.Deg2Rad;
        float tmax = Mathf.Tan(maxSteerRad);
        float kmax = tmax / (HalfWheelbase + tmax * WheelY);
        float curvature = steer * kmax;

        ComputeWheel(FrontX, +WheelY, curvature, true, out float flModelRad, out flSpeed);
        ComputeWheel(FrontX, -WheelY, curvature, true, out float frModelRad, out frSpeed);
        ComputeWheel(MiddleX, +WheelY, curvature, false, out _, out mlSpeed);
        ComputeWheel(MiddleX, -WheelY, curvature, false, out _, out mrSpeed);
        ComputeWheel(RearX, +WheelY, curvature, true, out float rlModelRad, out rlSpeed);
        ComputeWheel(RearX, -WheelY, curvature, true, out float rrModelRad, out rrSpeed);

        float unitySign = sacPositiveIsLeft ? -1f : 1f;

        flDeg = unitySign * Mathf.Clamp(flModelRad, -maxSteerRad, maxSteerRad) * Mathf.Rad2Deg;
        frDeg = unitySign * Mathf.Clamp(frModelRad, -maxSteerRad, maxSteerRad) * Mathf.Rad2Deg;
        rlDeg = unitySign * Mathf.Clamp(rlModelRad, -maxSteerRad, maxSteerRad) * Mathf.Rad2Deg;
        rrDeg = unitySign * Mathf.Clamp(rrModelRad, -maxSteerRad, maxSteerRad) * Mathf.Rad2Deg;
    }

    private void ComputeWheel(
        float longitudinalX,
        float lateralY,
        float curvature,
        bool steerable,
        out float steerRad,
        out float speedScale)
    {
        float xRel = longitudinalX - SteeringCenterX;
        float longitudinalScale = 1f - curvature * lateralY;
        float lateralScale = curvature * xRel;

        speedScale =
            Mathf.Max(
                0.10f,
                Mathf.Sqrt(
                    longitudinalScale * longitudinalScale +
                    lateralScale * lateralScale
                )
            );

        steerRad =
            steerable
            ? Mathf.Atan2(lateralScale, longitudinalScale)
            : 0f;
    }

    private void SlewSteerAllToZero()
    {
        float maxDelta =
            steeringSlewRateDegPerSec * Time.fixedDeltaTime;

        frontLeft.steerAngle =
            Mathf.MoveTowards(frontLeft.steerAngle, 0f, maxDelta);

        frontRight.steerAngle =
            Mathf.MoveTowards(frontRight.steerAngle, 0f, maxDelta);

        rearLeft.steerAngle =
            Mathf.MoveTowards(rearLeft.steerAngle, 0f, maxDelta);

        rearRight.steerAngle =
            Mathf.MoveTowards(rearRight.steerAngle, 0f, maxDelta);
    }

    private bool AllWheelsAssigned()
    {
        return
            frontLeft != null &&
            frontRight != null &&
            middleLeft != null &&
            middleRight != null &&
            rearLeft != null &&
            rearRight != null;
    }

    private void SetMotorAll(float torque)
    {
        frontLeft.motorTorque = torque;
        frontRight.motorTorque = torque;
        middleLeft.motorTorque = torque;
        middleRight.motorTorque = torque;
        rearLeft.motorTorque = torque;
        rearRight.motorTorque = torque;
    }

    private void SetBrakeAll(float torque)
    {
        frontLeft.brakeTorque = torque;
        frontRight.brakeTorque = torque;
        middleLeft.brakeTorque = torque;
        middleRight.brakeTorque = torque;
        rearLeft.brakeTorque = torque;
        rearRight.brakeTorque = torque;
    }

    private void SyncVisibleWheels()
    {
        SyncWheel(frontLeft, frontLeftMesh);
        SyncWheel(frontRight, frontRightMesh);
        SyncWheel(middleLeft, middleLeftMesh);
        SyncWheel(middleRight, middleRightMesh);
        SyncWheel(rearLeft, rearLeftMesh);
        SyncWheel(rearRight, rearRightMesh);
    }

    private static void SyncWheel(WheelCollider wc, Transform mesh)
    {
        if (wc == null || mesh == null)
            return;

        wc.GetWorldPose(out Vector3 p, out Quaternion q);
        mesh.position = p;
        mesh.rotation = q;
    }
}
