using UnityEngine;
using ViveHandTracking;
using System;

public class FistControlledJoystick : MonoBehaviour
{
    [Header("Joystick Components")]
    public Transform handle;
    public Transform basePlate;

    [Header("Movement Settings")]
    public float maxTiltAngle = 30f;
    public float maxHandleDistance = 0.2f;
    public float returnSpeed = 8f;
    public float smoothFactor = 5f;

    [Header("Tilt Settings")]
    public float tiltScale = 1f;

    [Header("Direction Thresholds")]
    [Range(0.1f, 0.9f)]
    public float directionThreshold = 0.5f;
    public float deadZone = 0.1f;

    [Header("Grab Settings")]
    public float grabDistanceThreshold = 0.3f;
    public float releaseDistanceThreshold = 0.4f;

    [Header("Event System")]
    public bool enableEvents = true;

    public float CurrentForward { get; private set; }
    public float CurrentBackward { get; private set; }
    public float CurrentRight { get; private set; }
    public float CurrentLeft { get; private set; }

    private Vector3 initialHandleLocalPosition;
    private Quaternion initialHandleLocalRotation;
    private Vector3 targetHandlePosition;
    private Quaternion targetHandleRotation;

    private bool isGrabbing = false;
    private Vector3 grabOffset;
    private GestureResult grabbingHand = null;

    [SerializeField]
    private GameObject _telemetry;
    private Telemetry telemetry;

    private float lastForward;
    private float lastBackward;
    private float lastRight;
    private float lastLeft;

    [Header("Control Settings")]
    public float movementSpeed = 2f;
    public bool normalizeDiagonal = true;
    public float diagonalSpeedMultiplier = 0.7f;

    void Start()
    {
        if (handle == null)
        {
            Debug.LogError("Handle is not assigned!");
            return;
        }

        initialHandleLocalPosition = handle.localPosition;
        initialHandleLocalRotation = handle.localRotation;

        targetHandlePosition = initialHandleLocalPosition;
        targetHandleRotation = initialHandleLocalRotation;

        _telemetry = GameObject.FindGameObjectWithTag("Telemetry");
        telemetry = _telemetry.GetComponent<Telemetry>();
    }

    void Update()
    {
        CheckFistGrab();
        UpdateHandleMovement();
        CalculateDirectionalInput();

        //setControlJoystick();
        //DebugJoystickState();
    }

    private void DebugJoystickState()
    {
        if (Time.frameCount % 60 == 0) // Mỗi 1 giây
        {
            Debug.Log($"=== JOYSTICK DEBUG ===");
            Debug.Log($"IsGrabbing: {isGrabbing}");
            Debug.Log($"Hand Ready: {IsRightHandReady()}");

            if (isGrabbing && grabbingHand != null)
            {
                Debug.Log($"Hand Position: {grabbingHand.position}");
                Debug.Log($"Handle Local Pos: {handle.localPosition}");
                Debug.Log($"Target Handle Pos: {targetHandlePosition}");

                Vector2 direction = new Vector2(
                    handle.localPosition.x / maxHandleDistance,
                    handle.localPosition.z / maxHandleDistance
                );
                Debug.Log($"Normalized Direction: {direction}");

                Debug.Log($"Forward: {CurrentForward:F2}, Backward: {CurrentBackward:F2}");
                Debug.Log($"Right: {CurrentRight:F2}, Left: {CurrentLeft:F2}");
            }
        }
    }

    private void CheckFistGrab()
    {
        bool rightHandReady = IsRightHandReady();

        if (rightHandReady && !isGrabbing)
        {
            StartGrab(GestureProvider.RightHand);
        }
        else if (!rightHandReady && isGrabbing)
        {
            StopGrab();
        }
        else if (isGrabbing && grabbingHand != null)
        {
            if (!IsRightHandReady())
            {
                StopGrab();
            }
            else
            {
                UpdateGrabPosition(GestureProvider.RightHand);
            }
        }
    }

    private bool IsRightHandReady()
    {
        return IsHandReady(GestureProvider.RightHand);
    }

    private bool IsHandReady(GestureResult hand)
    {
        if (hand == null) return false;
        return hand.gesture == GestureType.Fist && IsHandNearHandle(hand.position);
    }

    private bool IsHandNearHandle(Vector3 handPosition)
    {
        if (handle == null) return false;

        float distanceToHandle = Vector3.Distance(handPosition, handle.position);
        bool isNear = distanceToHandle <= grabDistanceThreshold;

        if (isNear && Time.frameCount % 30 == 0)
        {
            Debug.Log($"[Right Hand] near handle: {distanceToHandle:F2}m (Thresh Hold: {grabDistanceThreshold:F2}m)");
        }

        return isNear;
    }

