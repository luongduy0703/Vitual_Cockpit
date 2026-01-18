using UnityEngine;
using UnityEngine.Events;
using ViveHandTracking;

public class SteeringWheelController : MonoBehaviour
{
    [Header("Steering Wheel Components")]
    public Transform steeringWheel;
    public Transform wheelPivot;

    [Header("References")]
    [SerializeField] private Telemetry telemetry;
    [SerializeField] private Communication communication;
    [SerializeField] private FistControlledJoystick fistJoystick;
    [SerializeField] private AtitudeController atitudeController;

    public enum WheelAxisLocal { Forward, Right, Up }

    [Header("Steering Settings")]
    public WheelAxisLocal rotationAxisLocal = WheelAxisLocal.Forward;
    public float maxWheelAngle = 180f;
    public float steeringSensitivity = 3.0f;
    public float maxYawRate = 700f;
    public float wheelReturnSpeed = 180f;

    [Header("Grab Settings")]
    public bool requireTwoHands = true;
    public bool requireFistGesture = true;
    public bool requireLeftNear = true;
    public bool requireRightNear = false;
    public float grabDistanceThreshold = 0.35f;
    public float releaseDistanceThreshold = 0.5f;

    [Header("Mode Toggle")]
    public float okToggleCooldown = 0.8f;

    [Header("State Detection (Left / Center / Right)")]
    public float centerExitAngle = 5f;
    public float centerEnterAngle = 3f;
    public enum WheelTurnState { Center, Left, Right }
    public WheelTurnState CurrentTurnState { get; private set; } = WheelTurnState.Center;
    public UnityEvent OnTurnLeft;
    public UnityEvent OnTurnRight;
    public UnityEvent OnCenter;

    [Header("Debug")]
    public bool enableDebug = true;
    public Color gizmoColor = new Color(0.1f, 0.8f, 1f, 0.35f);

    // State
    private bool isSteeringMode = false;
    private bool isGrabbing = false;

    private float currentWheelAngle;
    private Quaternion initialWheelLocalRotation;

    private Vector3 prevLeftVecPlane;
    private Vector3 prevRightVecPlane;
    private bool prevVecInitialized = false;

    private bool lastBothOk = false;
    private float okCooldownTimer = 0f;

    private void OnValidate()
    {
        if (wheelPivot == null) wheelPivot = steeringWheel;
        if (centerEnterAngle > centerExitAngle) centerEnterAngle = centerExitAngle;
    }

    private void Awake()
    {
        if (steeringWheel != null)
            initialWheelLocalRotation = steeringWheel.localRotation;

        if (wheelPivot == null)
            wheelPivot = steeringWheel; // fallback
    }

    void Start()
    {
        SetSteeringMode(false, force: true);

        if (communication == null)
        {
            communication = GetComponent<Communication>();
            if (communication == null)
            {
                Debug.LogError("Communication component not found in hierarchy.");
                return;
            }
        }

        var communicationObj = GameObject.FindGameObjectWithTag("Communication");
        if (communicationObj == null)
        {
            Debug.LogError("Communication GameObject not found. Please check the tag.");
            return;
        }

        var telemetryTransform = communicationObj.transform.Find("Telemetry");
        if (telemetryTransform == null)
        {
            Debug.LogError("Telemetry child not found under Communication GameObject.");
            return;
        }

        telemetry = telemetryTransform.GetComponent<Telemetry>();
        if (telemetry == null)
        {
            Debug.LogError("Telemetry component not found on Telemetry GameObject.");
            return;
        }
    }

    void Update()
    {

        if (steeringWheel == null || wheelPivot == null) return;

        var left = GestureProvider.LeftHand;
        var right = GestureProvider.RightHand;

        HandleOkToggle(left, right);

        if (!isSteeringMode)
            return;

        if (!isGrabbing)
        {
            if (ShouldStartGrab(left, right))
            {
                StartGrab();
            }
            else
            {
                ReturnWheelToCenter();
                ApplyYawFromWheelAngle();
                UpdateTurnState();
            }
        }
        else
        {
            if (!ShouldKeepGrabbing(left, right))
            {
                StopGrab();
                return;
            }

            UpdateWheelFromTwoHands(left, right);
            ApplyWheelVisual();
            ApplyYawFromWheelAngle();
            UpdateTurnState();
        }
        setControlWheel();
    }

    private void HandleOkToggle(GestureResult left, GestureResult right)
    {
        if (okCooldownTimer > 0f)
            okCooldownTimer -= Time.deltaTime;

        bool bothOk = left != null && right != null &&
                      left.gesture == GestureType.OK &&
                      right.gesture == GestureType.OK;

        if (bothOk && !lastBothOk && okCooldownTimer <= 0f)
        {
            if (!isSteeringMode)
            {
                setControlWheel();
                //telemetry.MissionTrainingEnable = !telemetry.MissionTrainingEnable;
            }

            SetSteeringMode(!isSteeringMode);
            okCooldownTimer = okToggleCooldown;
        }

        lastBothOk = bothOk;
    }

