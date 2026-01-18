using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using ViveHandTracking;
using System.Threading.Tasks;

public enum HitProjectionMode
{
    Orthographic,
}

public enum RayLocalAxis
{
    PlusX, MinusX,
    PlusY, MinusY,
    PlusZ, MinusZ
}

[RequireComponent(typeof(MeshFilter))]
public class SimpleDrawingCanvas : MonoBehaviour
{
    [Header("Vive Hand Tracking (Right Hand)")]
    public Transform rightHandTransform;

    [Tooltip("Only draw with Right Hand use Point gesture")]
    public bool requireRightHandPointGesture = true;

    [Header("Hit Projection")]
    public HitProjectionMode projectionMode = HitProjectionMode.Orthographic;

    // For long-distance Raycast/Hybrid, use the Transform's local axis as the ray direction.
    public RayLocalAxis viveRayAxis = RayLocalAxis.PlusZ;

    [Tooltip("Force Touch (|Distance| <= contactDistance) draw acceptable")]
    public bool drawRequiresTouch = true;

    // ————— UI —————
    [Header("UI (World/Screen Space)")]
    public Text coordinatesText;
    public Text servoAnglesText;

    // ————— Drawing use LineRenderer —————
    [Header("Drawing (LineRenderer)")]
    public Material lineMaterial;
    [Min(0f)] public float lineWidth = 0.01f;

    [Tooltip("Minimum distance between two consecutive points on a stroke (m)")]
    [Min(0f)] public float minPointDistance = 0.005f;

    [Tooltip("Small offset from the surface to avoid z-fighting (m)")]
    [Min(0f)] public float surfaceOffset = 0.0005f;

    [Tooltip("Coordinate display distance (m) - near canvas")]
    [Min(0f)] public float drawingDistance = 0.1f;

    [Tooltip("Touch threshold (m) when drawRequiresTouch = true")]
    [Min(0f)] public float contactDistance = 0.005f;

    [Header("strokes (optional)")]
    public Transform strokesParent;

    // ————— Stabilize / Limit Brush Stroke Movement —————
    [Header("Brush Stabilization")]
    [Tooltip("Enable low-pass filter to reduce drawing point jitter")]
    public bool enableSmoothing = true;

    [Tooltip("Smoothing filter time constant (seconds)")]
    [Min(0f)] public float smoothingTime = 0.05f;

    [Tooltip("Giới hạn bước dịch chuyển theo mặt phẳng mỗi frame")]
    public bool clampMovePerFrame = true;

    [Tooltip("Maximum planar movement per frame")]
    [Min(0f)] public float maxMovePerFrameOnPlane = 0.05f; // 5cm

    // ————— Input: Mouse/Touch and External —————
    [Header("Mouse/Touch (Fallback)")]
    public bool allowMouseFallback = false;

    [Header("External Input (UI/ROS)")]
    [Tooltip("Use input (x,y,penDown) set")]
    public bool useExternalInput = false;
    [Range(0f, 1f)] public float extX = 0.5f;
    [Range(0f, 1f)] public float extY = 0.5f;
    public bool extPenDown = false;

    // ————— Auto Draw (Square) —————
    [Header("Auto Draw (Square)")]
    public bool autoDrawOnStart = false;
    public bool autoLoop = true;

    [Tooltip("Auto-point interval (seconds)")]
    [Min(0.01f)] public float autoPointInterval = 0.2f;

    [Tooltip("Side length of the square in UV coordinates [0..1]")]
    [Range(0f, 1f)] public float autoSquareSideUV = 0.25f;

    [Tooltip("Points per side (>=2)")]
    [Min(2)] public int autoPointsPerSide = 20;

    // ————— Robot Kinematics & Mapping (cm) —————
    [Header("Robot Kinematics (cm)")]
    [Tooltip("Length Shoulder - Elbow")]
    public float L1_cm = 6.0f;

    [Tooltip("Length Elbow - Wrist")]
    public float L2_cm = 5.5f;

    [Tooltip("Length Wrist - Pen Tip")]
    public float L3_cm = 5.5f;

    [Tooltip("Plane M Height (cm)")]
    public float zPlane_cm = 15f;

