using UnityEngine;
using ViveHandTracking;
using System;

public class AtitudeController : MonoBehaviour
{
    [Header("Lever Components")]
    public Transform leverHandle;
    public Transform leverBase;
    public Transform leverPivot;

    [Header("Movement Settings")]
    public float maxForwardAngle = 30f;
    public float maxBackwardAngle = 30f;
    public float rotationSpeed = 5f;
    public float returnSpeed = 8f;

    [Header("Grab Settings")]
    public float grabDistanceThreshold = 0.3f;
    public float releaseDistanceThreshold = 0.4f;

    [Header("Event Settings")]
    public float onThreshold = 0.7f;
    public float offThreshold = 0.3f;

    [Header("Debug Settings")]
    public bool enableDebug = true;
    public float debugUpdateInterval = 1f;

    public float CurrentValue { get; private set; }
    public bool IsOn { get; private set; }
    public bool IsGrabbing => isGrabbing;
    public LeverDirection CurrentDirection { get; private set; }

    private Quaternion initialHandleRotation;
    private Quaternion targetHandleRotation;
    private bool isGrabbing = false;
    private Vector3 grabOffset;
    private GestureResult grabbingHand = null;
    private float debugTimer = 0f;
    private LeverDirection previousDirection;

    // Debug info
    private float lastDistanceToLever = 0f;
    private Vector3 lastHandPosition = Vector3.zero;
    private string lastHandState = "None";

    [SerializeField]
    private Telemetry telemetry;
    [SerializeField]
    private Communication communication;
    [SerializeField]
    private FistControlledJoystick fistJoystick;

    private int state = 0;
    private bool autoMode = false;

    public enum LeverDirection
    {
        Neutral,
        Forward,
        Backward
    }

    void Start()
    {
        if (leverHandle == null)
        {
            Debug.LogError("LeverHandle is not assigned!");
            return;
        }

        if (leverPivot == null)
        {
            leverPivot = leverBase;
        }

        initialHandleRotation = leverHandle.rotation;
        targetHandleRotation = initialHandleRotation;
        CurrentDirection = LeverDirection.Neutral;
        previousDirection = LeverDirection.Neutral;

        if (communication == null)
        {
            // Try to find Communication in parent
            communication = GetComponent<Communication>();
            if (communication == null)
            {
                Debug.LogError("Communication component not found in hierarchy.");
                enabled = false;
                return;
            }
        }

        var communicationObj = GameObject.FindGameObjectWithTag("Communication");
        if (communicationObj == null)
        {
            Debug.LogError("Communication GameObject not found. Please check the tag.");
            enabled = false;
            return;
        }

        // Find Telemetry as a child of Communication
        var telemetryTransform = communicationObj.transform.Find("Telemetry");
        if (telemetryTransform == null)
        {
            Debug.LogError("Telemetry child not found under Communication GameObject.");
            enabled = false;
            return;
        }

        telemetry = telemetryTransform.GetComponent<Telemetry>();
        if (telemetry == null)
        {
            Debug.LogError("Telemetry component not found on Telemetry GameObject.");
            enabled = false;
            return;
        }

        if (fistJoystick == null)
            fistJoystick = FindObjectOfType<FistControlledJoystick>();

        telemetry.VelX = 0f;
        telemetry.VelY = 0f;
        telemetry.VelZ = 0.0f;
        telemetry.VelYawRate = 0f;
        telemetry.VelFrame = 1;
        
        telemetry.BaseSV = 90f;
        telemetry.ShoulderSV = 30f;
        telemetry.ElbowSV = 0f;
        telemetry.WristSV = 180f;

        telemetry.TeleVxControl = 0;      // Roll: [-1000, 1000]
        telemetry.TeleVyControl = 0;      // Pitch: [-1000, 1000]
        telemetry.TeleVzControl = 500;    // Throttle: [-1000, 1000]
        telemetry.TeleYawRateControl = 0; // Yaw: [-1000, 1000]

        //Debug.Log($"Max tilt angle: forward {maxForwardAngle}°, backward {maxBackwardAngle}°");
    }

    void Update()
    {
        CheckLeftHandGrab();
        UpdateHandleMovement();
        CalculateLeverValue();
        CheckLeverDirection();
        setControlAtitude();

        if (enableDebug && Time.frameCount % 60 == 0)
        {
            Debug.Log($"Angle: {leverHandle.localEulerAngles.x:F1}°, Value: {CurrentValue:F2}, Direction: {CurrentDirection}");
        }

        //if (enableDebug)
        //{
        //    UpdateDebugInfo();
        //}
    }

    private void CheckLeftHandGrab()
    {
        bool leftHandReady = IsLeftHandReady();

        if (leftHandReady && !isGrabbing)
        {
            StartGrab(GestureProvider.LeftHand);
        }
        else if (!leftHandReady && isGrabbing)
        {
            StopGrab();
        }
        else if (isGrabbing && grabbingHand != null)
        {
            if (!IsLeftHandReady())
            {
                StopGrab();
            }
            else
            {
                UpdateGrabPosition(GestureProvider.LeftHand);
            }
        }
    }