    private void StartGrab(GestureResult hand)
    {
        if (hand == null) return;

        isGrabbing = true;
        grabbingHand = hand;
        grabOffset = handle.position - hand.position;

        ResetAllInputs();

        //Debug.Log("[Right Hand] Start control");
        //Debug.Log($"[Right Hand] Handle position: {handle.position}");
        //Debug.Log($"[Right Hand] Hand position: {hand.position}");
        //Debug.Log($"[Right Hand] Tilt Scale: {tiltScale}x");
    }

    private void StopGrab()
    {
        isGrabbing = false;
        grabbingHand = null;
        targetHandlePosition = initialHandleLocalPosition;
        targetHandleRotation = initialHandleLocalRotation;
        ResetAllInputs();

        //Debug.Log("[Right Hand] Stop Grab");
    }

    private void ResetAllInputs()
    {
        CurrentForward = 0;
        CurrentBackward = 0;
        CurrentRight = 0;
        CurrentLeft = 0;
    }

    private void UpdateGrabPosition(GestureResult hand)
    {
        if (hand == null) return;

        Vector3 currentHandPos = hand.position + grabOffset;
        Vector3 localDirection = basePlate.InverseTransformPoint(currentHandPos) - initialHandleLocalPosition;

        Vector2 horizontalDirection = new Vector2(localDirection.x, localDirection.z);
        horizontalDirection = Vector2.ClampMagnitude(horizontalDirection, maxHandleDistance);

        targetHandlePosition = new Vector3(
            horizontalDirection.x,
            initialHandleLocalPosition.y,
            horizontalDirection.y
        );

        UpdateTargetRotation(horizontalDirection);

        float currentDistance = Vector3.Distance(hand.position, handle.position);
        if (currentDistance > releaseDistanceThreshold)
        {
            StopGrab();
        }
    }

    private void UpdateTargetRotation(Vector2 direction)
    {
        if (direction.magnitude > 0.01f)
        {
            float scaledMaxTiltAngle = maxTiltAngle * tiltScale;
            float tiltX = Mathf.Clamp(-direction.y / maxHandleDistance * scaledMaxTiltAngle, -scaledMaxTiltAngle, scaledMaxTiltAngle);
            float tiltZ = Mathf.Clamp(-direction.x / maxHandleDistance * scaledMaxTiltAngle, -scaledMaxTiltAngle, scaledMaxTiltAngle);
            targetHandleRotation = Quaternion.Euler(tiltX, 0, tiltZ);
        }
        else
        {
            targetHandleRotation = initialHandleLocalRotation;
        }
    }

    private void UpdateHandleMovement()
    {
        handle.localPosition = Vector3.Lerp(handle.localPosition, targetHandlePosition, Time.deltaTime * smoothFactor);
        handle.localRotation = Quaternion.Slerp(handle.localRotation, targetHandleRotation, Time.deltaTime * smoothFactor);
    }

    private void CalculateDirectionalInput()
    {
        if (!isGrabbing) return;

        Vector2 direction = new Vector2(
            handle.localPosition.x / maxHandleDistance,
            handle.localPosition.z / maxHandleDistance
        );

        float magnitude = direction.magnitude;

        if (magnitude < deadZone)
        {
            direction = Vector2.zero;
            magnitude = 0;
        }
        else
        {
            direction = direction.normalized * ((magnitude - deadZone) / (1 - deadZone));
            magnitude = direction.magnitude;
        }

        CalculateForwardInput(direction.y, magnitude);
        CalculateBackwardInput(direction.y, magnitude);
        CalculateRightInput(direction.x, magnitude);
        CalculateLeftInput(direction.x, magnitude);

        if (Time.frameCount % 30 == 0 && magnitude > 0.1f)
        {
            //Debug.Log($"[Right Hand] Input: ({direction.x:F2}, {direction.y:F2})");
            //Debug.Log($"[Right Hand] Tilt Scale: {tiltScale}x - Max Tilt: {maxTiltAngle * tiltScale}°");
        }
    }

    private void CalculateForwardInput(float yInput, float magnitude)
    {
        if (yInput > directionThreshold)
        {
            CurrentForward = Mathf.Clamp01((yInput - directionThreshold) / (1 - directionThreshold)) * magnitude;
        }
        else
        {
            CurrentForward = 0;
        }
    }

    private void CalculateBackwardInput(float yInput, float magnitude)
    {
        if (yInput < -directionThreshold)
        {
            CurrentBackward = Mathf.Clamp01((-yInput - directionThreshold) / (1 - directionThreshold)) * magnitude;
        }
        else
        {
            CurrentBackward = 0;
        }
    }