    [Header("Drawing Area (cm)")]
    [Tooltip("X: [-xHalfSpan_cm, +xHalfSpan_cm]")]
    public float xHalfSpan_cm = 6f;

    [Tooltip("Y: [-yHalfSpan_cm, +yHalfSpan_cm]")]
    public float yHalfSpan_cm = 6f;

    [Header("IK Options")]
    public bool elbowUp = false;

    // ————— Internal state —————
    private Mesh _mesh;
    private Vector3 _meshMinLocal;
    private Vector3 _meshSizeLocal;
    private Camera _mainCam;

    private LineRenderer _currentLine;
    private readonly List<Vector3> _currentPoints = new List<Vector3>();

    // Presence hold to avoid interruption when hand drops frames.
    private float _vivePresenceTimer = 0f;
    private const float VivePresenceHold = 0.15f; // seconds
    private bool _lastRightHandIsPoint = false;
    private bool _usingVive = false;
    private bool _usingMouse = false;
    private bool _usingExternal = false;

    // Smoothing/clamp hit point
    private bool _hasSmoothedPoint = false;
    private Vector3 _smoothedPoint = Vector3.zero;
    private Vector3 _prevDrawPoint = Vector3.zero;
    private bool _hasPrevDrawPoint = false;

    // IK publish timer
    private float _sendTimer = 0f;
    private const float SendInterval = 0.5f; // Limit send command every 200ms

    // Save UV & last worldPoint for UI & draw
    private Vector2 _lastUV01 = new Vector2(0.5f, 0.5f);
    private Vector3 _lastWorldPoint = Vector3.zero;
    private bool _hasWorldPoint = false;

    [SerializeField] private Telemetry telemetry;
    [SerializeField] private Communication communication;

    // ——— Auto Draw internal state ———
    private bool _autoActive = false;
    private float _autoTimer = 0f;
    private int _autoIndex = 0;
    private Vector2 _autoCurrentUV = new Vector2(0.5f, 0.5f);
    private readonly List<Vector2> _autoPathUV = new List<Vector2>();

    // --- Draw Area ---
    private float _drawArea = 0f;

