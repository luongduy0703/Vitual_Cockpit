using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading.Tasks;

public class Telemetry : MonoBehaviour
{
    [SerializeField]
    private Communication communication;

    public enum Mode
    {
        MANUAL = 0,
        ALTCTL = 1,
        POSCTL = 2,
        OFFBOARD = 3,
        AUTO_LAND = 4,
        AUTO_MISSION = 5,
        AUTO_LOITER = 6,
        AUTO_RTL = 7,
        ACRO = 8,
        RATTITUDE = 9
    };

    private bool _connected = false;
    private bool _arm = false;
    private Mode _CurMode;
    private float _battery;
    private double _lat;
    private double _lon;
    private float _alt;
    private float _vx;
    private float _vy;
    private float _vz;

    private int _vxControl;
    private int _vyControl;
    private int _vzControl;
    private int _yawRateControl;

    // Position for Offboard
    private float posx;
    private float posy;
    private float posz;
    private float posyaw;
    private byte posframe;

    // Position for Point Pen
    private float _positionPenX;
    private float _positionPenY;
    private float _positionPenZ;

    // Velocity for Offboard
    private float _velx;
    private float _vely;
    private float _velz;
    private float _velyawrate;
    private byte _velframe;
    private bool _velActive = false;

    // Orientation
    private float _roll;
    private float _pitch;
    private float _yaw;

    // Distance sensors
    private float _shortDistance;
    private float _longDistance;

    // RC signal
    private ushort rc1, rc2, rc3, rc4, rc5, rc6, rc7, rc8;

    private int servo1, servo2, servo3, servo4, servo5;

    // Robotic Arm Servo
    private float _base, _shoulder, _elbow, _wrist, _gripper;

    private int _switchMode = 0; // 0: Manual, 1: PosCtl
    //Setpoint for Offboard
    private bool _sendSetpoint = false;

    // Mission Offboard
    // Circle fly test
    public float circleRadius = 0.5f;
    public float flightHeight = 1.5f;
    public float circleSpeed = 1.0f; // rad/s
    public bool clockwise = true; // true: clockwise, false: counter-clockwise
    private float currentAngle = 0f;
    private bool isSending = false;
    private Vector3 circleCenter = Vector3.zero;
    public bool _missionEnable = false;
    public bool _missionTrainingEnable = false;
    public bool _missionOverrideVx = false;
    public bool _autoDrawActive = false;

    //Manual and Arm
    // Parameters config
    public float modeSwitchTimeout = 3.0f; // Timeout switch mode (second)
    public float armTimeout = 5.0f;        // Timeout arm command (second)
    public int maxArmRetries = 3;          // Times try maximum for arm

    // Check current mode
    public bool IsInManualMode
    {
        get { return _CurMode == Mode.MANUAL && _switchMode == 0; }
    }
    public bool IsInAltctlMode
    {
        get { return _CurMode == Mode.ALTCTL && _switchMode == 1; }
    }
    public bool IsInPosctlMode
    {
        get { return _CurMode == Mode.POSCTL && _switchMode == 2; }
    }
    public bool IsInOffboardMode
    {
        get { return _CurMode == Mode.OFFBOARD && _switchMode == 3; }
    }

    /****************************************************************/
    // State get from drone
    public bool TeleConnected
    {
        get { return _connected; }
    }
    public Mode TeleCurMode
    {
        get { return _CurMode; }
    }
    public float TeleBattery
    {
        get { return _battery; }
    }
    public bool TeleArmed
    {
        get { return _arm; }
    }

    // Coordinate get from drone
    public double TeleLatitude
    {
        get { return _lat; }
    }
    public double TeleLongitude
    {
        get { return _lon; }
    }
    public float TeleAltitude
    {
        get { return _alt; }
    }

    // Velocity get from drone
    public float TeleVx
    {
        get { return _vx; }
    }
    public float TeleVy
    {
        get { return _vy; }
    }
    public float TeleVz
    {
        get { return _vz; }
    }