    private void CalculateRightInput(float xInput, float magnitude)
    {
        if (xInput > directionThreshold)
        {
            CurrentRight = Mathf.Clamp01((xInput - directionThreshold) / (1 - directionThreshold)) * magnitude;
        }
        else
        {
            CurrentRight = 0;
        }
    }

    private void CalculateLeftInput(float xInput, float magnitude)
    {
        if (xInput < -directionThreshold)
        {
            CurrentLeft = Mathf.Clamp01((-xInput - directionThreshold) / (1 - directionThreshold)) * magnitude;
        }
        else
        {
            CurrentLeft = 0;
        }
    }

    private void setControlJoystick()
    {
        if (CurrentForward == 0 &&
            CurrentBackward == 0 &&
            CurrentRight == 0 &&
            CurrentLeft == 0)
        {
            telemetry.TeleVxControl = 0;
            telemetry.TeleVyControl = 0;
            telemetry.TeleVzControl = 500;
            telemetry.TeleYawRateControl = 0;

            telemetry.VelX = 0.0f;
            telemetry.VelY = 0.0f;
            telemetry.VelZ = 0.0f;
            telemetry.VelYawRate = 0.0f;
            telemetry.VelFrame = 1;
        }

        //if (!isGrabbing) return;

        if (IsMovingForward())  // Forward
        {
            if (telemetry.TeleVyControl < 700)
            {
                telemetry.TeleVxControl = 0;
                telemetry.TeleVyControl += 2;
                telemetry.TeleVzControl = 500;
                telemetry.TeleYawRateControl = 0;
            }
            else
            {
                telemetry.TeleVxControl = 0;
                telemetry.TeleVyControl = 700;
                telemetry.TeleVzControl = 500;
                telemetry.TeleYawRateControl = 0;
            }
            telemetry.VelX = 0.8f;
            // Debug.Log($"[Right Hand] Forward");
        }

        if (IsMovingBackward()) // Backward
        {
            if (telemetry.TeleVyControl > -700)
            {
                telemetry.TeleVxControl = 0;
                telemetry.TeleVyControl -= 2;
                telemetry.TeleVzControl = 500;
                telemetry.TeleYawRateControl = 0;
            }
            else
            {
                telemetry.TeleVxControl = 0;
                telemetry.TeleVyControl = -700;
                telemetry.TeleVzControl = 500;
                telemetry.TeleYawRateControl = 0;
            }
            telemetry.VelX = -0.8f;
            Debug.Log($"[Right Hand] Backward");
        }


        if (IsMovingRight()) // Right
        {
            if (telemetry.TeleVxControl < 700)
            {
                telemetry.TeleVxControl += 2;
                telemetry.TeleVyControl = 0;
                telemetry.TeleVzControl = 500;
                telemetry.TeleYawRateControl = 0;
            }
            else
            {
                telemetry.TeleVxControl = 700;
                telemetry.TeleVyControl = 0;
                telemetry.TeleVzControl = 500;
                telemetry.TeleYawRateControl = 0;
            }
            telemetry.VelY = 0.8f;
            Debug.Log($"[Right Hand] Right");
        }

        if (IsMovingLeft()) // Left
        {
            if (telemetry.TeleVxControl > -700)
            {
                telemetry.TeleVxControl -= 2;
                telemetry.TeleVyControl = 0;
                telemetry.TeleVzControl = 500;
                telemetry.TeleYawRateControl = 0;
            }
            else
            {
                telemetry.TeleVxControl = -700;
                telemetry.TeleVyControl = 0;
                telemetry.TeleVzControl = 500;
                telemetry.TeleYawRateControl = 0;
            }
            telemetry.VelY = -0.8f;
            Debug.Log($"[Right Hand] Left");
        }

        if (Time.frameCount % 60 == 0 && (IsMovingForward() || IsMovingBackward() || IsMovingRight() || IsMovingLeft()))
        {
            string directions = "";
            if (IsMovingForward()) directions += "Forward ";
            if (IsMovingBackward()) directions += "Backward ";
            if (IsMovingRight()) directions += "Right ";
            if (IsMovingLeft()) directions += "Left ";
            Debug.Log($"Current active direction: {directions}");
        }
    }

    public bool IsMovingForward() => CurrentForward > 0;
    public bool IsMovingBackward() => CurrentBackward > 0;
    public bool IsMovingRight() => CurrentLeft > 0;
    public bool IsMovingLeft() => CurrentRight > 0;

    public Vector2 GetCombinedInput()
    {
        return new Vector2(
            CurrentRight > 0 ? CurrentRight : -CurrentLeft,
            CurrentForward > 0 ? CurrentForward : -CurrentBackward
        );
    }
}