    private void SetSteeringMode(bool enable, bool force = false)
    {
        if (!force && isSteeringMode == enable) return;

        isSteeringMode = enable;

        if (steeringWheel != null)
            steeringWheel.gameObject.SetActive(isSteeringMode);
        if (fistJoystick != null)
            fistJoystick.gameObject.SetActive(!isSteeringMode);
        if (atitudeController != null)
            atitudeController.gameObject.SetActive(!isSteeringMode);

        telemetry.MissionTrainingEnable = isSteeringMode;

        isGrabbing = false;
        prevVecInitialized = false;
        currentWheelAngle = 0f;
        ApplyWheelVisual();
        SetYaw(0);
        SetTurnState(WheelTurnState.Center); // reset state

        if (enableDebug)
            Debug.Log(isSteeringMode ? "Steering mode ON" : "Steering mode OFF (Attitude/Joystick)");
    }

    private bool GestureOK(GestureResult hand)
    {
        if (!requireFistGesture) return true;
        return hand != null && hand.gesture == GestureType.Fist;
    }

    private bool IsNear(Vector3 handWorldPos, float threshold)
    {
        float dist = Vector3.Distance(handWorldPos, wheelPivot.position);
        return dist <= threshold;
    }

    private bool ShouldStartGrab(GestureResult left, GestureResult right)
    {
        if (left == null || right == null) return false;
        if (!GestureOK(left) || !GestureOK(right)) return false;

        bool leftNear = !requireLeftNear || IsNear(left.position, grabDistanceThreshold);
        bool rightNear = !requireRightNear || IsNear(right.position, grabDistanceThreshold);

        if (enableDebug) Debug.Log($"StartGrab: leftNear={leftNear}, rightNear={rightNear}");

        return leftNear && rightNear;
    }

    private bool ShouldKeepGrabbing(GestureResult left, GestureResult right)
    {
        if (left == null || right == null) return false;
        if (!GestureOK(left) || !GestureOK(right)) return false;

        bool leftClose = !requireLeftNear || IsNear(left.position, releaseDistanceThreshold);
        bool rightClose = !requireRightNear || IsNear(right.position, releaseDistanceThreshold);

        return leftClose && rightClose;
    }

    private void StartGrab()
    {
        isGrabbing = true;
        prevVecInitialized = false;
        if (enableDebug) Debug.Log("Start grabbing steering wheel");
    }

    private void StopGrab()
    {
        isGrabbing = false;
        prevVecInitialized = false;
        if (enableDebug) Debug.Log("Stop grabbing steering wheel");
    }

    private static Vector3 LocalAxisVector(WheelAxisLocal axis)
    {
        switch (axis)
        {
            case WheelAxisLocal.Right: return Vector3.right;
            case WheelAxisLocal.Up: return Vector3.up;
            default: return Vector3.forward;
        }
    }

    private Vector3 AxisWorld()
    {
        Vector3 localAxis = LocalAxisVector(rotationAxisLocal);
        return wheelPivot != null ? wheelPivot.TransformDirection(localAxis) : localAxis;
    }

    private static Vector3 ProjectOnPlaneNormalized(Vector3 v, Vector3 planeNormal)
    {
        Vector3 p = Vector3.ProjectOnPlane(v, planeNormal);
        return p.sqrMagnitude > 1e-6f ? p.normalized : Vector3.zero;
    }

    private static float SignedAngleOnPlane(Vector3 from, Vector3 to, Vector3 planeNormal)
    {
        return Vector3.SignedAngle(from, to, planeNormal);
    }

    private void UpdateWheelFromTwoHands(GestureResult left, GestureResult right)
    {
        Vector3 axis = AxisWorld();
        Vector3 leftVec = ProjectOnPlaneNormalized(left.position - wheelPivot.position, axis);
        Vector3 rightVec = ProjectOnPlaneNormalized(right.position - wheelPivot.position, axis);

        if (!prevVecInitialized)
        {
            prevLeftVecPlane = leftVec;
            prevRightVecPlane = rightVec;
            prevVecInitialized = true;
            return;
        }

        float deltaL = SignedAngleOnPlane(prevLeftVecPlane, leftVec, axis);
        float deltaR = SignedAngleOnPlane(prevRightVecPlane, rightVec, axis);
        float delta = (deltaL + deltaR) * 0.5f;

        currentWheelAngle = Mathf.Clamp(currentWheelAngle + delta, -maxWheelAngle, maxWheelAngle);

        prevLeftVecPlane = leftVec;
        prevRightVecPlane = rightVec;

        if (enableDebug) Debug.Log($"Wheel: {delta:F2}°, angle: {currentWheelAngle:F1}°");
    }

    private void ReturnWheelToCenter()
    {
        if (Mathf.Approximately(currentWheelAngle, 0f)) return;

        float step = wheelReturnSpeed * Time.deltaTime;
        currentWheelAngle = Mathf.MoveTowards(currentWheelAngle, 0f, step);
        ApplyWheelVisual();
    }

