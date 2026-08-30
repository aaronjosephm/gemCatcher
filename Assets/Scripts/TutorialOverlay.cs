using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Tutorial overlay that shows a pulsing white circle with "Press" label
/// on the side of the screen the player should tap.
/// </summary>
public class TutorialOverlay : MonoBehaviour
{
    public static TutorialOverlay Instance { get; private set; }

    Canvas canvas;
    GameObject indicator; // container for circle + label
    Image circleImage;
    TextMeshProUGUI pressLabel;
    RectTransform indicatorRect;

    bool showing;
    float showTime;

    void Awake()
    {
        Instance = this;
        BuildUI();
        HideArrows();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void BuildUI()
    {
        // Overlay canvas.
        GameObject canvasGo = new GameObject("TutorialCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        // Indicator container.
        indicator = new GameObject("PressIndicator", typeof(RectTransform));
        indicator.transform.SetParent(canvasGo.transform, false);
        indicatorRect = indicator.GetComponent<RectTransform>();
        indicatorRect.sizeDelta = new Vector2(200f, 260f);

        // "Press" label above the circle.
        GameObject labelGo = new GameObject("PressLabel", typeof(RectTransform));
        labelGo.transform.SetParent(indicator.transform, false);
        RectTransform labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 1f);
        labelRect.anchorMax = new Vector2(0.5f, 1f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.anchoredPosition = new Vector2(0f, 0f);
        labelRect.sizeDelta = new Vector2(200f, 60f);

        pressLabel = labelGo.AddComponent<TextMeshProUGUI>();
        pressLabel.text = "Press";
        pressLabel.fontSize = 48;
        pressLabel.fontStyle = FontStyles.Bold;
        pressLabel.alignment = TextAlignmentOptions.Center;
        pressLabel.color = Color.white;
        pressLabel.raycastTarget = false;
        pressLabel.outlineWidth = 0.25f;
        pressLabel.outlineColor = new Color(0f, 0f, 0f, 0.7f);

        // White circle below the label.
        GameObject circleGo = new GameObject("Circle", typeof(RectTransform));
        circleGo.transform.SetParent(indicator.transform, false);
        RectTransform circleRect = circleGo.GetComponent<RectTransform>();
        circleRect.anchorMin = new Vector2(0.5f, 0f);
        circleRect.anchorMax = new Vector2(0.5f, 0f);
        circleRect.pivot = new Vector2(0.5f, 0f);
        circleRect.anchoredPosition = new Vector2(0f, 0f);
        circleRect.sizeDelta = new Vector2(160f, 160f);

        circleImage = circleGo.AddComponent<Image>();
        circleImage.color = new Color(1f, 1f, 1f, 0.85f);
        circleImage.raycastTarget = false;

        // Make it circular by using a rounded sprite — but we don't have one.
        // Use the default UI sprite and mask it round via a generated texture.
        circleImage.sprite = CreateCircleSprite(128);

        indicator.SetActive(false);
    }

    /// <summary>Generates a white filled circle sprite at runtime.</summary>
    static Sprite CreateCircleSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float center = size * 0.5f;
        float radius = center - 1f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                if (dist <= radius)
                    tex.SetPixel(x, y, Color.white);
                else if (dist <= radius + 1f)
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(radius + 1f - dist)));
                else
                    tex.SetPixel(x, y, Color.clear);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    /// <summary>Show the press indicator on the given side (+1 = right, -1 = left).</summary>
    public void ShowArrows(int dir)
    {
        showing = true;
        showTime = Time.time;

        // Position: right or left side, vertically centered.
        float xAnchor = dir > 0 ? 0.78f : 0.22f;
        indicatorRect.anchorMin = new Vector2(xAnchor, 0.45f);
        indicatorRect.anchorMax = new Vector2(xAnchor, 0.45f);
        indicatorRect.pivot = new Vector2(0.5f, 0.5f);
        indicatorRect.anchoredPosition = Vector2.zero;

        indicator.SetActive(true);
    }

    public void HideArrows()
    {
        showing = false;
        if (indicator != null) indicator.SetActive(false);
    }

    void Update()
    {
        if (!showing) return;

        float t = Time.time - showTime;

        // Pulse scale between 0.9 and 1.1.
        float pulse = Mathf.Sin(t * Mathf.PI * 2f * 1.2f);
        float scale = Mathf.Lerp(0.9f, 1.1f, (pulse + 1f) * 0.5f);
        indicatorRect.localScale = Vector3.one * scale;

        // Pulse alpha between 0.5 and 1.0.
        float alpha = Mathf.Lerp(0.5f, 1f, (pulse + 1f) * 0.5f);
        circleImage.color = new Color(1f, 1f, 1f, alpha);
        pressLabel.color = new Color(1f, 1f, 1f, alpha);
    }
}