    // Value to send control drone Manual Mode
    public int TeleVxControl
    {
        get { return _vxControl; }
        set { _vxControl = value; }
    }
    public int TeleVyControl
    {
        get { return _vyControl; }
        set { _vyControl = value; }
    }
    public int TeleYawRateControl
    {
        get { return _yawRateControl; }
        set { _yawRateControl = value; }
    }
    public int TeleVzControl
    {
        get { return _vzControl; }
        set { _vzControl = value; }
    }

    // Set RC signal
    public ushort RC1
    {
        get { return rc1; }
        set { rc1 = value; }
    }
    public ushort RC2
    {
        get { return rc2; }
        set { rc2 = value; }
    }
    public ushort RC3
    {
        get { return rc3; }
        set { rc3 = value; }
    }
    public ushort RC4
    {
        get { return rc4; }
        set { rc4 = value; }
    }
    public ushort RC5
    {
        get { return rc5; }
        set { rc5 = value; }
    }
    public ushort RC6
    {
        get { return rc6; }
        set { rc6 = value; }
    }
    public ushort RC7
    {
        get { return rc7; }
        set { rc7 = value; }
    }
    public ushort RC8
    {
        get { return rc8; }
        set { rc8 = value; }
    }

    // Velocity Offboard get/set - #AI
    public float VelX
    {
        get { return _velx; }
        set { _velx = value; }
    }
    public float VelY
    {
        get { return _vely; }
        set { _vely = value; }
    }
    public float VelZ
    {
        get { return _velz; }
        set { _velz = value; }
    }
    public float VelYawRate
    {
        get { return _velyawrate; }
        set { _velyawrate = value; }
    }
    public byte VelFrame
    {
        get { return _velframe; }
        set { _velframe = value; }
    }

    // Servo get/set ROBO mode
    public int TeleServo1
    {
        get { return servo1; }
        set { servo1 = value; }
    }
    public int TeleServo2
    {
        get { return servo2; }
        set { servo2 = value; }
    }
    public int TeleServo3
    {
        get { return servo3; }
        set { servo3 = value; }
    }
    public int TeleServo4
    {
        get { return servo4; }
        set { servo4 = value; }
    }
    public int TeleServo5
    {
        get { return servo5; }
        set { servo5 = value; }
    }
    public int TeleSwitchMode
    {
        get { return _switchMode; }
        set { _switchMode = value; }
    }

    public float BaseSV
    {
        get { return _base; }
        set { _base = value; }
    }

    public float ShoulderSV
    {
        get { return _shoulder; }
        set { _shoulder = value; }
    }

    public float ElbowSV
    {
        get { return _elbow; }
        set { _elbow = value; }
    }

    public float WristSV
    {
        get { return _wrist; }
        set { _wrist = value; }
    }

    public float GripperSV
    {
        get { return _gripper; }
        set { _gripper = value; }
    }

    public bool AutoDrawActive
    {
        get { return _autoDrawActive; }
        set { _autoDrawActive = value; }
    }

    //position offboard get/set
    public float PosX
    {
        get { return posx; }
        set { posx = value; }
    }
    public float PosY
    {
        get { return posy; }
        set { posy = value; }
    }
    public float PosZ
    {
        get { return posz; }
        set { posz = value; }
    }
    public float PosYaw
    {
        get { return posyaw; }
        set { posyaw = value; }
    }
    public byte PosF
    {
        get { return posframe; }
        set { posframe = value; }
    }

    // Orientation get/set
    public float Roll
    {
        get { return _roll; }
        set { _roll = value; }
    }
    public float Pitch
    {
        get { return _pitch; }
        set { _pitch = value; }
    }
    public float Yaw
    {
        get { return _yaw; }
        set { _yaw = value; }
    }

    // Distance sensors get/set
    public float ShortDistance
    {
        get { return _shortDistance; }
        set { _shortDistance = value; }
    }

    public float LongDistance
    {
        get { return _longDistance; }
        set { _longDistance = value; }
    }

    // Position Pen
    public float PositionPenX
    {
        get { return _positionPenX; }
        set { _positionPenX = value; }
    }

    public float PositionPenY
    {
        get { return _positionPenY; }
        set { _positionPenY = value; }
    }