    private void ApplyWheelVisual()
    {
        if (steeringWheel == null) return;
        Vector3 axisLocal = LocalAxisVector(rotationAxisLocal);
        steeringWheel.localRotation = initialWheelLocalRotation * Quaternion.AngleAxis(currentWheelAngle, axisLocal);
    }

    private void ApplyYawFromWheelAngle()
    {
        float yawRate = currentWheelAngle * steeringSensitivity;
        yawRate = Mathf.Clamp(yawRate, -maxYawRate, maxYawRate);
        SetYaw(Mathf.RoundToInt(yawRate));
    }

    private void SetYaw(int yawRate)
    {
        if (telemetry == null) return;
        //telemetry.TeleYawRateControl = yawRate;
    }

    private void UpdateTurnState()
    {
        float ang = currentWheelAngle;

        switch (CurrentTurnState)
        {
            case WheelTurnState.Center:
                if (ang > centerExitAngle)
                    SetTurnState(WheelTurnState.Right);
                else if (ang < -centerExitAngle)
                    SetTurnState(WheelTurnState.Left);
                break;

            case WheelTurnState.Right:
                if (Mathf.Abs(ang) < centerEnterAngle)
                    SetTurnState(WheelTurnState.Center);
                else if (ang < -centerExitAngle)
                    SetTurnState(WheelTurnState.Left);
                break;

            case WheelTurnState.Left:
                if (Mathf.Abs(ang) < centerEnterAngle)
                    SetTurnState(WheelTurnState.Center);
                else if (ang > centerExitAngle)
                    SetTurnState(WheelTurnState.Right);
                break;
        }
    }

    private void SetTurnState(WheelTurnState newState)
    {
        if (CurrentTurnState == newState) return;

        CurrentTurnState = newState;

        if (enableDebug) Debug.Log($"Turn State => {newState}");

        switch (newState)
        {
            case WheelTurnState.Left:
                OnTurnLeft?.Invoke();
                break;
            case WheelTurnState.Right:
                OnTurnRight?.Invoke();
                break;
            case WheelTurnState.Center:
                OnCenter?.Invoke();
                break;
        }
    }

    private void setControlWheel()
    {
        if (CurrentTurnState == WheelTurnState.Center)
        {
            telemetry.TeleYawRateControl = 0;
            telemetry.VelYawRate = 0.0f;

            if (telemetry.TeleSwitchMode == 0) // Manual control
            {
                communication.HandleHoldControl();
            }
            else if (telemetry.TeleSwitchMode == 1) // Altitude control
            {
                communication.HandleHoldAltctlControl();
            }
            else if (telemetry.TeleSwitchMode == 2) // Position control POSCTL
            {
                communication.HandleHoldPosctlControl();
            }
            else if (telemetry.TeleSwitchMode == 3) // Offboard control
            {
                if (telemetry.VelActive == false)
                {
                    communication.HandleHoldOffboardControl();
                }
                else
                {
                    if (telemetry.MissionTrainingEnable)
                    {
                        //SendTrainingControlMessage();
                        //telemetry.VelYawRate = 0f;
                    }
                    else
                    {
                        communication.SendVelocityControl();
                    }
                    
                }

            }
        }
        else if (CurrentTurnState == WheelTurnState.Left)
        {
            // manual control
            if (telemetry.TeleYawRateControl > -700)
                telemetry.TeleYawRateControl -= 2;
            else
                telemetry.TeleYawRateControl = -700;

            // velocity
            if (telemetry.VelYawRate < 0.5f)
                telemetry.VelYawRate += 0.01f;
            else
                telemetry.VelYawRate = 0.5f;

        }
        else if (CurrentTurnState == WheelTurnState.Right)
        {
            if (telemetry.TeleYawRateControl < 700)
                telemetry.TeleYawRateControl += 2;
            else
                telemetry.TeleYawRateControl = 700;

            // velocity control
            if (telemetry.VelYawRate > -0.5f)
                telemetry.VelYawRate -= 0.01f;
            else
                telemetry.VelYawRate = -0.5f;
        }

        telemetry.TeleVxControl = 0;
        telemetry.TeleVyControl = 0;
        telemetry.TeleVzControl = 500;
        SendTrainingControlMessage();
    }

    private void SendTrainingControlMessage() 
    {
        telemetry.VelX = 0f;
        telemetry.VelY = 0f;
        telemetry.VelZ = 0f;
        telemetry.VelFrame = 1;
    }

    private void OnDrawGizmos()
    {
        if (!enableDebug || wheelPivot == null) return;

        Gizmos.color = gizmoColor;
        Gizmos.DrawWireSphere(wheelPivot.position, grabDistanceThreshold);
        Gizmos.DrawWireSphere(wheelPivot.position, releaseDistanceThreshold);

        Vector3 axis = wheelPivot != null
            ? wheelPivot.TransformDirection(LocalAxisVector(rotationAxisLocal))
            : LocalAxisVector(rotationAxisLocal);
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(wheelPivot.position, axis * 0.3f);
        Gizmos.DrawRay(wheelPivot.position, -axis * 0.3f);
    }


}