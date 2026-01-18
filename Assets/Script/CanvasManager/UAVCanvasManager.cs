using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UAVCanvasManager : MonoBehaviour
{
    [SerializeField] private Text BatteryCanvasText;

    [SerializeField] private Text AltitudeCanvasText;
    [SerializeField] private Text LatitudeCanvasText;
    [SerializeField] private Text LongitudeCanvasText;

    [SerializeField] private Text PosXCanvasText;
    [SerializeField] private Text PosYCanvasText;
    [SerializeField] private Text PosZCanvasText;

    [SerializeField] private Text VxCanvasText;
    [SerializeField] private Text VyCanvasText;
    [SerializeField] private Text VzCanvasText;

    [SerializeField] private Text RollCanvasText;
    [SerializeField] private Text PitchCanvasText;
    [SerializeField] private Text YawCanvasText;

    [SerializeField] private Text ShortDistanceText;
    [SerializeField] private Text LongDistanceText;

    [SerializeField] private Text PenPositionX;
    [SerializeField] private Text PenPositionY;
    [SerializeField] private Text PenPositionZ;

    [SerializeField] private Text Servo1;
    [SerializeField] private Text Servo2;
    [SerializeField] private Text Servo3;
    [SerializeField] private Text Servo4;
    [SerializeField] private Text Servo5;

    [SerializeField] private Telemetry telemetry;
    
    void Start()
    {
        // Find Communication GameObject by tag
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
        UpdateUI();
    }

    void UpdateUI()
    {
        BatteryCanvasText.text = telemetry.TeleBattery.ToString() + "%";
        AltitudeCanvasText.text = telemetry.TeleAltitude >= 0 ? telemetry.TeleAltitude.ToString() : "0";
        LatitudeCanvasText.text = telemetry.TeleLatitude.ToString();
        LongitudeCanvasText.text = telemetry.TeleLongitude.ToString();

        PosXCanvasText.text = telemetry.PosX.ToString();
        PosYCanvasText.text = telemetry.PosY.ToString();
        PosZCanvasText.text = telemetry.PosZ.ToString();

        VxCanvasText.text = telemetry.TeleVx.ToString();
        VyCanvasText.text = telemetry.TeleVy.ToString();
        VzCanvasText.text = telemetry.TeleVz.ToString();

        RollCanvasText.text = telemetry.Roll.ToString() + "°";
        PitchCanvasText.text = telemetry.Pitch.ToString() + "°";
        YawCanvasText.text = telemetry.Yaw.ToString() + "°";

        ShortDistanceText.text = telemetry.ShortDistance.ToString() + " m";
        LongDistanceText.text = telemetry.LongDistance.ToString() + " m";

        PenPositionX.text = telemetry.PositionPenX.ToString();
        PenPositionY.text = telemetry.PositionPenY.ToString();
        PenPositionZ.text = telemetry.PositionPenZ.ToString();
    }
}