    public float PositionPenZ
    {
        get { return _positionPenZ; }
        set { _positionPenZ = value; }
    }

    // Flag Mission enable
    public bool MissionEnable
    {
        get { return _missionEnable; }
        set { _missionEnable = value; }
    }
    public bool MissionTrainingEnable
    {
        get { return _missionTrainingEnable; }
        set { _missionTrainingEnable = value; }
    }
    public bool MissionOverrideVx
    {
        get { return _missionOverrideVx; }
        set { _missionOverrideVx = value;}
    }
        
    // Flag to switch velocity control - #AI
    public bool VelActive
    {
        get { return _velActive; }
        set { _velActive = value; }
    }

    void Start()
    {
        _CurMode = Mode.POSCTL;
        if (communication == null)
        {
            // Try to find Communication in parent
            communication = GetComponentInParent<Communication>();
            if (communication == null)
            {
                Debug.LogError("Communication component not found in parent hierarchy.");
                enabled = false;
                return;
            }
        }

    }
    public void SetFlightMode(int mode)
    {
        if (communication != null)
        {
            communication.SendCommandSetMode(mode);
            _switchMode = mode;
        }
    }

    /**************************** SETUP MODE & ARM *********************************/
    // -------------------- SETUP MANUAL MODE & ARM -------------------- //
    public void SetManualMode()
    {
        StopAllThreadsControl();
        OnManualModeButtonPressed();
    }
    public void OnManualModeButtonPressed()
    {
        StartCoroutine(SwitchToManualAndArmSequence());
    }
    private IEnumerator SwitchToManualAndArmSequence() 
    {
        if (_arm)
        {
            Debug.LogWarning("Drone is already armed. Disarm first.");
            yield break; // Break if drone state was armed
        }

        // Enable continuously thread control before switch mode
        communication.StartManualControlThread();

        // Send command control in Manual mode to active connect - (TEST)
        for (int i = 0; i < 25; i++) // ~0.75s 20Hz
        {
            communication.HandleHoldControl(); // z = 0
            yield return new WaitForSeconds(0.05f);
        }

        SetFlightMode((int)Mode.MANUAL);

        // Wait and check switch mode was successed
        bool modeSwitchSuccess = false;
        float modeSwitchStartTime = Time.time;

        while (Time.time - modeSwitchStartTime < modeSwitchTimeout)
        {
            yield return new WaitForSeconds(0.02f); // check every 100ms

            if (_CurMode == Mode.MANUAL)
            {
                modeSwitchSuccess = true;
                Debug.Log("Mode switched to MANUAL successfully.");
                break;
            }
        }

        if (!modeSwitchSuccess)
        {
            Debug.LogError("Failed to switch to MANUAL mode. Timeout.");
            yield break; // Break if switch mode false
        }

        // Retry arm and wait success
        int armRetryCount = 0;
        bool armSuccess = false;

        while (armRetryCount < maxArmRetries && !armSuccess)
        {
            armRetryCount++;
            Debug.Log($"Sending ARM command (Attempt {armRetryCount})");

            communication.SendCommandArmDisarm((byte)1);

            // wait and check result
            float armStartTime = Time.time;
            while (Time.time - armStartTime < armTimeout)
            {
                yield return new WaitForSeconds(0.02f); // check every 100ms

                if (_arm)
                {
                    armSuccess = true;
                    Debug.Log("Drone armed successfully!");
                    break;
                }
            }

            if (!armSuccess)
            {
                Debug.LogWarning($"Arm attempt {armRetryCount} failed. Timeout.");
                yield return new WaitForSeconds(0.5f);
            }
        }

        if (!armSuccess)
        {
            Debug.LogError("Failed to arm drone after all retries. Please check safety conditions.");
            // Warning UI message to user

        }
        else
        {
            communication.StartManualControlThread();

            for (int i = 0; i < 15; i++)
            {
                communication.HandleHoldControl();
                yield return new WaitForSeconds(0.05f);
            }
            Debug.Log("Drone is now in MANUAL mode and ARMED. Ready for flight.");
        }
    }

