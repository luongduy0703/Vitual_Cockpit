using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class UdpControlSender : MonoBehaviour
{
    [Header("UI")]
    public InputField ipField;     // Pi IP or hostname
    public InputField portField;   // Port (default: 5600)
    public Toggle enableToggle;    // enable/disable control
    public Toggle armToggle;       // ARM/DISARM
    public Slider[] pwmSliders;    // 5 sliders for 5 motors, PWM [1100..1900]
    public Button btnForward, btnBackward, btnRight, btnLeft, btnUp, btnDown;
    public Button btnConnect, btnDisconnect;
    public Text statusText;
    public Text lastJsonText;

    [Header("Defaults")]
    public string defaultIp = "192.168.2.2";
    public int defaultPort = 5600;
    public int minPwm = 1100;
    public int neutralPwm = 1500;
    public int maxPwm = 1900;

    [Header("Send")]
    public float sendRateHz = 20f;
    public bool autoConnectOnStart = true;

    private UdpClient client;
    private IPEndPoint endPoint;
    private bool connected = false;
    private float period;

    private string currentIp;
    private int currentPort;

    [Serializable]
    private class Payload
    {
        public bool enable;
        public bool arm;
        public int[] pwm;      // per-motor PWM array
        public string command; // "forward","backward","right","left","up","down"
    }

    void Start()
    {
        Application.runInBackground = true;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        period = 1f / Mathf.Max(1f, sendRateHz);

        if (btnConnect) btnConnect.onClick.AddListener(OnConnect);
        if (btnDisconnect) btnDisconnect.onClick.AddListener(OnDisconnect);

        if (pwmSliders != null)
        {
            foreach (var s in pwmSliders)
            {
                if (s == null) continue;
                s.minValue = minPwm;
                s.maxValue = maxPwm;
                s.value = neutralPwm;

                var snap = s.GetComponent<SnapBackSlider>();
                if (snap == null) snap = s.gameObject.AddComponent<SnapBackSlider>();
                snap.slider = s;
                snap.neutral = neutralPwm;
            }
        }

        if (btnForward) btnForward.onClick.AddListener(() => SendCommandOnce("forward"));
        if (btnBackward) btnBackward.onClick.AddListener(() => SendCommandOnce("backward"));
        if (btnRight) btnRight.onClick.AddListener(() => SendCommandOnce("right"));
        if (btnLeft) btnLeft.onClick.AddListener(() => SendCommandOnce("left"));
        if (btnUp) btnUp.onClick.AddListener(() => SendCommandOnce("up"));
        if (btnDown) btnDown.onClick.AddListener(() => SendCommandOnce("down"));

        if (autoConnectOnStart) TryConnect();
        UpdateStatus("Idle");
    }

    public void OnConnect() => TryConnect();
    public void OnDisconnect() => Disconnect();

    private bool TryResolveEndpoint(string hostOrIp, int port, out IPEndPoint ep, out string error)
    {
        ep = null;
        error = null;

        if (string.IsNullOrWhiteSpace(hostOrIp))
            hostOrIp = defaultIp;

        if (port <= 0 || port > 65535)
            port = defaultPort;

        try
        {
            if (IPAddress.TryParse(hostOrIp.Trim(), out var ipAddr))
            {
                ep = new IPEndPoint(ipAddr, port);
                return true;
            }

            var addrs = Dns.GetHostAddresses(hostOrIp.Trim());
            foreach (var a in addrs)
            {
                if (a.AddressFamily == AddressFamily.InterNetwork) // Prefer IPv4
                {
                    ep = new IPEndPoint(a, port);
                    return true;
                }
            }

            if (addrs.Length > 0)
            {
                ep = new IPEndPoint(addrs[0], port);
                return true;
            }

            error = $"DNS resolve failed for {hostOrIp}";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void TryConnect()
    {
        string uiIp = (ipField != null && !string.IsNullOrWhiteSpace(ipField.text)) ? ipField.text.Trim() : defaultIp;
        int uiPort = defaultPort;
        if (portField != null && int.TryParse(portField.text.Trim(), out int p)) uiPort = p;

        if (!TryResolveEndpoint(uiIp, uiPort, out var ep, out var err))
        {
            UpdateStatus($"Connect error: {err}");
            connected = false;
            return;
        }

        bool destinationChanged = (!connected) || (currentIp != ep.Address.ToString()) || (currentPort != ep.Port);
        if (!destinationChanged)
        {
            UpdateStatus($"Already connected to {currentIp}:{currentPort}");
            return;
        }

        Disconnect();

        try
        {
            client = new UdpClient();
            client.Client.SendTimeout = 1000;

            endPoint = ep;
            currentIp = endPoint.Address.ToString();
            currentPort = endPoint.Port;

            connected = true;
            UpdateStatus($"Connected to {currentIp}:{currentPort}");
            InvokeRepeating(nameof(SendContinuous), period, period);
        }
        catch (Exception ex)
        {
            UpdateStatus("Connect error: " + ex.Message);
            connected = false;
        }
    }

    private void SendContinuous()
    {
        if (!connected || client == null || endPoint == null) return;

        var pl = new Payload
        {
            enable = enableToggle != null ? enableToggle.isOn : true,
            arm = armToggle != null ? armToggle.isOn : false
        };

        int n = pwmSliders != null ? pwmSliders.Length : 0;
        pl.pwm = new int[n];
        for (int i = 0; i < n; i++)
        {
            var s = pwmSliders[i];
            int val = s != null ? Mathf.RoundToInt(s.value) : neutralPwm;
            val = Mathf.Clamp(val, minPwm, maxPwm);
            pl.pwm[i] = val;
        }

        pl.command = null;

        SendJson(pl);
    }

    private void SendCommandOnce(string cmd)
    {
        if (!connected || client == null || endPoint == null) return;

        var pl = new Payload
        {
            enable = enableToggle != null ? enableToggle.isOn : true,
            arm = armToggle != null ? armToggle.isOn : false,
            pwm = null,
            command = cmd
        };

        SendJson(pl);
    }

    private void SendJson(Payload pl)
    {
        string json = JsonUtility.ToJson(pl);
        byte[] data = Encoding.UTF8.GetBytes(json);

        try
        {
            client.Send(data, data.Length, endPoint);
            if (lastJsonText) lastJsonText.text = $"Last [{endPoint.Address}:{endPoint.Port}]: {json}";
        }
        catch (Exception ex)
        {
            UpdateStatus("Send error: " + ex.Message);
        }
    }

    private void UpdateStatus(string s)
    {
        if (statusText) statusText.text = $"Status: {s}";
        Debug.Log(s);
    }

    private void Disconnect()
    {
        CancelInvoke(nameof(SendContinuous));
        if (client != null)
        {
            try { client.Close(); } catch { }
            client = null;
        }
        connected = false;
        UpdateStatus("Disconnected");
    }

    void OnDestroy() => Disconnect();
}