    private bool IsLeftHandReady()
    {
        return IsHandReady(GestureProvider.LeftHand);
    }

    private bool IsHandReady(GestureResult hand)
    {
        if (hand == null)
        {
            lastHandState = "Hand is null";
            return false;
        }

        bool isFist = hand.gesture == GestureType.Fist;
        bool isNear = IsHandNearLever(hand.position);

        lastHandState = $"Gesture: {hand.gesture}, Near: {isNear}";
        lastHandPosition = hand.position;

        return isFist && isNear;
    }

    private bool IsHandNearLever(Vector3 handPosition)
    {
        if (leverHandle == null) return false;

        float distanceToLever = Vector3.Distance(handPosition, leverHandle.position);
        lastDistanceToLever = distanceToLever;

        return distanceToLever <= grabDistanceThreshold;
    }

    private void StartGrab(GestureResult hand)
    {
        if (hand == null) return;

        isGrabbing = true;
        grabbingHand = hand;
        grabOffset = leverPivot.position - hand.position;
    }

    private void StopGrab()
    {
        isGrabbing = false;
        grabbingHand = null;
        targetHandleRotation = initialHandleRotation;
        //Debug.Log("[Left Hand]: Stop grab]");
    }

    private void UpdateGrabPosition(GestureResult hand)
    {
        if (hand == null) return;

        Vector3 targetHandPos = hand.position + grabOffset;
        Vector3 directionFromPivot = targetHandPos - leverPivot.position;
        directionFromPivot.y = 0;

        if (directionFromPivot.magnitude > 0.01f)
        {
            float distance = directionFromPivot.magnitude;
            float maxDistance = Mathf.Max(maxForwardAngle, maxBackwardAngle) * 0.01f;
            float angle = Mathf.Clamp(distance / maxDistance, 0f, 1f) * Mathf.Max(maxForwardAngle, maxBackwardAngle);

            float dotProduct = Vector3.Dot(directionFromPivot.normalized, leverPivot.forward);

            if (dotProduct > 0)
            {
                targetHandleRotation = initialHandleRotation * Quaternion.Euler(-angle, 0, 0);
            }
            else
            {
                targetHandleRotation = initialHandleRotation * Quaternion.Euler(angle, 0, 0);
            }
        }
        else
        {
            targetHandleRotation = initialHandleRotation;
        }

        float currentDistance = Vector3.Distance(hand.position, leverHandle.position);
        if (currentDistance > releaseDistanceThreshold)
        {
            //Debug.Log($"Distance over threshold: {currentDistance:F2}m > {releaseDistanceThreshold:F2}m");
            StopGrab();
        }
    }

    private void UpdateHandleMovement()
    {
        if (isGrabbing)
        {
            leverHandle.rotation = Quaternion.Lerp(
                leverHandle.rotation,
                targetHandleRotation,
                Time.deltaTime * rotationSpeed
            );
        }
        else
        {
            leverHandle.rotation = Quaternion.Lerp(
                leverHandle.rotation,
                initialHandleRotation,
                Time.deltaTime * returnSpeed
            );
        }

        CalculateLeverValue();
        CheckLeverDirection();
    }

    private void CalculateLeverValue()
    {
        Vector3 currentEuler = leverHandle.localEulerAngles;
        float currentAngle = currentEuler.x;

        if (currentAngle > 180f) currentAngle -= 360f;

        float newValue = 0f;

        if (currentAngle < 0f)
        {
            // Tilt forward (angle -) Forward
            newValue = Mathf.Clamp(currentAngle / -maxForwardAngle, 0f, 1f);
        }
        else if (currentAngle > 0f)
        {
            // Tilt back (angle +) Backward
            newValue = Mathf.Clamp(currentAngle / -maxBackwardAngle, -1f, 0f);
        }
        // else currentAngle = 0f newValue = 0f (Neutral)

        if (Mathf.Abs(newValue - CurrentValue) > 0.01f)
        {
            CurrentValue = newValue;
        }
    }

    private void CheckLeverDirection()
    {
        LeverDirection newDirection;

        if (Mathf.Abs(CurrentValue) < 0.3f)
        {
            newDirection = LeverDirection.Neutral;
        }
        else if (CurrentValue >= 0.3f)
        {
            newDirection = LeverDirection.Backward;
        }
        else
        {
            newDirection = LeverDirection.Forward;
        }

        if (newDirection != CurrentDirection)
        {
            CurrentDirection = newDirection;
        }
    }