    private void Start()
    {
        if (communication == null)
        {
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

        if (autoDrawOnStart)
        {
            StartAutoDraw();
        }
    }

    // ————— Unity —————
    private void Awake()
    {
        _mainCam = Camera.main;

        var mf = GetComponent<MeshFilter>();
        if (mf != null)
        {
            _mesh = mf.sharedMesh;
            if (_mesh != null)
            {
                var b = _mesh.bounds;
                _meshMinLocal = b.min;
                _meshSizeLocal = b.size;
            }
        }

        // Fallback bounds for Plane default 10x10 local
        if (_mesh == null || _meshSizeLocal.x <= 1e-6f || _meshSizeLocal.z <= 1e-6f)
        {
            _meshMinLocal = new Vector3(-5f, 0f, -5f);
            _meshSizeLocal = new Vector3(10f, 0f, 10f);
        }

        if (coordinatesText) coordinatesText.text = "X: 0.00, Y: 0.00";
        if (servoAnglesText) servoAnglesText.text = "θ0: 0.0°, θ1: 0.0°, θ2: 0.0°, θ3: 0.0°";
    }

    private void Update()
    {
        // Get zPlane from telemetry
        //zPlane_cm = telemetry.LongDistance * 100f + 2f; // 3cm offset range sensor

        // Calculate draw area
        float xSpan, ySpan;
        CalculateWorkspace(zPlane_cm, out xSpan, out ySpan);
        _drawArea = ySpan;

        // Set start draw from Button
        _autoActive = telemetry.AutoDrawActive;
        // Collect input & compute hit/UV points (priority: Auto - External - Vive - Mouse)
        InputSample sample = GetInputSample();

        // Point Stabilization (Smoothing + Clamp)
        Vector3 drawPointWorld = sample.worldHit;
        if (sample.hasHit)
        {
            drawPointWorld = StabilizePoint(sample.worldHit);
            _lastWorldPoint = drawPointWorld;
            _lastUV01 = sample.uv;
            _hasWorldPoint = true;
        }
        else
        {
            _hasSmoothedPoint = false;
            _hasPrevDrawPoint = false;
        }

        // Update coordinate UI when near canvas
        bool nearEnough = sample.hasHit && sample.inside && Mathf.Abs(sample.signedDistance) < drawingDistance;
        bool showCoord = _usingExternal || nearEnough || _autoActive;
        UpdateCoordinateUI(showCoord, _lastUV01);

        // penDown logic
        bool isDrawing = sample.isDrawing;

        // LineRenderer
        if (isDrawing && sample.hasHit && sample.inside)
        {
            Vector3 drawPoint = drawPointWorld + transform.up * surfaceOffset;
            if (_currentLine == null) BeginNewStroke(drawPoint);
            else TryAddPoint(drawPoint);
        }
        else
        {
            EndStrokeIfAny();
        }

        // Mapping (x,y) -> robot (cm) + Z theo penDown (X,Y symmetric ±span)
        float Xr_cm, Yr_cm, Zr_cm;
        MapToRobotSpace3D(_lastUV01, isDrawing, out Xr_cm, out Yr_cm, out Zr_cm);

        // IK 3D with Base (θ0) + 2-link (θ1,θ2) + Wrist (θ3)
        float degBase, degShoulder, degElbow, degWrist;
        SolveIK3DWithBase(Xr_cm, Yr_cm, Zr_cm, elbowUp, out degBase, out degShoulder, out degElbow, out degWrist, isDrawing);

        if (servoAnglesText)
        {
            servoAnglesText.text = 
                $"θ0: {degBase:0.0}°, θ1: {degShoulder:0.0}°, θ2: {degElbow:0.0}°, θ3: {degWrist:0.0}° | " +
                $"penDown: {isDrawing} | Target(cm): ({Xr_cm:0.0}, {Yr_cm:0.0}, {Zr_cm:0.0}), {_drawArea: 0.0}";
        }

        telemetry.PositionPenX = telemetry.PosX + 0.2f + Zr_cm * 0.01f;
        telemetry.PositionPenY = telemetry.PosY + 0f - Xr_cm * 0.01f;
        telemetry.PositionPenZ = telemetry.PosZ - 0.05f + Yr_cm * 0.01f;

        float dt = Time.deltaTime;
        _sendTimer += dt;

        // Send command only when drawing AND after a sufficient delay (200ms)
        if (isDrawing && _sendTimer >= SendInterval)
        {
            _sendTimer = 0f; // Reset counter

            if (telemetry != null)
            {
                telemetry.BaseSV = degBase;
                telemetry.ShoulderSV = degShoulder;
                telemetry.ElbowSV = degElbow;
                telemetry.WristSV = degWrist;

                communication.HandleServoControl();
            }
        }

        // Presence hold to reduce discontinuity
        if (_vivePresenceTimer > 0f) _vivePresenceTimer -= dt;
        else _usingVive = false;
    }

    // ————— Input —————
    private struct InputSample
    {
        public bool hasHit;
        public Vector3 worldHit;
        public Vector2 uv;
        public float signedDistance;
        public bool inside;
        public bool isDrawing;
    }

    private InputSample GetInputSample()
    {
        InputSample s = new InputSample
        {
            hasHit = false,
            worldHit = Vector3.zero,
            uv = _lastUV01,
            signedDistance = 0f,
            inside = false,
            isDrawing = false
        };

        _usingExternal = useExternalInput;
        _usingMouse = false;

        // 0) AUTO DRAW
        if (_autoActive)
        {
            // Advance points on timer tick (every autoPointInterval seconds)
            _autoTimer += Time.deltaTime;
            if (_autoTimer >= autoPointInterval)
            {
                int steps = Mathf.FloorToInt(_autoTimer / Mathf.Max(0.0001f, autoPointInterval));
                _autoTimer -= steps * autoPointInterval;

                for (int i = 0; i < steps; i++)
                {
                    _autoIndex++;
                    if (_autoIndex >= _autoPathUV.Count)
                    {
                        if (autoLoop) _autoIndex = 0;
                        else
                        {
                            _autoActive = false;
                            break;
                        }
                    }
                }

                if (_autoActive && _autoIndex >= 0 && _autoIndex < _autoPathUV.Count)
                {
                    _autoCurrentUV = _autoPathUV[_autoIndex];
                }
            }

            s.uv = _autoCurrentUV;
            s.worldHit = UVToWorldOnCanvas(s.uv);
            s.hasHit = true;
            s.inside = true;
            s.signedDistance = 0f;
            s.isDrawing = true;

            return s;
        }

        // 1) External
        if (useExternalInput)
        {
            s.uv = new Vector2(Mathf.Clamp01(extX), Mathf.Clamp01(extY));
            s.worldHit = UVToWorldOnCanvas(s.uv);
            s.hasHit = true;
            s.inside = true;
            s.signedDistance = 0f; // assume it is on the plane
            s.isDrawing = extPenDown;
            return s;
        }

        // 2) Vive
        bool haveRightTransform = (rightHandTransform != null);
        GestureResult right = GestureProvider.RightHand; // null if no hand detected
        if (right != null)
        {
            _vivePresenceTimer = VivePresenceHold;
            _usingVive = haveRightTransform;
            _lastRightHandIsPoint = ((GetFlag(right) & HandFlag.Point) != 0);
        }

        if (_usingVive && haveRightTransform && _vivePresenceTimer > 0f)
        {
            Vector3 planePoint = transform.position;
            Vector3 normal = transform.up;

            // Signed distance from palm to plane (along the normal)
            Vector3 v = rightHandTransform.position - planePoint;
            float signedDist = Vector3.Dot(v, normal);

            // Hit method
            HitProjectionMode modeToUse = projectionMode;

            Vector3 hitWorld;
            bool hitOK = false;

            if (modeToUse == HitProjectionMode.Orthographic)
            {
                hitWorld = rightHandTransform.position - signedDist * normal;
                hitOK = true;
            }
            else
            {
                Vector3 dir = ResolveLocalAxis(rightHandTransform, viveRayAxis);
                Ray ray = new Ray(rightHandTransform.position, dir);
                Plane plane = new Plane(normal, planePoint);
                hitWorld = Vector3.zero;
            }

            if (hitOK)
            {
                s.hasHit = true;
                s.worldHit = hitWorld;
                s.signedDistance = signedDist;

                // UV from hitWorld
                Vector3 localPoint = transform.InverseTransformPoint(hitWorld);
                float u = (_meshSizeLocal.x > 1e-6f) ? (localPoint.x - _meshMinLocal.x) / _meshSizeLocal.x : 0f;
                float v01 = (_meshSizeLocal.z > 1e-6f) ? (localPoint.z - _meshMinLocal.z) / _meshSizeLocal.z : 0f;
                s.uv = new Vector2(u, v01);
                s.inside = (u >= 0f && u <= 1f && v01 >= 0f && v01 <= 1f);

                // Rule draw: gesture Point + (touch if required)
                bool contactOK = !drawRequiresTouch || Mathf.Abs(signedDist) <= contactDistance;
                bool gestureOK = !requireRightHandPointGesture || _lastRightHandIsPoint;
                s.isDrawing = s.inside && contactOK && gestureOK;
                return s;
            }
        }

        // 3) Mouse/Touch fallback
        if (allowMouseFallback && _mainCam != null)
        {
            // Priority to Touch
            if (Input.touchCount > 0)
            {
                Touch t = Input.GetTouch(0);
                Ray ray = _mainCam.ScreenPointToRay(t.position);
                Plane plane = new Plane(transform.up, transform.position);
                if (plane.Raycast(ray, out float enter))
                {
                    _usingMouse = true;
                    s.hasHit = true;
                    s.worldHit = ray.GetPoint(enter);
                    s.signedDistance = 0f;

                    Vector3 localPoint = transform.InverseTransformPoint(s.worldHit);
                    float u = (_meshSizeLocal.x > 1e-6f) ? (localPoint.x - _meshMinLocal.x) / _meshSizeLocal.x : 0f;
                    float v01 = (_meshSizeLocal.z > 1e-6f) ? (localPoint.z - _meshMinLocal.z) / _meshSizeLocal.z : 0f;
                    s.uv = new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v01));
                    s.inside = (s.uv.x >= 0f && s.uv.x <= 1f && s.uv.y >= 0f && s.uv.y <= 1f);

                    s.isDrawing = (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary);
                    return s;
                }
            }
            else
            {
                Ray mray = _mainCam.ScreenPointToRay(Input.mousePosition);
                Plane plane = new Plane(transform.up, transform.position);
                if (plane.Raycast(mray, out float enter))
                {
                    _usingMouse = true;
                    s.hasHit = true;
                    s.worldHit = mray.GetPoint(enter);
                    s.signedDistance = 0f;

                    Vector3 localPoint = transform.InverseTransformPoint(s.worldHit);
                    float u = (_meshSizeLocal.x > 1e-6f) ? (localPoint.x - _meshMinLocal.x) / _meshSizeLocal.x : 0f;
                    float v01 = (_meshSizeLocal.z > 1e-6f) ? (localPoint.z - _meshMinLocal.z) / _meshSizeLocal.z : 0f;
                    s.uv = new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v01));
                    s.inside = (s.uv.x >= 0f && s.uv.x <= 1f && s.uv.y >= 0f && s.uv.y <= 1f);

                    s.isDrawing = Input.GetMouseButton(0);
                    return s;
                }
            }
        }

        // if no source is available, keep old UV, do not draw
        return s;
    }

    // ————— Utilities —————

    public void CalculateWorkspace(float zDistanceCm, out float xHalfSpan, out float yHalfSpan)
    {
        float r_max_total = L1_cm + L2_cm + L3_cm;
        float r_max_total_sq = r_max_total * r_max_total;
        float z_dist_sq = zDistanceCm * zDistanceCm;

        if (z_dist_sq >= r_max_total_sq)
        {
            xHalfSpan = 0f;
        }
        else
        {
            xHalfSpan = Mathf.Sqrt(r_max_total_sq - z_dist_sq);
        }

        float r_max_L1_L2 = L1_cm + L2_cm; // 11.5 cm
        float r_max_L1_L2_sq = r_max_L1_L2 * r_max_L1_L2;

        float r_wrist_at_X_zero = zDistanceCm - L3_cm;
        float r_wrist_sq = r_wrist_at_X_zero * r_wrist_at_X_zero;

        if (r_wrist_sq >= r_max_L1_L2_sq)
        {
            yHalfSpan = 0f;
        }
        else
        {
            yHalfSpan = Mathf.Sqrt(r_max_L1_L2_sq - r_wrist_sq);
        }
    }

    private Vector3 StabilizePoint(Vector3 current)
    {
        // Low-pass filter
        if (enableSmoothing)
        {
            if (!_hasSmoothedPoint)
            {
                _smoothedPoint = current;
                _hasSmoothedPoint = true;
            }
            else
            {
                float dt = Mathf.Max(Time.deltaTime, 1e-5f);
                float alpha = 1f - Mathf.Exp(-dt / Mathf.Max(smoothingTime, 1e-5f));
                _smoothedPoint = Vector3.Lerp(_smoothedPoint, current, alpha);
            }
        }
        else
        {
            _smoothedPoint = current;
            _hasSmoothedPoint = true;
        }

        // Clamp the movement step along the plane
        if (clampMovePerFrame && _hasPrevDrawPoint)
        {
            Vector3 normal = transform.up;
            Vector3 deltaPlane = Vector3.ProjectOnPlane(_smoothedPoint - _prevDrawPoint, normal);
            float d = deltaPlane.magnitude;
            if (d > maxMovePerFrameOnPlane && d > 1e-6f)
            {
                deltaPlane = deltaPlane * (maxMovePerFrameOnPlane / d);
                _smoothedPoint = _prevDrawPoint + deltaPlane + Vector3.Project(_smoothedPoint - _prevDrawPoint, normal);
            }
        }

        _prevDrawPoint = _smoothedPoint;
        _hasPrevDrawPoint = true;
        return _smoothedPoint;
    }

    private void UpdateCoordinateUI(bool show, Vector2 uv01)
    {
        if (!coordinatesText) return;
        coordinatesText.enabled = show;
        if (show) coordinatesText.text = $"X: {uv01.x:0.00}, Y: {uv01.y:0.00}";
    }

    private void BeginNewStroke(Vector3 startPoint)
    {
        GameObject go = new GameObject("Stroke");
        Transform parent = strokesParent != null ? strokesParent : transform.parent;
        go.transform.SetParent(parent, worldPositionStays: true);

        _currentLine = go.AddComponent<LineRenderer>();
        _currentLine.positionCount = 0;
        _currentLine.useWorldSpace = true;
        _currentLine.material = lineMaterial;
        _currentLine.startWidth = lineWidth;
        _currentLine.endWidth = lineWidth;
        _currentLine.numCornerVertices = 4;
        _currentLine.numCapVertices = 4;
        _currentLine.textureMode = LineTextureMode.Stretch;

        _currentPoints.Clear();
        TryAddPoint(startPoint);
    }

    private void TryAddPoint(Vector3 worldPoint)
    {
        if (_currentLine == null) return;

        if (_currentPoints.Count == 0 || Vector3.Distance(_currentPoints[_currentPoints.Count - 1], worldPoint) >= minPointDistance)
        {
            _currentPoints.Add(worldPoint);
            _currentLine.positionCount = _currentPoints.Count;
            _currentLine.SetPositions(_currentPoints.ToArray());
        }
    }
    private void EndStrokeIfAny()
    {
        if (_currentLine != null)
        {
            _currentLine = null;
            _currentPoints.Clear();
        }
    }

    private Vector3 UVToWorldOnCanvas(Vector2 uv01)
    {
        // UV [0,1] -> local X/Z theo bounds mesh -> world
        float lx = _meshMinLocal.x + uv01.x * _meshSizeLocal.x;
        float lz = _meshMinLocal.z + uv01.y * _meshSizeLocal.z;
        Vector3 local = new Vector3(lx, 0f, lz);
        return transform.TransformPoint(local);
    }

    private Vector3 ResolveLocalAxis(Transform t, RayLocalAxis axis)
    {
        switch (axis)
        {
            case RayLocalAxis.PlusX: return t.right;
            case RayLocalAxis.MinusX: return -t.right;
            case RayLocalAxis.PlusY: return t.up;
            case RayLocalAxis.MinusY: return -t.up;
            case RayLocalAxis.PlusZ: return t.forward;
            case RayLocalAxis.MinusZ: return -t.forward;
        }
        return t.forward;
    }

    // Flag HandStateChecker to check Point
    private HandFlag GetFlag(GestureResult hand)
    {
        var flag = HandFlag.NoHand;
        if (hand != null)
        {
            // 2 << gesture: Unknown=2, Point=4, Fist=8, OK=16, Like=32, Five=64, Victory=128, ...
            flag = (HandFlag)(2 << (int)hand.gesture);
            if (hand.pinch.isPinching) flag |= HandFlag.Pinch;
        }
        return flag;
    }

    // ————— Mapping & IK —————
    // Map (x,y) in range [0,1] to (Xr,Yr) in range [-span,+span], with Zr = zPlane
    private void MapToRobotSpace3D(Vector2 uv01, bool penDown, out float Xr_cm, out float Yr_cm, out float Zr_cm)
    {
        //Xr_cm = (2f * uv01.x - 1f) * xHalfSpan_cm;
        //Yr_cm = (2f * uv01.y - 1f) * yHalfSpan_cm;

        // Calculate draw area map to 3D
        Xr_cm = (2f * uv01.x - 1f) * _drawArea;
        Yr_cm = (2f * uv01.y - 1f) * _drawArea;
        Zr_cm = zPlane_cm;
    }

    // IK 3D with Base (J0) + 2-link (J1,J2) + Wrist (J3)
    private void SolveIK3DWithBase(float Xr_cm, float Yr_cm, float Zr_cm, bool elbowUpMode,
                                   out float degBase, out float degShoulder, out float degElbow, out float degWrist, bool isDrawing)
    {
        // === JOINT J0 (BASE, ROTATES ABOUT Y-AXIS) ===

        // theta0: the base joint's rotation angle
        float theta0 = Mathf.Atan2(Xr_cm, Zr_cm); // Radian

        // === CALCULATE TARGET FOR 2-LINK (SHOULDER & ELBOW) ARM ===
        // After the base rotates, J1 and J2 operate in a 2D plane (r, y)

        // r_target is the horizontal reach distance (within the XZ plane)
        float r_target = Mathf.Sqrt(Xr_cm * Xr_cm + Zr_cm * Zr_cm);

        // y_target is the vertical reach distance
        float y_target = Yr_cm;

        // Solve IK for the J2 joint (elbow joint)
        // The goal for link lengths (L1, L2) is to reach the target (r_wrist, y_wrist)
        float r_wrist = r_target - L3_cm;
        float y_wrist = y_target; // assume J1 (shoulder) start with y=0

        // === 2-LINK IK SOLUTION (J1 - SHOULDER, J2 - ELBOW) ===
        // This is the standard 2-link inverse kinematics mathematics
        float d_sq = r_wrist * r_wrist + y_wrist * y_wrist;
        float L1_sq = L1_cm * L1_cm;
        float L2_sq = L2_cm * L2_cm;

        float cosTheta2 = (d_sq - L1_sq - L2_sq) / (2f * L1_cm * L2_cm);
        cosTheta2 = Mathf.Clamp(cosTheta2, -1f, 1f); // avoid Acos error

        float theta2 = Mathf.Acos(cosTheta2); // J2 angle (elbow) [0, pi]
        if (elbowUpMode) theta2 = -theta2;

        // === SOLVE J1 (Shoulder) ===
        float k1 = L1_cm + L2_cm * Mathf.Cos(theta2);
        float k2 = L2_cm * Mathf.Sin(theta2);

        // theta1: J1 angle (Shoulder)
        float theta1 = Mathf.Atan2(y_wrist, r_wrist) - Mathf.Atan2(k2, k1);

        // === SOLVE J3 (Wrist) ===
        // To keep the pen always horizontal (pointing at the draw area)
        // J3 must compensate for the angles of J1 and J2
        float theta3 = -(theta1 + theta2);

        // === CONVERT TO DEGREES AND APPLY OFFSET ===
        float deg0 = theta0 * Mathf.Rad2Deg;
        float deg1 = theta1 * Mathf.Rad2Deg;
        float deg2 = theta2 * Mathf.Rad2Deg;
        float deg3 = theta3 * Mathf.Rad2Deg;

        degBase     = 90f + (deg0 * 1.5f);
        degShoulder = 90f - (deg1 * 1.25f);
        degElbow    = 180f + (deg2 * 1.25f);
        degWrist    = 150f - (deg3 * 1.25f);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Draw canvas normal
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position, transform.up * 0.2f);

        if (rightHandTransform != null)
        {
            Gizmos.color = Color.yellow;
            Vector3 dir = ResolveLocalAxis(rightHandTransform, viveRayAxis);
            Gizmos.DrawRay(rightHandTransform.position, dir * 0.2f);
        }

        // Draw last point hit
        if (_hasWorldPoint)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(_lastWorldPoint, 0.005f);
        }
    }