    // -------------------- SETUP ATLCTL MODE & ARM -------------------- //
    public void SetAltctlMode()
    {
        StopAllThreadsControl();
        OnAltctlModeButtonPressed();
    }
    public void OnAltctlModeButtonPressed()
    {
        StartCoroutine(SwitchToAltctlSequence());
    }
    private IEnumerator SwitchToAltctlSequence()
    {
        if (_arm)
        {
            Debug.LogWarning("Drone is already armed. Disarm first.");
            yield break;
        }
        // Enable continuously thread control before switch mode
        communication.StartAltctlControlThread();

        for (int i = 0; i < 25; i++) // ~0.75s 20Hz
        {
            communication.HandleHoldAltctlControl();
            yield return new WaitForSeconds(0.05f);
        }
        _switchMode = 1;
        SetFlightMode((int)Mode.ALTCTL);

        bool modeSwitchSuccess = false;
        float modeSwitchStartTime = Time.time;
        while (Time.time - modeSwitchStartTime < modeSwitchTimeout)
        {
            yield return new WaitForSeconds(0.05f); // check every 100ms
            if (_CurMode == Mode.ALTCTL)
            {
                modeSwitchSuccess = true;
                Debug.Log("Mode switched to ALTCTL successfully.");
                break;
            }
        }
        if (!modeSwitchSuccess)
        {
            Debug.LogError("Failed to switch to ALTCTL mode. Timeout.");
            yield break;
        }

        // 3. Send arm command, wait to successed (with retry)
        int armRetryCount = 0;
        bool armSuccess = false;

        while (armRetryCount < maxArmRetries && !armSuccess)
        {
            armRetryCount++;
            Debug.Log($"Sending ARM command (Attempt {armRetryCount})");

            communication.SendCommandArmDisarm((byte)1);

            // wait and check result
            float armStartTime = Time.time;
            while (Time.time - armStartTime < armTimeout)
            {
                yield return new WaitForSeconds(0.05f); // Check every 100ms

                if (_arm) // _arm was updated true state
                {
                    armSuccess = true;
                    Debug.Log("Drone armed successfully!");
                    break;
                }
            }

            if (!armSuccess)
            {
                Debug.LogWarning($"Arm attempt {armRetryCount} failed. Timeout.");
                yield return new WaitForSeconds(0.5f);
            }
        }

        if (!armSuccess)
        {
            Debug.LogError("Failed to arm drone after all retries. Please check safety conditions.");
            // Warning UI message to user

        }
        else
        {
            communication.StartAltctlControlThread();

            for (int i = 0; i < 15; i++)
            {
                communication.HandleHoldAltctlControl();
                yield return new WaitForSeconds(0.05f);
            }
            Debug.Log("Drone is now in ALTCTL mode and ARMED. Ready for flight.");
        }
    }

