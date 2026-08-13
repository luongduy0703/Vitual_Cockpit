using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ControlCanvas : MonoBehaviour
{
    [SerializeField] private Communication _communication;
    [SerializeField] private Telemetry telemetry;

    // Drone Action
    [SerializeField] private Button TakeOffBtn;
    [SerializeField] private Button DisArmBtn;
    [SerializeField] private Button ArmBtn;
    [SerializeField] private Button ConnectBtn;
    [SerializeField] private Button DisConnectBtn;
    [SerializeField] private Button LandBtn;

    // Drone Control Mode
    [SerializeField] private Button ManualModeBtn;
    [SerializeField] private Button PosctlModeBtn;
    [SerializeField] private Button OffBoardModeBtn;
    [SerializeField] private Button AltctlModeBtn;

    // Mission Planner - path following training
    [SerializeField] private Button LandModeBtn;
    [SerializeField] private Button VelocityModeBtn;
    [SerializeField] private Button StartRecordBtn;
    [SerializeField] private Button StopRecordBtn;
    //[SerializeField] private Button StartVxBtn;
    //[SerializeField] private Button StopVxBtn;

    [Header("Drawing Canvas Toggle")]
    [SerializeField] private GameObject simpleDrawingCanvasGO;
    [SerializeField] private Button ShowDrawingCanvasBtn;
    [SerializeField] private Button HideDrawingCanvasBtn;

    // Servo Control Mode
    [SerializeField] private Button ServoControl;
    [SerializeField] private Button StopServoControl;

    // Drone state
    [SerializeField] private Text uavStateCanvasText;
    [SerializeField] private Text armDisarmCanvasText;
    [SerializeField] private Text connectionStateCanvasText;

    void Start()
    {
        if (_communication == null)
        {
            _communication = GetComponent<Communication>();
            if (_communication == null)
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

        Button btnTakeOff = TakeOffBtn.GetComponent<Button>();
        Button btnDisArm = DisArmBtn.GetComponent<Button>();
        Button btnArm = ArmBtn.GetComponent<Button>();
        Button btnConnect = ConnectBtn.GetComponent<Button>();
        Button btnDisConnect = DisConnectBtn.GetComponent<Button>();
        Button btnLand = LandBtn.GetComponent<Button>();

        Button btnManual = ManualModeBtn.GetComponent<Button>();
        Button btnAltctl = AltctlModeBtn.GetComponent<Button>();
        Button btnPosctl = PosctlModeBtn.GetComponent<Button>();
        Button btnOffBoard = OffBoardModeBtn.GetComponent<Button>();
        Button btnLandMode = LandModeBtn.GetComponent<Button>();

        Button btnStartRecord = StartRecordBtn.GetComponent<Button>();
        Button btnStopRecord = StopRecordBtn.GetComponent<Button>();
        Button btnVelocityMode = VelocityModeBtn.GetComponent<Button>();
        //Button btnStartVx = StartVxBtn.GetComponent<Button>();
        //Button btnStopVx = StopVxBtn.GetComponent<Button>();
        Button btnServoControl = ServoControl.GetComponent<Button>();
        Button btnStopServoControl = StopServoControl.GetComponent<Button>();

        btnTakeOff.onClick.AddListener(TakeOffOnClick);
        btnDisArm.onClick.AddListener(DisArmOnClick);
        btnArm.onClick.AddListener(ArmOnClick);
        btnLand.onClick.AddListener(LandOnClick);
        btnConnect.onClick.AddListener(ConnectOnClick);
        btnDisConnect.onClick.AddListener(DisconnectOnClick);

        btnManual.onClick.AddListener(ManualOnClick);
        btnAltctl.onClick.AddListener(AltctlOnClick);
        btnPosctl.onClick.AddListener(PositionOnClick);
        btnOffBoard.onClick.AddListener(OffBoardOnClick);
        btnLandMode.onClick.AddListener(LandModeOnClick);

        btnStartRecord.onClick.AddListener(StartRecordOnClick);
        btnStopRecord.onClick.AddListener(StopRecordOnClick);
        btnVelocityMode.onClick.AddListener(VelocityModeOnClick);
        //btnStartVx.onClick.AddListener(StartVxOnClick);
        //btnStopVx.onClick.AddListener(StopVxOnClick);
        btnServoControl.onClick.AddListener(ServoControlOnClick);
        btnStopServoControl.onClick.AddListener(StopServoControlOnClick);

        if (ShowDrawingCanvasBtn != null)
            ShowDrawingCanvasBtn.onClick.AddListener(ShowDrawingCanvasOnClick);

        if (HideDrawingCanvasBtn != null)
            HideDrawingCanvasBtn.onClick.AddListener(HideDrawingCanvasOnClick);

    }

    private void ShowDrawingCanvasOnClick()
    {
        if (simpleDrawingCanvasGO != null)
            simpleDrawingCanvasGO.SetActive(true);
    }

    private void HideDrawingCanvasOnClick()
    {
        if (simpleDrawingCanvasGO != null)
            simpleDrawingCanvasGO.SetActive(false);
    }

    void TakeOffOnClick()
    {
        //_communication.SendCommandTakeoff(0.5f);
    }

    void DisArmOnClick()
    {
        _communication.SendCommandArmDisarm((byte)0);
    }

    void ArmOnClick()
    {
        _communication.SendCommandArmDisarm((byte)1);
    }

    void LandOnClick()
    {
        _communication.SendCommandLand();
    }

    void ConnectOnClick()
    {
        _communication.StartUDP();
    }

    void DisconnectOnClick()
    {
        _communication.StopUDP();
    }

    void ManualOnClick()
    {
        telemetry.SetManualMode();
    }

    void AltctlOnClick()
    {
        telemetry.SetAltctlMode();
    }

    void PositionOnClick()
    {
        telemetry.SetPosctlMode();
    }

    void OffBoardOnClick()
    {
        telemetry.SetOffboardMode();
    }

    void LandModeOnClick()
    {
        _communication.SendCommandSetMode(4); // AUTO.LAND
    }

    void StartRecordOnClick()
    {
        _communication.RecordDataTrainingActive(true);
    }

    void StopRecordOnClick()
    {
        _communication.RecordDataTrainingActive(false);
    }

    void StartVxOnClick()
    {
        //telemetry.MissionOverrideVx = true;
        //_communication.VxOverrideActive(true);
    }

    void StopVxOnClick()
    {
        //telemetry.MissionOverrideVx = false;
        //_communication.VxOverrideActive(false);
    }

    //void StartTrainingOnClick()
    //{
    //    telemetry.MissionTrainingEnable = true;
    //}

    void VelocityModeOnClick()
    {
        telemetry.VelActive = true;
        _communication.SendImmediateControlOnModeSwitch();
    }

    void ServoControlOnClick()
    {
        telemetry.AutoDrawActive = true;
    }

    void StopServoControlOnClick()
    {
        telemetry.AutoDrawActive = false;
    }

    private void UpdateUI() 
    {
        string mode = telemetry.TeleCurMode.ToString();
        uavStateCanvasText.text = (mode != "-1") ? mode : "CONNECTED";
        armDisarmCanvasText.text = telemetry.TeleArmed ? "ARMED" : "DISARM";
        connectionStateCanvasText.text = telemetry.TeleConnected.ToString();
    }
    void Update()
    {
        UpdateUI();
    }
}