#endif

    // ————— Auto Draw helpers —————
    private void StartAutoDraw()
    {
        _autoPathUV.Clear();

        // Calculate four angles of square UV
        float half = Mathf.Clamp01(autoSquareSideUV) * 0.5f;
        Vector2 c0 = new Vector2(0.5f - half, 0.5f - half);
        Vector2 c1 = new Vector2(0.5f + half, 0.5f - half);
        Vector2 c2 = new Vector2(0.5f + half, 0.5f + half);
        Vector2 c3 = new Vector2(0.5f - half, 0.5f + half);

        void AddSegment(Vector2 a, Vector2 b, int n)
        {
            n = Mathf.Max(2, n);
            for (int i = 0; i < n; i++)
            {
                float t = (n <= 1) ? 0f : (float)i / (n - 1);
                Vector2 p = Vector2.Lerp(a, b, t);
                p.x = Mathf.Clamp01(p.x);
                p.y = Mathf.Clamp01(p.y);
                _autoPathUV.Add(p);
            }
        }

        // Four edges/sides forming a closed perimeter
        AddSegment(c0, c1, autoPointsPerSide);
        AddSegment(c1, c2, autoPointsPerSide);
        AddSegment(c2, c3, autoPointsPerSide);
        AddSegment(c3, c0, autoPointsPerSide);

        _autoIndex = 0;
        _autoTimer = 0f;
        _autoActive = _autoPathUV.Count > 0;
        _autoCurrentUV = _autoActive ? _autoPathUV[0] : new Vector2(0.5f, 0.5f);
    }

    private void StopAutoDraw()
    {
        _autoActive = false;
    }
}