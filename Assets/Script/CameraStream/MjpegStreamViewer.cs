using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows an MJPEG HTTP stream on a mesh or a UI RawImage.
///
/// web_video_server serves each ROS image topic as multipart/x-mixed-replace,
/// which is just a run of JPEG frames. Decoding that needs far less than a
/// browser engine — so unlike the Vuplex WebView screens, this works in the
/// Unity Editor as well as on the phone, and starts in a fraction of a second.
///
/// Attach to the screen object (it will use that object's Renderer), or to any
/// object and assign targetRenderer / targetRawImage explicitly.
/// </summary>
public class MjpegStreamViewer : MonoBehaviour
{
    [Header("Nguồn")]
    [Tooltip("Ví dụ: http://192.168.50.1:8080/stream?topic=/main_camera/image_raw")]
    public string url = "http://192.168.50.1:8080/stream?topic=/main_camera/image_raw";

    [Tooltip("Tự kết nối khi object được bật")]
    public bool playOnEnable = true;

    [Header("Đích hiển thị (để trống sẽ tự lấy Renderer của object này)")]
    public Renderer targetRenderer;
    public RawImage targetRawImage;

    [Header("Kết nối")]
    [Tooltip("Giây chờ trước khi thử lại khi mất kết nối")]
    [Min(0.5f)] public float reconnectDelay = 2f;

    [Tooltip("Giây chờ tối đa khi mở kết nối")]
    [Min(1f)] public float timeout = 5f;

    [Header("Gỡ lỗi")]
    public bool logStatus = true;

    // ── Trạng thái dùng chung giữa luồng nền và luồng chính ─────────────
    private readonly object _lock = new object();
    private byte[] _pendingFrame;          // khung mới nhất chưa vẽ
    private Thread _worker;
    private volatile bool _running;
    private volatile string _lastError;

    private Texture2D _texture;
    private int _framesReceived;
    private float _fpsTimer;
    private int _fpsCount;

    public bool IsStreaming => _running;
    public int FramesReceived => _framesReceived;

    private void OnEnable()
    {
        if (playOnEnable) StartStream();
    }

    private void OnDisable()
    {
        StopStream();
    }

    private void OnDestroy()
    {
        StopStream();
        if (_texture != null) Destroy(_texture);
    }

    /// <summary>Bắt đầu nhận luồng. Gọi lại được sau khi đổi url.</summary>
    public void StartStream()
    {
        if (_running) return;
        if (string.IsNullOrEmpty(url))
        {
            Debug.LogWarning($"[MJPEG] {name}: chưa đặt url");
            return;
        }

        if (targetRenderer == null && targetRawImage == null)
            targetRenderer = GetComponent<Renderer>();

        _running = true;
        _worker = new Thread(ReadLoop) { IsBackground = true, Name = "MJPEG " + name };
        _worker.Start();

        if (logStatus) Debug.Log($"[MJPEG] {name}: đang kết nối {url}");
    }

    public void StopStream()
    {
        _running = false;
        if (_worker != null && _worker.IsAlive) _worker.Join(500);
        _worker = null;
    }

    /// <summary>Đổi nguồn khi đang chạy (ví dụ chuyển giữa hai camera).</summary>
    public void SetUrl(string newUrl)
    {
        StopStream();
        url = newUrl;
        StartStream();
    }

    // ── Luồng nền: đọc HTTP, tách từng khung JPEG ───────────────────────

    private void ReadLoop()
    {
        while (_running)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = Mathf.RoundToInt(timeout * 1000f);
                request.ReadWriteTimeout = Mathf.RoundToInt(timeout * 1000f);
                request.AllowReadStreamBuffering = false;
                request.KeepAlive = true;

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                {
                    _lastError = null;
                    ReadFrames(stream);
                }
            }
            catch (Exception e)
            {
                if (!_running) return;
                _lastError = e.Message;
            }

            if (!_running) return;
            Thread.Sleep(Mathf.RoundToInt(reconnectDelay * 1000f));
        }
    }

    /// <summary>
    /// Máy trạng thái tìm cặp dấu hiệu JPEG: FF D8 mở khung, FF D9 đóng khung.
    /// Cách này không phụ thuộc vào boundary của multipart, nên chịu được mọi
    /// biến thể header mà web_video_server sinh ra.
    /// </summary>
    private void ReadFrames(Stream stream)
    {
        var buffer = new byte[16384];
        var frame = new MemoryStream(131072);
        bool inFrame = false;
        int prev = -1;

        while (_running)
        {
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read <= 0) return;               // máy chủ đóng kết nối

            for (int i = 0; i < read; i++)
            {
                int cur = buffer[i];

                if (!inFrame)
                {
                    if (prev == 0xFF && cur == 0xD8)
                    {
                        inFrame = true;
                        frame.SetLength(0);
                        frame.WriteByte(0xFF);
                        frame.WriteByte(0xD8);
                    }
                }
                else
                {
                    frame.WriteByte((byte)cur);

                    if (prev == 0xFF && cur == 0xD9)
                    {
                        lock (_lock) { _pendingFrame = frame.ToArray(); }
                        inFrame = false;
                        if (frame.Length > 4_000_000) frame.Capacity = 131072;
                    }
                }

                prev = cur;
            }
        }
    }

    // ── Luồng chính: nạp khung vào texture ──────────────────────────────

    private void Update()
    {
        byte[] jpeg = null;
        lock (_lock)
        {
            if (_pendingFrame != null)
            {
                jpeg = _pendingFrame;
                _pendingFrame = null;       // chỉ vẽ khung mới nhất, bỏ khung cũ
            }
        }

        if (jpeg != null)
        {
            if (_texture == null)
            {
                _texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                _texture.wrapMode = TextureWrapMode.Clamp;
            }

            if (_texture.LoadImage(jpeg))
            {
                Apply(_texture);
                _framesReceived++;
                _fpsCount++;
            }
        }

        if (!logStatus) return;

        _fpsTimer += Time.deltaTime;
        if (_fpsTimer >= 5f)
        {
            if (_fpsCount > 0)
                Debug.Log($"[MJPEG] {name}: {_fpsCount / _fpsTimer:0.0} khung/giây");
            else
                Debug.LogWarning($"[MJPEG] {name}: chưa nhận được khung nào" +
                                 (string.IsNullOrEmpty(_lastError) ? "" : $" — {_lastError}"));
            _fpsTimer = 0f;
            _fpsCount = 0;
        }
    }

    private void Apply(Texture2D tex)
    {
        if (targetRawImage != null) targetRawImage.texture = tex;

        if (targetRenderer != null)
        {
            var mat = targetRenderer.material;      // bản sao riêng, không đụng shared material
            if (mat.HasProperty("_MainTex")) mat.mainTexture = tex;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        }
    }
}