    // -------------------- SETUP POSCTL MODE & ARM -------------------- //
    public void SetPosctlMode()
    {
        StopAllThreadsControl();
        OnPosctlModeButtonPressed();
    }
    public void OnPosctlModeButtonPressed()
    {
        StartCoroutine(SwitchToPosctlSequence());
    }
    private IEnumerator SwitchToPosctlSequence()
    {
        if (_arm)
        {
            Debug.LogWarning("Drone is already armed. Disarm first.");
            yield break;
        }
        
        communication.StartPosctlControlThread();
        for (int i = 0; i < 25; i++) // ~0.75s với 20Hz
        {
            communication.HandleHoldPosctlControl();
            yield return new WaitForSeconds(0.05f);
        }
        _switchMode = 2;
        SetFlightMode((int)Mode.POSCTL);
        bool modeSwitchSuccess = false;
        float modeSwitchStartTime = Time.time;
        while (Time.time - modeSwitchStartTime < modeSwitchTimeout)
        {
            yield return new WaitForSeconds(0.05f); // check every 50ms
            if (_CurMode == Mode.POSCTL)
            {
                modeSwitchSuccess = true;
                Debug.Log("Mode switched to POSCTL successfully.");
                break;
            }
        }
        if (!modeSwitchSuccess)
        {
            Debug.LogError("Failed to switch to POSCTL mode. Timeout.");
            yield break;
        }
        // Send arm command, wait to successed (with retry)
        int armRetryCount = 0;
        bool armSuccess = false;

        while (armRetryCount < maxArmRetries && !armSuccess)
        {
            armRetryCount++;
            Debug.Log($"Sending ARM command (Attempt {armRetryCount})");

            communication.SendCommandArmDisarm((byte)1);

            // wait and check result
            float armStartTime = Time.time;
            while (Time.time - armStartTime < armTimeout)
            {
                yield return new WaitForSeconds(0.05f); // check every 50ms

                if (_arm) // _arm was updated to true
                {
                    armSuccess = true;
                    Debug.Log("Drone armed successfully!");
                    break;
                }
            }

            if (!armSuccess)
            {
                Debug.LogWarning($"Arm attempt {armRetryCount} failed. Timeout.");
                yield return new WaitForSeconds(0.5f);
            }
        }

        if (!armSuccess)
        {
            Debug.LogError("Failed to arm drone after all retries. Please check safety conditions.");
            // Warning UI message to user

        }
        else
        {
            communication.StartPosctlControlThread();

            for (int i = 0; i < 15; i++)
            {
                communication.HandleHoldPosctlControl();
                yield return new WaitForSeconds(0.05f);
            }
            Debug.Log("Drone is now in ALTCTL mode and ARMED. Ready for flight.");
        }
    }
    // ------------------ SETUP OFFBOARD MODE & ARM ------------------ //
    public void SetOffboardMode()
    {
        StopAllThreadsControl();
        OnOffboardModeButtonPressed();
    }
    public void OnOffboardModeButtonPressed()
    {
        StartCoroutine(SwitchToOffboardSequence());
    }
    private IEnumerator SwitchToOffboardSequence()
    {
        if (_arm)
        {
            Debug.LogWarning("Drone is already armed. Disarm first.");
            yield break;
        }

        // 1. Start thread Offboard Control
        communication.StartOffboardControlThread();

        // 2. Set mode to offboard control
        _switchMode = 3;
        communication.SendOffboardControlMode(true);

        // 3. Check mode switch success
        bool modeSwitchSuccess = false;
        float modeSwitchStartTime = Time.time;
        while (Time.time - modeSwitchStartTime < modeSwitchTimeout)
        {
            yield return new WaitForSeconds(0.05f); // check every 50ms
            if (_CurMode == Mode.OFFBOARD)
            {
                modeSwitchSuccess = true;
                Debug.Log("Mode switched to OFFBOARD successfully.");
                break;
            }
        }
        if (!modeSwitchSuccess)
        {
            Debug.LogError("Failed to switch to OFFBOARD mode. Timeout.");
            yield break;
        }

        // 4. If switch success, send ARM command and wait for success (with retry)
        int armRetryCount = 0;
        bool armSuccess = false;

        while (armRetryCount < maxArmRetries && !armSuccess)
        {
            armRetryCount++;
            Debug.Log($"Sending ARM command (Attempt {armRetryCount})");

            communication.SendCommandArmDisarm((byte)1);

            // Wait and check result
            float armStartTime = Time.time;
            while (Time.time - armStartTime < armTimeout)
            {
                yield return new WaitForSeconds(0.02f); // check every 20ms

                if (_arm)
                {
                    armSuccess = true;
                    Debug.Log("Drone armed successfully!");
                    break;
                }
            }

            if (!armSuccess)
            {
                Debug.LogWarning($"Arm attempt {armRetryCount} failed. Timeout.");
                yield return new WaitForSeconds(0.5f);
            }
        }

        if (!armSuccess)
        {
            Debug.LogError("Failed to arm drone after all retries. Please check safety conditions.");
            // Warning UI message to user

        }
        else
        {
            Debug.Log("Drone is now in MANUAL mode and ARMED. Ready for flight.");
        }
    }

