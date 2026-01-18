using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;

public class Communication : MonoBehaviour
{
    [SerializeField] private string _hostname;
    [SerializeField] private int _port;
    [SerializeField] private Telemetry telemetry;

    public delegate void DelegateState(Uavlink_msg_state_t tele);
    public delegate void DelegateLinked(bool isLinked);

    public event DelegateState StateChanged;
    public event DelegateLinked LinkedChanged;

    // Position in Offboard Mode
    public delegate void DelegatePositionFeedback(Uavlink_position_feedback_t feedback);
    public event DelegatePositionFeedback PositionFeedbackReceived;
    private Uavlink_position_feedback_t _positionFeedback;
    public Uavlink_position_feedback_t PositionFeedback
    {
        get { return _positionFeedback; }
    }

    // UDP and Threading
    private UdpConnect _connect;
    private Thread _receivingThread;
    private Thread _parsingThread;
    private Thread _sendingThread;
    private CancellationTokenSource _cts;
    private BlockingCollection<Uavlink_message_t> _sendQueue;
    private BlockingCollection<byte[]> _recvQueue;

    // Flag Manual Control thread
    public bool _isManualControlActive = false;
    private Thread _manualControlThread; // Thread send message continuously

    //Flag Altctl Control thread
    public bool _isAltctlControlActive = false;
    private Thread _altctlControlThread; // Thread send message continuously

    //Flag Posctl Control thread
    public bool _isPosctlControlActive = false;
    private Thread _posctlControlThread; // Thread send message continuously

    //Flag Offboard Control thread
    public bool _isOffboardControlActive = false;
    private Thread _offboardControlThread; // Thread send message continuously

    // Drone Status
    private UavlinkDroneStatus _droneStatus;
    public UavlinkDroneStatus DroneStatus => _droneStatus;

    // Start is called before the first frame update
    private sbyte _armed, _mode, _connected, _battery;
    private double _lat;
    private double _lon;
    private float _alt;
    private float _vx;
    private float _vy;
    private float _vz;
    private float _posx;
    private float _posy;
    private float _posz;
    private float _posyaw;
    private float _roll;
    private float _pitch;
    private float _yaw;
    private float _shortdist;
    private float _longdist;

    // Volocity Control (path-following)
    private float _velx;
    private float _vely;
    private float _velz;
    private float _yawrate;
    private byte _frame;
    public UdpConnect Connect
    {
        get { return _connect; }
    }
    // Drone State
    public sbyte Connected
    {
        get { return _connected; }
    }
    public sbyte Armed
    {
        get { return _armed; }
    }
    public sbyte Mode
    {
        get { return _mode; }
    }
    public sbyte Battery
    {
        get { return _battery; }
    }
    // Drone Coordinate
    public double Latitude
    {
        get { return _lat; }
    }
    public double Longitude
    {
        get { return _lon; }
    }
    public float Altitude
    {
        get { return _alt; }
    }
    // Drone Velocity
    public float Vx
    {
        get { return _vx; }
    }
    public float Vy
    {
        get { return _vy; }
    }
    public float Vz
    {
        get { return _vz; }
    }
    // Drone Position Offboard
    public float PosX
    {
        get { return _posx; }
    }
    public float PosY
    {
        get { return _posy; }
    }
    public float PosZ
    {
        get { return _posz; }
    }
    public float PosYaw
    {
        get { return _posyaw; }
        set { _posyaw = value; }
    }
    // Drone Tilt Angle
    public float Roll
    {
        get { return _roll; }
    }
    public float Pitch
    {
        get { return _pitch; }
    }
    public float Yaw
    {
        get { return _yaw; }
    }

    // Velocity Control (path-following)
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
    public float YawRate
    {
        get { return _yawrate; }
        set { _yawrate = value; }
    }
    public byte Frame
    {
        get { return _frame; }
        set { _frame = value; }
    }

    // Distance Sensor
    public float ShortDistance
    {
        get { return _shortdist; }
    }

    public float LongDistance
    {
        get { return _longdist; }
    }

    void Start()
    {
        //telemetry = GetComponent<Telemetry>();
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

    }

