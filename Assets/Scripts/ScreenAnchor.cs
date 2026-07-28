using UnityEngine;

/// <summary>
/// Anchors a world-space GameObject to a screen-relative position so it
/// stays at the same visual spot regardless of device aspect ratio.
///
/// The anchor is defined as a normalized viewport offset from a chosen
/// edge (e.g., bottom-left corner). The object's children (gems embedded
/// in rocks) keep their local transforms untouched — only the root
/// position is adjusted.
///
/// Attach to each rock GameObject and configure the anchor in Inspector.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class ScreenAnchor : MonoBehaviour
{
    public enum HorizontalAnchor { Left, Center, Right }
    public enum VerticalAnchor { Bottom, Center, Top }

    [Header("Anchor Point")]
    [Tooltip("Horizontal screen edge to anchor to.")]
    public HorizontalAnchor horizontalAnchor = HorizontalAnchor.Left;

    [Tooltip("Vertical screen edge to anchor to.")]
    public VerticalAnchor verticalAnchor = VerticalAnchor.Bottom;

    [Header("Offset (world units from anchor point)")]
    [Tooltip("Horizontal offset from the anchor edge. Positive = inward.")]
    public float offsetX = 0f;

    [Tooltip("Vertical offset from the anchor edge. Positive = inward.")]
    public float offsetY = 0f;

    [Tooltip("Z depth (unchanged across devices).")]
    public float z = 1.65f;

    private Camera cam;
    private float lastAspect = -1f;
    private float lastOrtho = -1f;

    void OnEnable()
    {
        Reposition();
    }

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        if (!Mathf.Approximately(cam.aspect, lastAspect)
            || !Mathf.Approximately(cam.orthographicSize, lastOrtho))
        {
            Reposition();
        }
    }

    void Reposition()
    {
        cam = Camera.main;
        if (cam == null) return;

        float ortho = cam.orthographicSize;
        float aspect = cam.aspect;
        lastAspect = aspect;
        lastOrtho = ortho;

        float halfH = ortho;
        float halfW = ortho * aspect;
        float camX = cam.transform.position.x;
        float camY = cam.transform.position.y;

        float x, y;

        switch (horizontalAnchor)
        {
            case HorizontalAnchor.Left:   x = camX - halfW + offsetX; break;
            case HorizontalAnchor.Right:  x = camX + halfW + offsetX; break;
            default:                       x = camX + offsetX; break;
        }

        switch (verticalAnchor)
        {
            case VerticalAnchor.Bottom: y = camY - halfH + offsetY; break;
            case VerticalAnchor.Top:    y = camY + halfH + offsetY; break;
            default:                     y = camY + offsetY; break;
        }

        transform.position = new Vector3(x, y, z);
    }
}