    public void setControlAtitude()
    {

        if (CurrentDirection == LeverDirection.Forward) //&& Mathf.Abs(CurrentValue) > onThreshold
        {
            telemetry.VelZ = 0.2f;
            if (telemetry.TeleVzControl < 700)
            {
                telemetry.TeleVzControl += 2;
                telemetry.TeleVyControl = 0;
                telemetry.TeleVxControl = 0;
                telemetry.TeleYawRateControl = 0;
            }
            else
            {
                telemetry.TeleVzControl = 700;
                telemetry.TeleVyControl = 0;
                telemetry.TeleVxControl = 0;
                telemetry.TeleYawRateControl = 0;
            }
        }
        else if (CurrentDirection == LeverDirection.Backward) //&& Mathf.Abs(CurrentValue) > onThreshold
        {
            telemetry.VelZ = -0.2f;
            if (telemetry.TeleVzControl > -700)
            {
                telemetry.TeleVzControl -= 2;
                telemetry.TeleVyControl = 0;
                telemetry.TeleVxControl = 0;
                telemetry.TeleYawRateControl = 0;
            }
            else
            {
                telemetry.TeleVzControl = -700;
                telemetry.TeleVyControl = 0;
                telemetry.TeleVxControl = 0;
                telemetry.TeleYawRateControl = 0;
            }
        }
        else if (CurrentDirection == LeverDirection.Neutral)
        {
            telemetry.VelZ = 0.0f;

            if (fistJoystick != null && fistJoystick.enabled)
            {
                if (fistJoystick.CurrentForward == 0 &&
                    fistJoystick.CurrentBackward == 0 &&
                    fistJoystick.CurrentRight == 0 &&
                    fistJoystick.CurrentLeft == 0)
                {
                    telemetry.TeleVxControl = 0;
                    telemetry.TeleVyControl = 0;

                    telemetry.VelX = 0f;
                    telemetry.VelY = 0f;
                }

                //if (!isGrabbing) return;

                if (fistJoystick.IsMovingRight())  // Right
                {
                    if (telemetry.TeleVyControl < 700)
                    {
                        telemetry.TeleVxControl = 0;
                        telemetry.TeleVyControl += 2;
                    }
                    else
                    {
                        telemetry.TeleVxControl = 0;
                        telemetry.TeleVyControl = 700;
                    }

                    telemetry.VelX = 0f; 
                    telemetry.VelY = -0.7f;
                    Debug.Log($"[Control] Right");
                }


                if (fistJoystick.IsMovingLeft()) // Left
                {
                    if (telemetry.TeleVyControl > -700)
                    {
                        telemetry.TeleVxControl = 0;
                        telemetry.TeleVyControl -= 2;
                    }
                    else
                    {
                        telemetry.TeleVxControl = 0;
                        telemetry.TeleVyControl = -700;
                    }

                    //offboard
                    telemetry.VelX = 0f;
                    telemetry.VelY = 0.7f;
                    Debug.Log($"[Control] Left");
                }


                if (fistJoystick.IsMovingForward()) // Forward
                {
                    if (telemetry.TeleVxControl < 700)
                    {
                        telemetry.TeleVxControl += 2;
                        telemetry.TeleVyControl = 0;
                    }
                    else
                    {
                        telemetry.TeleVxControl = 700;
                        telemetry.TeleVyControl = 0;
                    }

                    // offboard
                    telemetry.VelX = 0.7f;
                    telemetry.VelY = 0f;
                    Debug.Log($"[Control] Forward");
                }


                if (fistJoystick.IsMovingBackward()) // Backward
                {
                    if (telemetry.TeleVxControl > -700)
                    {
                        telemetry.TeleVxControl -= 2;
                        telemetry.TeleVyControl = 0;
                    }
                    else
                    {
                        telemetry.TeleVxControl = -700;
                        telemetry.TeleVyControl = 0;
                    }

                    // offboard
                    telemetry.VelX = -0.7f;
                    telemetry.VelY = 0f;
                    Debug.Log($"[Control] Backward");
                }

                telemetry.TeleVzControl = 500;
                telemetry.TeleYawRateControl = 0;

                // offboard
                telemetry.VelZ = 0f;
                telemetry.VelYawRate = 0f;
            }
            else
            {
                telemetry.TeleVxControl = 0;
                telemetry.TeleVyControl = 0;
                telemetry.TeleVzControl = 500;
                telemetry.TeleYawRateControl = 0;

                telemetry.VelX = 0f;
                telemetry.VelY = 0f;
                telemetry.VelZ = 0f;
                telemetry.VelYawRate = 0f;
            }

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
                    communication.HandleOffboardControl();
                }
                else
                {
                    communication.SendVelocityControl();
                }

            }
        }
    }

    //private void UpdateDebugInfo()
    //{
    //    debugTimer += Time.deltaTime;

    //    if (debugTimer >= debugUpdateInterval)
    //    {
    //        debugTimer = 0f;

    //        if (GestureProvider.LeftHand != null)
    //        {
    //            Debug.Log($"\n=== DEBUG INFO ===");
    //            Debug.Log($"Left Hand: {GestureProvider.LeftHand.gesture}");
    //            Debug.Log($"Threshhold grab: {grabDistanceThreshold:F2}m");
    //            Debug.Log($"Grab state: {isGrabbing}");
    //            Debug.Log($"Direction: {CurrentDirection}");
    //            Debug.Log($"==================\n");
    //        }
    //        else
    //        {
    //            Debug.Log("Left hand no detected.");
    //        }
    //    }
    //}

    public Vector3 GetHandPosition()
    {
        return GestureProvider.LeftHand != null ? GestureProvider.LeftHand.position : Vector3.zero;
    }

    public float GetDistanceToLever()
    {
        return lastDistanceToLever;
    }

    void OnDestroy()
    {

    }
}