    void Update()
    {

    }
    /***********************************************************
     ************************ UDP Manager *********************
     ***********************************************************/
    public void StartUDP()
    {
        if (_connect != null && _connect.IsOpen)
        {
            Debug.LogWarning("UDP already started.");
            return;
        }

        _connect = new UdpConnect(_hostname, _port);
        _connect.DoConnect();

        if (_connect.IsOpen)
        {
            _cts = new CancellationTokenSource();

            _sendQueue = new BlockingCollection<Uavlink_message_t>();
            _recvQueue = new BlockingCollection<byte[]>();

            _receivingThread = new Thread(() => ReceivingThreadFunction(_cts.Token)) { IsBackground = true };
            _parsingThread = new Thread(() => ParsingThreadFunction(_cts.Token)) { IsBackground = true };
            _sendingThread = new Thread(() => SendingThreadFunction(_cts.Token)) { IsBackground = true };

            _receivingThread.Start();
            _parsingThread.Start();
            _sendingThread.Start();

            Debug.Log("UDP communication started.");
        }
        else
        {
            Debug.LogError("Failed to open UDP connection.");
        }
    }
    public void StopUDP()
    {
        Debug.Log("Stopping UDP communication...");

        _cts?.Cancel();

        // Wait for threads to finish
        JoinThread(_receivingThread);
        JoinThread(_parsingThread);
        JoinThread(_sendingThread);

        _receivingThread = null;
        _parsingThread = null;
        _sendingThread = null;

        _sendQueue?.CompleteAdding();
        _recvQueue?.CompleteAdding();

        _sendQueue?.Dispose();
        _recvQueue?.Dispose();
        _sendQueue = null;
        _recvQueue = null;

        _connect?.DoDisconnect();
        _connect = null;

        _cts?.Dispose();
        _cts = null;

        Debug.Log("UDP communication stopped.");
    }
    private void JoinThread(Thread thread)
    {
        if (thread != null && thread.IsAlive)
        {
            if (!thread.Join(1000))
            {
                Debug.LogWarning("Thread did not terminate in time.");
            }
        }
    }
    public void Dispose()
    {
        _connect?.DoDisconnect();
        _connect = null;

        //kill thread

        _parsingThread?.Abort();
        _sendingThread?.Abort();
        _receivingThread?.Abort();

        _parsingThread?.Join();
        _sendingThread?.Join();
        _receivingThread?.Join();

        _sendingThread = null;
        _parsingThread = null;
        _receivingThread = null;

        _sendQueue?.Dispose();
        _recvQueue?.Dispose();
        _sendQueue = null;
        _recvQueue = null;
    }
    // UDP Communication with 3 threads: Receiving, Parsing, Sending
    private void ReceivingThreadFunction(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (_connect != null && _connect.IsOpen)
                {
                    if (_connect.ReceiveData(out var buffer) > 0)
                    {
                        _recvQueue.Add(buffer, token);
                    }
                }
                Thread.Sleep(1);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.LogError($"ReceivingThreadFunction error: {ex}");
        }
    }
    private void ParsingThreadFunction(CancellationToken token)
    {
        try
        {
            foreach (var buffer in _recvQueue.GetConsumingEnumerable(token))
            {
                var message = new Uavlink_message_t();
                message.Decode(buffer);
                OnReceivedMessage(message);
                Thread.Sleep(1);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.LogError($"ParsingThreadFunction error: {ex}");
        }
    }
    private void SendingThreadFunction(CancellationToken token)
    {
        try
        {
            foreach (var message in _sendQueue.GetConsumingEnumerable(token))
            {
                if (message != null)
                {
                    message.Encode(out var buffer);
                    int size = buffer.Length;
                    while (size > 0 && _connect != null && _connect.IsOpen)
                    {
                        try
                        {
                            size -= _connect.SendData(buffer);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"SendingThreadFunction send error: {ex}");
                            break;
                        }
                    }
                }
                Thread.Sleep(1);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.LogError($"SendingThreadFunction error: {ex}");
        }
    }
    private void SendMessage(Uavlink_message_t message)
    {
        if (_sendQueue != null && !_sendQueue.IsAddingCompleted)
            _sendQueue.Add(message);
    }
    // Handle received messages
    private void OnReceivedMessage(Uavlink_message_t message)
    {
        switch (message.Msgid)
        {
            case MessageId.UAVLINK_MSG_ID_STATE:
                OnReceivedState(message.Payload);
                break;

            // Position Feedback for Offboard
            case MessageId.UAVLINK_MSG_ID_POSITION_FEEDBACK:
                OnReceivedPositionFeedback(message.Payload);
                break;

            case MessageId.UAVLINK_MSG_ID_DRONE_STATUS:
                OnReceivedDroneStatus(message.Payload);
                break;

            default:
                break;
        }
    }
    private void OnReceivedState(byte[] data)
    {
        Uavlink_msg_state_t state = new Uavlink_msg_state_t();
        state.Decode(data);
        StateChanged?.Invoke(state);

        _armed = state.Armed;
        _mode = state.Mode;
        _connected = state.Connected;
        _battery = state.Battery;
    }
    private void OnReceivedDroneStatus(byte[] data)
    {
        if (_droneStatus == null) _droneStatus = new UavlinkDroneStatus();
        _droneStatus.Decode(data);

        _alt = (float)Math.Round(_droneStatus.Altitude, 1);
        _battery = _droneStatus.Battery;
        _lat = _droneStatus.Latitude;
        _lon = _droneStatus.Longitude;
        _posx = (float)Math.Round(_droneStatus.PosX, 3);
        _posy = (float)Math.Round(_droneStatus.PosY, 3);
        _posz = (float)Math.Round(_droneStatus.PosZ, 3);

        _vx = (float)Math.Round(_droneStatus.VelocityX, 2);
        _vy = (float)Math.Round(_droneStatus.VelocityY, 2);
        _vz = (float)Math.Round(_droneStatus.VelocityZ, 2);                      

        _roll = (float)Math.Round(_droneStatus.Roll * Mathf.Rad2Deg, 3);
        _pitch = (float)Math.Round(_droneStatus.Pitch * Mathf.Rad2Deg, 3);
        _yaw = (float)Math.Round(_droneStatus.Yaw * Mathf.Rad2Deg, 3);
        _posyaw = _droneStatus.Yaw;

        _shortdist = _droneStatus.ShortDistance - 0.03f;
        _longdist = _droneStatus.LongDistance;

        Debug.Log($"[DRONE STATUS] Alt={_droneStatus.Altitude}, Bat={_droneStatus.Battery}%, " +
            $"Lat={_droneStatus.Latitude}, Lon={_droneStatus.Longitude}, " +
            $"X={_droneStatus.PosX}, Y={_droneStatus.PosY}, Z={_droneStatus.PosZ}");
    }
    private void OnReceivedPositionFeedback(byte[] data)
    {
        Uavlink_position_feedback_t feedback = new Uavlink_position_feedback_t();
        feedback.Decode(data);
        _positionFeedback = feedback;
        PositionFeedbackReceived?.Invoke(feedback);

        Debug.Log($"Position Feedback: Success={feedback.success}, Error=({feedback.error_x:F2}, {feedback.error_y:F2}, {feedback.error_z:F2})");

    }
    // Send Functions - Commands message
    public Task<Tuple<bool, string>> SendCommandTakeoff(float altitude)
    {
        Func<Tuple<bool, string>> sendCommand = () =>
        {
            Tuple<bool, string> resAnswers = new Tuple<bool, string>(false, null);
            byte[] takeoff_data;

            Uavlink_cmd_takeoff_t takeoffcmd = new Uavlink_cmd_takeoff_t();
            takeoffcmd.Altitude = altitude;
            takeoffcmd.Encode(out takeoff_data);

            Uavlink_message_t message = new Uavlink_message_t();
            message.Msgid = MessageId.UAVLINK_MSG_ID_COMMAND;
            message.LenPayload = (sbyte)takeoff_data.Length;
            message.Payload = takeoff_data;

            SendMessage(message);
            resAnswers = Tuple.Create(true, "send command ok");
            return resAnswers;
        };
        var task = new Task<Tuple<bool, string>>(sendCommand);
        task.Start();
        return task;
    }
    //public Task<Tuple<bool, string>> SendCommandFlyto(byte allwp, int wpid)
    //{
    //    Func<Tuple<bool, string>> sendCommand = () =>
    //    {
    //        Tuple<bool, string> resAnswers = new Tuple<bool, string>(false, null);
    //        byte[] flyto_data;

    //        Uavlink_cmd_flyto_t flyto_cmd = new Uavlink_cmd_flyto_t();
    //        flyto_cmd.AllWP = allwp;
    //        flyto_cmd.WPId = wpid;

    //        flyto_cmd.Encode(out flyto_data);

    //        Uavlink_message_t message = new Uavlink_message_t();
    //        message.Msgid = MessageId.UAVLINK_MSG_ID_COMMAND;
    //        message.LenPayload = (sbyte)flyto_data.Length;
    //        message.Payload = flyto_data;

    //        SendMessage(message);
    //        resAnswers = Tuple.Create(true, "send command ok");
    //        return resAnswers;
    //    };
    //    var task = new Task<Tuple<bool, string>>(sendCommand);
    //    task.Start();
    //    return task;
    //}
    public void SendCommandSetMode(int mode)
    {
        Debug.Log($"Setting mode to: {mode}");

        byte[] setmode_data;
        Uavlink_cmd_setmode_t setmodeCmd = new Uavlink_cmd_setmode_t();
        setmodeCmd.Mode = (byte)mode;
        setmodeCmd.Encode(out setmode_data);

        Uavlink_message_t message = new Uavlink_message_t();
        message.Msgid = MessageId.UAVLINK_MSG_ID_COMMAND;
        message.LenPayload = (sbyte)setmode_data.Length;
        message.Payload = setmode_data;

        SendMessage(message);
        Debug.Log("Set mode command sent.");
    }
    public Task<Tuple<bool, string>> SendCommandArmDisarm(byte armdisarm)
    {
        Func<Tuple<bool, string>> sendCommand = () =>
        {
            Tuple<bool, string> resAnswers = new Tuple<bool, string>(false, null);
            byte[] data = new byte[3];
            BitConverter.GetBytes((UInt16)CommandId.UAVLINK_CMD_ARM).CopyTo(data, 0);
            data[2] = armdisarm;

            Uavlink_message_t message = new Uavlink_message_t();
            message.Msgid = MessageId.UAVLINK_MSG_ID_COMMAND;
            message.LenPayload = 3;
            message.Payload = data;
            SendMessage(message);
            resAnswers = Tuple.Create(true, "send command ok");
            return resAnswers;
        };
        var task = new Task<Tuple<bool, string>>(sendCommand);
        task.Start();
        return task;
    }
    public Task<Tuple<bool, string>> SendCommandLand()
    {
        Func<Tuple<bool, string>> sendCommand = () =>
        {
            Tuple<bool, string> resAnswers = new Tuple<bool, string>(false, null);
            Uavlink_message_t message = new Uavlink_message_t();
            message.Msgid = MessageId.UAVLINK_MSG_ID_COMMAND;
            message.LenPayload = 2;
            message.Payload = BitConverter.GetBytes((UInt16)CommandId.UAVLINK_CMD_LAND);
            SendMessage(message);

            resAnswers = Tuple.Create(true, "send command ok");
            return resAnswers;
        };
        var task = new Task<Tuple<bool, string>>(sendCommand);
        task.Start();
        return task;
    }
    //public void SendMissionMessage(Waypoint _wp)
    //{
    //    if (_wp != null)
    //    {
    //        byte[] wp_data;

    //        Uavlink_msg_setwp_t wp_message = new Uavlink_msg_setwp_t();
    //        wp_message.WaypointID = _wp.WaypointID;
    //        wp_message.TargetX = _wp.PosX;
    //        wp_message.TargetY = _wp.PosY;
    //        wp_message.TargetZ = 1;
    //        wp_message.Encode(out wp_data);

    //        Uavlink_message_t message = new Uavlink_message_t();
    //        message.Msgid = MessageId.UAVLINK_MSG_SETWP;
    //        message.LenPayload = (sbyte)wp_data.Length;
    //        message.Payload = wp_data;

    //        SendMessage(message);
    //        return;
    //    }
    //    return;
    //}

    /***********************************************************
     ************************Change MODE and Arm****************
     ***********************************************************/
    // ----------------- Manual Control Thread ----------------- //
    public void StartManualControlThread()
    {
        if (_isManualControlActive) return;

        _isManualControlActive = true;
        _manualControlThread = new Thread(ManualControlWorkerThread);
        _manualControlThread.IsBackground = true;
        _manualControlThread.Start();
        Debug.Log("Manual control thread STARTED.");
    }
    public void StopManualControlThread()
    {
        _isManualControlActive = false;
        if (_manualControlThread != null && _manualControlThread.IsAlive)
        {
            _manualControlThread.Join(200); // wait thread stop within 200ms
        }
        Debug.Log("Manual control thread STOPPED.");
    }
    private void ManualControlWorkerThread() // Thread: send command control with staliblize frequency
    {
        System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        watch.Start();

        while (_isManualControlActive)
        {
            try
            {
                long startTime = watch.ElapsedMilliseconds;
                HandleManualControl();

                // Calculate time need to sleep to maintain 20Hz frequency (50ms/time)
                long processingTime = watch.ElapsedMilliseconds - startTime;
                long sleepTime = Math.Max(0, 10 - processingTime); // 50ms cho 20Hz

                Thread.Sleep((int)sleepTime);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error in control thread: {ex.Message}");
                break;
            }
        }
        watch.Stop();
    }
    private void HandleManualControl()
    {
        Uavlink_msg_manual_control_t manual_msg = new Uavlink_msg_manual_control_t();

        manual_msg.x = telemetry.TeleVxControl;      // Roll: [-1000, 1000]
        manual_msg.y = telemetry.TeleVyControl;      // Pitch: [-1000, 1000]
        manual_msg.z = telemetry.TeleVzControl;      // Throttle: [-1000, 1000]
        manual_msg.r = telemetry.TeleYawRateControl; // Yaw: [-1000, 1000]


        byte[] manual_pack;
        manual_msg.Encode(out manual_pack);

        Uavlink_message_t message = new Uavlink_message_t();
        message.Msgid = MessageId.UAVLINK_MSG_ID_MANUAL_CONTROL;
        message.LenPayload = (sbyte)manual_pack.Length;
        message.Payload = manual_pack;

        SendMessage(message);
    }
    // ------------------ Altctl Control Thread ------------------ //
    public void StartAltctlControlThread()
    {
        if (_isAltctlControlActive) return;
        _isAltctlControlActive = true;
        _altctlControlThread = new Thread(AltctlControlWorkerThread);
        _altctlControlThread.IsBackground = true;
        _altctlControlThread.Start();
        Debug.Log("Altctl control thread STARTED.");
    }
    public void StopAltctlControlThread()
    {
        _isAltctlControlActive = false;
        if (_altctlControlThread != null && _altctlControlThread.IsAlive)
        {
            _altctlControlThread.Join(200); // wait thread stop within 200ms
        }
        Debug.Log("Altctl control thread STOPPED.");
    }
    private void AltctlControlWorkerThread() // Thread: send command control with staliblize frequency
    {
        System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        watch.Start();
        while (_isAltctlControlActive)
        {
            try
            {
                long startTime = watch.ElapsedMilliseconds;
                HandleAltctlControl();
                // Calculate time need to sleep to maintain 20Hz frequency (50ms/time)
                long processingTime = watch.ElapsedMilliseconds - startTime;
                long sleepTime = Math.Max(0, 20 - processingTime); // 50ms cho 20Hz
                Thread.Sleep((int)sleepTime);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error in control thread: {ex.Message}");
                break;
            }
        }
        watch.Stop();
    }
    private void HandleAltctlControl()
    {
        Uavlink_msg_manual_control_t manual_msg = new Uavlink_msg_manual_control_t();

        manual_msg.x = telemetry.TeleVxControl;      // Roll: [-1000, 1000]
        manual_msg.y = telemetry.TeleVyControl;      // Pitch: [-1000, 1000]
        manual_msg.z = telemetry.TeleVzControl;      // Throttle: [-1000, 1000]
        manual_msg.r = telemetry.TeleYawRateControl; // Yaw: [-1000, 1000]


        byte[] manual_pack;
        manual_msg.Encode(out manual_pack);

        Uavlink_message_t message = new Uavlink_message_t();
        message.Msgid = MessageId.UAVLINK_MSG_ID_MANUAL_CONTROL;
        message.LenPayload = (sbyte)manual_pack.Length;
        message.Payload = manual_pack;

        SendMessage(message);
    }
    // ------------------- Posctl Control Thread ------------------- //
    public void StartPosctlControlThread()
    {
        if (_isPosctlControlActive) return;
        _isPosctlControlActive = true;
        _posctlControlThread = new Thread(PosctlControlWorkerThread);
        _posctlControlThread.IsBackground = true;
        _posctlControlThread.Start();
        Debug.Log("Posctl control thread STARTED.");
    }
    public void StopPosctlControlThread()
    {
        _isPosctlControlActive = false;
        if (_posctlControlThread != null && _posctlControlThread.IsAlive)
        {
            _posctlControlThread.Join(200); // Chờ luồng kết thúc trong 200ms
        }
        Debug.Log("Posctl control thread STOPPED.");
    }
    private void PosctlControlWorkerThread() // Thread: send command control with staliblize frequency
    {
        System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        watch.Start();
        while (_isPosctlControlActive)
        {
            try
            {
                long startTime = watch.ElapsedMilliseconds;
                HandlePosctlControl();
                // Calculate time need to sleep to maintain 20Hz frequency (50ms/time)
                long processingTime = watch.ElapsedMilliseconds - startTime;
                long sleepTime = Math.Max(0, 20 - processingTime); // 50ms cho 20Hz
                Thread.Sleep((int)sleepTime);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error in control thread: {ex.Message}");
                break;
            }
        }
        watch.Stop();
    }
    private void HandlePosctlControl()
    {
        Uavlink_msg_manual_control_t manual_msg = new Uavlink_msg_manual_control_t();

        manual_msg.x = telemetry.TeleVxControl;      // Roll: [-1000, 1000]
        manual_msg.y = telemetry.TeleVyControl;      // Pitch: [-1000, 1000]
        manual_msg.z = telemetry.TeleVzControl;      // Throttle: [-1000, 1000]
        manual_msg.r = telemetry.TeleYawRateControl; // Yaw: [-1000, 1000]

        byte[] manual_pack;
        manual_msg.Encode(out manual_pack);

        Uavlink_message_t message = new Uavlink_message_t();
        message.Msgid = MessageId.UAVLINK_MSG_ID_MANUAL_CONTROL;
        message.LenPayload = (sbyte)manual_pack.Length;
        message.Payload = manual_pack;

        SendMessage(message);
    }
    // ------------------- OFFBOARD Control Thread ------------------- //
    public void SendImmediateControlOnModeSwitch()
    {
        if (telemetry.VelActive)
        {
            SendVelocityControl();
        }
        else
        {
            HandleOffboardControl();
        }
    }
    public void StartOffboardControlThread()
    {
        if (_isOffboardControlActive) return;
        _isOffboardControlActive = true;
        _offboardControlThread = new Thread(OffboardControlWorkerThread);
        _offboardControlThread.IsBackground = true;
        _offboardControlThread.Start();
        Debug.Log("Offboard control thread STARTED.");
    }
    public void StopOffboardControlThread()
    {
        _isOffboardControlActive = false;
        if (_offboardControlThread != null && _offboardControlThread.IsAlive)
        {
            _offboardControlThread.Join(200); // wait thread stop within 200ms
        }
        Debug.Log("Offboard control thread STOPPED.");
    }
    private void OffboardControlWorkerThread() // Thread: send command with stablize frequency
    {
        System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        watch.Start();
        while (_isOffboardControlActive)
        {
            try
            {
                long startTime = watch.ElapsedMilliseconds;
                if (telemetry.VelActive == false)
                {
                    HandleOffboardControl();
                }
                else
                {
                    SendVelocityControl(); // Send command velocity control if VelActive = true
                }

                // Calculate time need to sleep to maintain 20Hz frequency (50ms/time)
                long processingTime = watch.ElapsedMilliseconds - startTime;
                long sleepTime = Math.Max(0, 20 - processingTime); // 50ms cho 20Hz
                Thread.Sleep((int)sleepTime);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error in control thread: {ex.Message}");
                break;
            }
        }
        watch.Stop();
    }
    public void HandleOffboardControl()
    {
        Uavlink_position_control_t posoffboard_msg = new Uavlink_position_control_t();
        posoffboard_msg.x = telemetry.PosX;
        posoffboard_msg.y = telemetry.PosY;
        //posoffboard_msg.z = telemetry.PosZ;
        posoffboard_msg.z = 0.8f;
        posoffboard_msg.yaw = telemetry.PosYaw;
        //posoffboard_msg.frame = telemetry.PosF;
        posoffboard_msg.frame = 0;

        byte[] posoffboard_pack;
        posoffboard_msg.Encode(out posoffboard_pack);

        Uavlink_message_t message = new Uavlink_message_t();
        message.Msgid = MessageId.UAVLINK_MSG_ID_POSITION_CONTROL;
        message.LenPayload = (sbyte)posoffboard_pack.Length;
        message.Payload = posoffboard_pack;

        SendMessage(message);
    }
    // Velocity Control Offboard Mode
    public void SendVelocityControl()
    {
        Uavlink_msg_velocity_control_t vel_msg = new Uavlink_msg_velocity_control_t();
        vel_msg.vx = telemetry.VelX;
        vel_msg.vy = telemetry.VelY;
        vel_msg.vz = telemetry.VelZ;
        vel_msg.yaw_rate = telemetry.VelYawRate;
        vel_msg.frame = 1; // 0: ENU local map, 1: body frame

        byte[] vel_pack;
        vel_msg.Encode(out vel_pack);

        Uavlink_message_t message = new Uavlink_message_t();
        message.Msgid = MessageId.UAVLINK_MSG_ID_VELOCITY_CONTROL;
        message.LenPayload = (sbyte)vel_pack.Length;
        message.Payload = vel_pack;

        SendMessage(message);
    }
    // Function On/Off position control in Offboard mode
    public void SendOffboardControlMode(bool enable)
    {
        byte[] data = new byte[6]; // command (2) + param1 (4)
        BitConverter.GetBytes((UInt16)CommandId.UAVLINK_CMD_POSITION_CONTROL_MODE).CopyTo(data, 0);
        BitConverter.GetBytes(enable ? 1f : 0f).CopyTo(data, 2);

        Uavlink_message_t message = new Uavlink_message_t
        {
            Msgid = MessageId.UAVLINK_MSG_ID_COMMAND,
            LenPayload = (sbyte)data.Length,
            Payload = data
        };

        SendMessage(message);
        Debug.Log($"Offboard Control Mode: {(enable ? "ENABLED" : "DISABLED")}");
    }
    // Function On/Off Record get samples for path-following training
    public void RecordDataTrainingActive(bool enable)
    {
        byte[] data = new byte[6]; // command (2) + param1 (4)
        BitConverter.GetBytes((UInt16)CommandId.UAVLINK_CMD_TRAIN_TOGGLE).CopyTo(data, 0);
        BitConverter.GetBytes(enable ? 1f : 0f).CopyTo(data, 2);

        Uavlink_message_t message = new Uavlink_message_t
        {
            Msgid = MessageId.UAVLINK_MSG_ID_COMMAND,
            LenPayload = (sbyte)data.Length,
            Payload = data
        };

        SendMessage(message);
        Debug.Log($"Record Data Mode: {(enable ? "ENABLED" : "DISABLED")}");
    }
    public void VxOverrideActive(bool enable)
    {
        byte[] data = new byte[6]; // command (2) + param1 (4)
        BitConverter.GetBytes((UInt16)CommandId.UAVLINK_CMD_VX_OVERRIDE).CopyTo(data, 0);
        BitConverter.GetBytes(enable ? 1f : 0f).CopyTo(data, 2);

        Uavlink_message_t message = new Uavlink_message_t
        {
            Msgid = MessageId.UAVLINK_MSG_ID_COMMAND,
            LenPayload = (sbyte)data.Length,
            Payload = data
        };

        SendMessage(message);
        Debug.Log($"Vx Override Mode: {(enable ? "ENABLED" : "DISABLED")}");
    }
    //Function On/Off Circle mode for Offboard Mission
    public void SendCircleMode(bool enable)
    {
        byte[] data = new byte[6]; // command (2) + param1 (4)
        BitConverter.GetBytes((UInt16)CommandId.UAVLINK_CMD_CIRCLE).CopyTo(data, 0);
        BitConverter.GetBytes(enable ? 1f : 0f).CopyTo(data, 2);
        Uavlink_message_t message = new Uavlink_message_t
        {
            Msgid = MessageId.UAVLINK_MSG_ID_COMMAND,
            LenPayload = (sbyte)data.Length,
            Payload = data
        };
        SendMessage(message);
        Debug.Log($"Circle Mode: {(enable ? "ENABLED" : "DISABLED")}");
    }

    // ------------------ Robotic Arm Control ------------------ //
    public void HandleServoControl()
    {
        Uavlink_msg_servo_control_t servo_msg = new Uavlink_msg_servo_control_t();
        servo_msg.servo0 = telemetry.BaseSV;
        servo_msg.servo1 = telemetry.ShoulderSV;
        servo_msg.servo2 = telemetry.ElbowSV;
        servo_msg.servo3 = telemetry.WristSV;
        //servo_msg.servo4 = telemetry.GripperSV;
        servo_msg.servo4 = 90f;

        byte[] servo_pack;
        servo_msg.Encode(out servo_pack);

        Uavlink_message_t message = new Uavlink_message_t();
        message.Msgid = MessageId.UAVLINK_MSG_ID_SERVO_CONTROL;
        message.LenPayload = (sbyte)servo_pack.Length;
        message.Payload = servo_pack;

        SendMessage(message);
        Debug.Log("Sent Servo control message.");
    }
    /***********************************************************/

    //Stop All Thread when Disarm
    public void OnDisarm()
    {
        StopOffboardControlThread();
        StopManualControlThread();
        StopAltctlControlThread();
        SendCommandArmDisarm((byte)0);
    }

    // Hold Control when Switch Mode - Testing, not recommend for main project
    public void HandleHoldControl()
    {
        Uavlink_msg_manual_control_t manual_msg = new Uavlink_msg_manual_control_t();

        manual_msg.x = 0;
        manual_msg.y = 0;
        manual_msg.z = 500;
        manual_msg.r = 0;

        byte[] manual_pack;
        manual_msg.Encode(out manual_pack);

        Uavlink_message_t message = new Uavlink_message_t();
        message.Msgid = MessageId.UAVLINK_MSG_ID_MANUAL_CONTROL;
        message.LenPayload = (sbyte)manual_pack.Length;
        message.Payload = manual_pack;

        SendMessage(message);
    }
    public void HandleHoldAltctlControl()
    {
        Uavlink_msg_manual_control_t manual_msg = new Uavlink_msg_manual_control_t();

        manual_msg.x = 0;
        manual_msg.y = 0;
        manual_msg.z = 500;
        manual_msg.r = 0;

        byte[] manual_pack;
        manual_msg.Encode(out manual_pack);

        Uavlink_message_t message = new Uavlink_message_t();
        message.Msgid = MessageId.UAVLINK_MSG_ID_MANUAL_CONTROL;
        message.LenPayload = (sbyte)manual_pack.Length;
        message.Payload = manual_pack;

        SendMessage(message);
    }
    public void HandleHoldPosctlControl()
    {
        Uavlink_msg_manual_control_t manual_msg = new Uavlink_msg_manual_control_t();

        manual_msg.x = 0;
        manual_msg.y = 0;
        manual_msg.z = 500;
        manual_msg.r = 0;

        byte[] manual_pack;
        manual_msg.Encode(out manual_pack);

        Uavlink_message_t message = new Uavlink_message_t();
        message.Msgid = MessageId.UAVLINK_MSG_ID_MANUAL_CONTROL;
        message.LenPayload = (sbyte)manual_pack.Length;
        message.Payload = manual_pack;

        SendMessage(message);
    }    
    public void HandleHoldOffboardControl()
    {
        Uavlink_position_control_t posoffboard_msg = new Uavlink_position_control_t();
        posoffboard_msg.x = telemetry.PosX;
        posoffboard_msg.y = telemetry.PosY;
        posoffboard_msg.z = telemetry.PosZ;
        posoffboard_msg.yaw = telemetry.PosYaw;
        posoffboard_msg.frame = 0; // 0: ENU local map, 1: body frame

        byte[] posoffboard_pack;
        posoffboard_msg.Encode(out posoffboard_pack);

        Uavlink_message_t messageHold = new Uavlink_message_t();
        messageHold.Msgid = MessageId.UAVLINK_MSG_ID_POSITION_CONTROL;
        messageHold.LenPayload = (sbyte)posoffboard_pack.Length;
        messageHold.Payload = posoffboard_pack;

        SendMessage(messageHold);
    }
    public void HandleHoldVelocityControl()
    {
        Uavlink_msg_velocity_control_t vel_msg = new Uavlink_msg_velocity_control_t();

        vel_msg.vx = 0f;
        vel_msg.vy = 0f;
        vel_msg.vz = 0.0f;
        vel_msg.yaw_rate = 0f;
        vel_msg.frame = 1; // 0: ENU local map, 1: body frame

        byte[] vel_pack;
        vel_msg.Encode(out vel_pack);
        Uavlink_message_t message = new Uavlink_message_t
        {
            Msgid = MessageId.UAVLINK_MSG_ID_VELOCITY_CONTROL,
            LenPayload = (sbyte)vel_pack.Length,
            Payload = vel_pack
        };
        SendMessage(message);
    }
    public void HandHoldRCControl()
    {
        Uavlink_msg_rc_channels_t rcChannels = new Uavlink_msg_rc_channels_t
        {
            chan1 = 1500,
            chan2 = 1500,
            chan3 = 1500,
            chan4 = 1500,
            chan5 = 1000,
            chan6 = 1000,
            chan7 = 1000,
            chan8 = 1000
        };

        byte[] rcData;
        rcChannels.Encode(out rcData);

        Uavlink_message_t message = new Uavlink_message_t();
        message.Msgid = MessageId.UAVLINK_MSG_ID_RC_CHANNELS;
        message.LenPayload = (sbyte)rcData.Length;
        message.Payload = rcData;

        SendMessage(message);
        Debug.Log("Sent RC hold control message.");
    }
}
