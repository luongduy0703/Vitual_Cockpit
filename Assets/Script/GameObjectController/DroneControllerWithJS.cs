using System.Collections;
using UnityEngine;

public class DroneControllerWithJS : MonoBehaviour
{
    [Header("Joystick Pack References")]
    public Joystick leftJoystick;   // Pitch/Roll
    public Joystick rightJoystick;  // Throttle/Yaw

    [Header("Drone Settings")]
    public float maxPitchRollSpeed = 2f;
    public float maxVerticalSpeed = 2f;
    public float maxYawSpeed = 1f;

    [SerializeField] public Telemetry telemetry;
    [SerializeField] public Communication communication;

    // Smooth speeding variables
    [Header("Smooth Speed")] public float smoothSpeed = 2.5f;
    private float smoothVelX = 0f;
    private float smoothVelY = 0f;
    private float smoothVelZ = 0f;
    private float smoothVelYawRate = 0f;

    private Vector3 currentVelocity = Vector3.zero;
    private float currentYaw = 0f;

    private void Start()
    {
        // Ensure communication and telemetry are assigned
        if (communication == null)
        {
            communication = GetComponent<Communication>();
            if (communication == null)
            {
                Debug.LogError("Communication component not found.");
                enabled = false;
                return;
            }
        }

        if (telemetry == null)
        {
            var commObj = GameObject.FindGameObjectWithTag("Communication");
            if (commObj != null)
            {
                var telemetryTransform = commObj.transform.Find("Telemetry");
                if (telemetryTransform != null)
                    telemetry = telemetryTransform.GetComponent<Telemetry>();
            }
            if (telemetry == null)
            {
                Debug.LogError("Telemetry component not found.");
                enabled = false;
                return;
            }
        }
    }

    private void Update()
    {
        // Target velocities based on joystick input
        float targetVelX = leftJoystick != null ? leftJoystick.Vertical : 0f;
        float targetVelY = leftJoystick != null ? leftJoystick.Horizontal * -1f : 0f;
        float targetVelZ = rightJoystick != null ? rightJoystick.Vertical : 0f;
        float targetVelYawRate = rightJoystick != null ? rightJoystick.Horizontal * -1f : 0f;

        // Smoothly interpolate current velocities towards target velocities
        smoothVelX = Mathf.MoveTowards(smoothVelX, targetVelX, smoothSpeed * Time.deltaTime);
        smoothVelY = Mathf.MoveTowards(smoothVelY, targetVelY, smoothSpeed * Time.deltaTime);
        smoothVelZ = Mathf.MoveTowards(smoothVelZ, targetVelZ, smoothSpeed * Time.deltaTime);
        smoothVelYawRate = Mathf.MoveTowards(smoothVelYawRate, targetVelYawRate, smoothSpeed * Time.deltaTime);

        //telemetry.VelX = Mathf.Clamp(smoothVelX, -0.1f, 0.1f);
        telemetry.VelX = targetVelX * 0.2f;
        telemetry.VelY = 0f;
        telemetry.VelZ = 0.5f;
        telemetry.VelYawRate = targetVelYawRate;
        telemetry.VelFrame = 1;

        // Apply movement logic if needed
        //ApplyPitchRoll(leftJoystick != null ? leftJoystick.Vertical : 0f,
        //               leftJoystick != null ? leftJoystick.Horizontal * -1f : 0f);

        //ApplyThrottleYaw(rightJoystick != null ? rightJoystick.Vertical : 0f,
        //                 rightJoystick != null ? rightJoystick.Horizontal * -1f : 0f);

        // Send updated telemetry data
        //ApplyPitchRoll(smoothVelZ, smoothVelX);
        //ApplyThrottleYaw(smoothVelZ, smoothVelYawRate);
    }

    private void ApplyPitchRoll(float pitch, float roll)
    {
        // Calculate velocity based on joystick input
        currentVelocity.x = roll * maxPitchRollSpeed;
        currentVelocity.z = pitch * maxPitchRollSpeed;
    }

    private void ApplyThrottleYaw(float throttle, float yaw)
    {
        currentVelocity.y = throttle * maxVerticalSpeed;
        currentYaw = yaw * maxYawSpeed;
    }
}
