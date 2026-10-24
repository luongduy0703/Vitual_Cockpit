using UnityEngine;

/// <summary>
/// Places the drawing board where the hand can actually reach it.
///
/// Cardboard gives rotation only (3DoF), so the camera stays at the rig's
/// position while the tracked hand floats roughly an arm's length in front of
/// the face. A board authored at world height 1.4 m is therefore unreachable —
/// the hit point never lands inside the mesh and penDown stays false.
///
/// This component drops the board in front of the head at a reachable
/// distance, sizes it, and paints it white so it is obvious in the headset.
/// Put it on the same GameObject as SimpleDrawingCanvas.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class DrawingBoardPlacer : MonoBehaviour
{
    [Header("Head")]
    [Tooltip("Leave empty to use Camera.main")]
    public Transform head;

    [Header("Placement")]
    [Tooltip("Distance in front of the head (m) — roughly where the hand is")]
    [Min(0.1f)] public float distance = 0.40f;

    [Tooltip("Height relative to eye level (m). Negative = below the eyes")]
    public float heightOffset = -0.10f;

    [Tooltip("Tilt from vertical (deg). 0 = upright board, 45 = drafting table")]
    [Range(0f, 80f)] public float tiltDegrees = 35f;

    [Tooltip("Follow the head's yaw only, so looking down does not move the board")]
    public bool yawOnly = true;

    [Header("Size")]
    [Tooltip("Board size in meters (a Unity Plane mesh is 10x10 local units)")]
    public Vector2 sizeMeters = new Vector2(0.40f, 0.40f);

    [Header("Look")]
    public bool paintWhite = true;
    public Color boardColor = Color.white;

    [Header("Behaviour")]
    [Tooltip("Place the board when the object is enabled (e.g. via Show Draw)")]
    public bool placeOnEnable = true;

    [Tooltip("Keep the board glued in front of the head every frame")]
    public bool followHead = false;

    private Transform Head
    {
        get
        {
            if (head != null) return head;
            if (Camera.main != null) head = Camera.main.transform;
            return head;
        }
    }

    private void OnEnable()
    {
        if (paintWhite) PaintBoard();
        if (placeOnEnable) Recenter();
    }

    private void LateUpdate()
    {
        if (followHead) Recenter();
    }

    /// <summary>
    /// Drops the board in front of the head. Safe to call from a UI button or
    /// a voice command, so the board can be recentred without leaving VR.
    /// </summary>
    public void Recenter()
    {
        var h = Head;
        if (h == null)
        {
            Debug.LogWarning("DrawingBoardPlacer: no camera found, board not placed.");
            return;
        }

        Vector3 forward = h.forward;
        if (yawOnly)
        {
            forward = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-6f) forward = h.forward;
            forward.Normalize();
        }

        transform.position = h.position + forward * distance + Vector3.up * heightOffset;

        // A Plane mesh has its normal along +Y, and SimpleDrawingCanvas uses
        // transform.up as the surface normal — so aim +Y back at the head.
        Quaternion facing = Quaternion.LookRotation(-forward, Vector3.up);
        transform.rotation = facing * Quaternion.Euler(90f - tiltDegrees, 0f, 0f);

        // Unity's built-in Plane is 10x10 units, hence the /10
        transform.localScale = new Vector3(sizeMeters.x / 10f, 1f, sizeMeters.y / 10f);
    }

    private void PaintBoard()
    {
        var renderer = GetComponent<MeshRenderer>();
        if (renderer == null) return;

        // Unlit so the board reads as white no matter how the scene is lit
        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) return;

        var mat = new Material(shader) { color = boardColor };
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", boardColor);
        renderer.material = mat;
        renderer.enabled = true;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawRay(transform.position, transform.up * 0.15f);
    }
#endif
}
