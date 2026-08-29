using TMPro;
using UnityEngine;

// Temporary "+20" / "-10" text that floats up from the catch position, fades out, and
// destroys itself. Spawned by UIManager.SpawnFloatingScore. Lives on a screen-space UI
// canvas; positions are in screen pixel coordinates.
[RequireComponent(typeof(TextMeshProUGUI))]
public class FloatingScoreText : MonoBehaviour
{
    public float lifetime = 1.0f;       // Seconds before the text destroys itself
    public float floatPixels = 120f;    // Distance (in canvas reference pixels) to drift upward
    public float scaleStart = 0.6f;     // Pop-in scale at t = 0
    public float scaleEnd = 1.2f;       // Final scale at t = lifetime

    private TextMeshProUGUI tmp;
    private RectTransform rect;
    private Vector2 startAnchoredPos;
    private Color startColor;
    private float age;

    void Awake()
    {
        tmp = GetComponent<TextMeshProUGUI>();
        rect = GetComponent<RectTransform>();
    }

    public void Initialize(int amount)
    {
        // Green for gains, red for losses. Looks fine on most backgrounds.
        Color color = amount > 0
            ? new Color(1.00f, 0.84f, 0.00f)   // Golden for points
            : new Color(1.00f, 0.45f, 0.45f);
        Initialize((amount > 0 ? "+" : "") + amount.ToString(), color);
    }

    // Overload for arbitrary text (e.g. "-1 ♥" on a miss).
    public void Initialize(string displayText, Color color)
    {
        if (tmp == null) tmp = GetComponent<TextMeshProUGUI>();
        if (rect == null) rect = GetComponent<RectTransform>();

        tmp.text = displayText;
        tmp.color = color;

        startAnchoredPos = rect.anchoredPosition;
        startColor = color;
        age = 0f;
    }

    void Update()
    {
        age += Time.deltaTime;
        float t = Mathf.Clamp01(age / lifetime);

        // Drift upward.
        rect.anchoredPosition = startAnchoredPos + new Vector2(0f, floatPixels * t);

        // Pop scale: ease-out so it grows quickly then settles.
        float scale = Mathf.Lerp(scaleStart, scaleEnd, 1f - (1f - t) * (1f - t));
        rect.localScale = new Vector3(scale, scale, 1f);

        // Fade out, biased toward the end so the number reads cleanly first.
        Color c = startColor;
        c.a = 1f - Mathf.Pow(t, 2f);
        tmp.color = c;

        if (t >= 1f)
        {
            Destroy(gameObject);
        }
    }
}
