using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CameraCanvas : MonoBehaviour
{
    [SerializeField]
    private GameObject _telemetry;

    [SerializeField]
    private Text VxCanvas;
    [SerializeField]
    private Text VyCanvas;
    [SerializeField]
    private Text VzCanvas;
    [SerializeField]
    private Text YawRateCanvas;

    private Telemetry telemetry;

    // Start is called before the first frame update
    void Awake()
    {
        _telemetry = GameObject.FindGameObjectWithTag("Telemetry");
        telemetry = _telemetry.GetComponent<Telemetry>();

    }

    // Update is called once per frame
    void Update()
    {
        //VxCanvas.text = telemetry.TeleVxControl.ToString();
        //YawRateCanvas.text = telemetry.TeleYawRateControl.ToString();
        //VyCanvas.text = telemetry.TeleVyControl.ToString();
        //VzCanvas.text = telemetry.TeleVzControl.ToString();

        VxCanvas.text = telemetry.VelX.ToString();
        VyCanvas.text = telemetry.VelY.ToString();
        VzCanvas.text = telemetry.VelZ.ToString();
        YawRateCanvas.text = telemetry.VelYawRate.ToString();
    }
}