    // SETUP CUSTOM MODE
    // ------------------- Voice Control Mode ------------------- //
    public void VCCircleStart()
    {
        StartCircleFlight();
        _missionEnable = true;
    }
    public void VCCircleStop()
    {
        _missionEnable = false;
        StopCircleFlight();
        ReturnToHome();
    }
    private async void StopAllThreadsControl()
    {
        _velActive = false;
        communication.StopOffboardControlThread();
        communication.StopManualControlThread();
        communication.StopPosctlControlThread();
        communication.StopAltctlControlThread();
        await Task.Delay(200); // wait 200ms for all threads stopped.
    }

    // Update is called once per frame
    void Update()
    {
        UpdateState();
        UpdateLocalPosition();
        if (_missionEnable && IsInOffboardMode)
        {
            UpdateCirclePathMission();
        }

    }

    // Mission Offboard Update
    void UpdateCirclePathMission()
    {
        if (!isSending) return;

        float direction = clockwise ? -1f : 1f;
        currentAngle += direction * circleSpeed * Time.deltaTime;

        if (currentAngle > 2 * Mathf.PI) currentAngle -= 2 * Mathf.PI;
        if (currentAngle < 0) currentAngle += 2 * Mathf.PI;

        float relX = circleRadius * Mathf.Cos(currentAngle);
        float relY = circleRadius * Mathf.Sin(currentAngle);
        float x = circleCenter.x + relX;
        float y = circleCenter.y + relY;
        float z = flightHeight;

        float yaw = currentAngle + (clockwise ? -Mathf.PI / 2 : Mathf.PI / 2);

        posx = x;
        posy = y;
        posz = z;
        posyaw = yaw;
        posframe = 0;
    }
    public void SetCircleCenterToCurrent()
    {

        float currentDroneX = communication.PosX;
        float currentDroneY = communication.PosY;

        circleCenter = new Vector3(currentDroneX, currentDroneY, 0f);
        Debug.Log($"Circle center set to: ({currentDroneX}, {currentDroneY})");
    }
    [ContextMenu("Start Circle Flight")]
    public void StartCircleFlight()
    {
        SetCircleCenterToCurrent();
        currentAngle = 0f;
        isSending = true;
        Debug.Log("Starting circle flight...");
    }
    [ContextMenu("Stop Circle Flight")]
    public void StopCircleFlight()
    {
        isSending = false;
        Debug.Log("Stopping circle flight...");
    }
    [ContextMenu("Return to Home")]
    public void ReturnToHome()
    {
        posx = 0f;
        posy = 0f;
        posz = flightHeight;
        posyaw = 0f;
        posframe = 0;
    }

    Vector3 CalculateCirclePosition(float angle)
    {
        float x = circleRadius * Mathf.Cos(angle);
        float y = circleRadius * Mathf.Sin(angle);
        float z = flightHeight;

        return new Vector3(x, y, z);
    }
    float CalculateYaw(float angle, bool clockwise)
    {
        float yawAngle = angle + (clockwise ? -Mathf.PI / 2 : Mathf.PI / 2);

        if (yawAngle > Mathf.PI) yawAngle -= 2 * Mathf.PI;
        if (yawAngle < -Mathf.PI) yawAngle += 2 * Mathf.PI;

        return yawAngle;
    }

    void UpdateState()
    {
        _arm = communication.Armed == 0 ? false : true;
        _connected = communication.Connected == 0 ? false : true;
        _CurMode = (Mode)communication.Mode;
        _battery = communication.Battery;

        //Debug.Log($"Mode: {_CurMode}, Armed: {_arm}, Connected: {_connected}");
    }
    void UpdateLocalPosition()
    {
        _lat = communication.Latitude;
        _lon = communication.Longitude;
        _alt = communication.Altitude;

        posx = communication.PosX;
        posy = communication.PosY;
        posz = communication.PosZ;
        posyaw = communication.PosYaw;

        _vx = communication.Vx;
        _vy = communication.Vy;
        _vz = communication.Vz;

        _roll = communication.Roll;
        _pitch = communication.Pitch;
        _yaw = communication.Yaw;

        _shortDistance = communication.ShortDistance;
        _longDistance = communication.LongDistance;
    }
}
