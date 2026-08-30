using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Procedural UI overlay that shows animated directional chevron arrows
/// to teach the player to move left/right. No text — pure visual.
///
/// The arrows slide across the screen in the target direction with a
/// staggered pulse, drawing the player's eye toward the safe gap.
/// </summary>
public class TutorialOverlay : MonoBehaviour
{
    public static TutorialOverlay Instance { get; private set; }

    Canvas canvas;
    readonly List<RectTransform> chevrons = new List<RectTransform>();
    readonly List<TextMeshProUGUI> chevronTexts = new List<TextMeshProUGUI>();

    int direction; // +1 = right, -1 = left
    bool showing;
    float showTime;

    const int ChevronCount = 3;
    const float ChevronSpacing = 120f;
    const float SlideRange = 60f;   // px of slide animation per chevron
    const float CycleTime = 0.8f;   // seconds per full pulse cycle
    const float ChevronSize = 100f;

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
        // Create an overlay canvas.
        GameObject canvasGo = new GameObject("TutorialCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90; // above gameplay, below menus

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        // Disable raycast blocking so it doesn't interfere with input.
        Destroy(canvasGo.GetComponent<GraphicRaycaster>());

        // Container positioned in the lower-middle area (near the catchy).
        GameObject container = new GameObject("ArrowContainer", typeof(RectTransform));
        container.transform.SetParent(canvasGo.transform, false);
        RectTransform containerRect = container.GetComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.5f, 0.25f);
        containerRect.anchorMax = new Vector2(0.5f, 0.25f);
        containerRect.pivot = new Vector2(0.5f, 0.5f);
        containerRect.anchoredPosition = Vector2.zero;
        containerRect.sizeDelta = new Vector2(600f, 200f);

        // Create chevron text elements.
        for (int i = 0; i < ChevronCount; i++)
        {
            GameObject chevGo = new GameObject($"Chevron_{i}", typeof(RectTransform));
            chevGo.transform.SetParent(container.transform, false);
            RectTransform chevRect = chevGo.GetComponent<RectTransform>();
            chevRect.anchorMin = new Vector2(0.5f, 0.5f);
            chevRect.anchorMax = new Vector2(0.5f, 0.5f);
            chevRect.pivot = new Vector2(0.5f, 0.5f);
            chevRect.sizeDelta = new Vector2(ChevronSize, ChevronSize);

            TextMeshProUGUI tmp = chevGo.AddComponent<TextMeshProUGUI>();
            tmp.text = "▶"; // will be flipped for left
            tmp.fontSize = 80;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(1f, 1f, 1f, 0.9f);
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;

            // Add outline for visibility against any background.
            tmp.outlineWidth = 0.2f;
            tmp.outlineColor = new Color(0f, 0f, 0f, 0.6f);

            chevrons.Add(chevRect);
            chevronTexts.Add(tmp);
        }
    }

    /// <summary>Show animated arrows pointing in the given direction (+1 right, -1 left).</summary>
    public void ShowArrows(int dir)
    {
        direction = dir;
        showing = true;
        showTime = Time.time;

        string symbol = dir > 0 ? "▶" : "◀";
        foreach (var tmp in chevronTexts)
        {
            tmp.text = symbol;
            tmp.gameObject.SetActive(true);
        }
    }

    public void HideArrows()
    {
        showing = false;
        foreach (var chevRect in chevrons)
            chevRect.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!showing) return;

        float t = Time.time - showTime;

        for (int i = 0; i < ChevronCount; i++)
        {
            // Each chevron is offset in the animation cycle.
            float phase = (t / CycleTime + (float)i / ChevronCount) % 1f;

            // Position: spread out from center, slide in the direction.
            float baseX = (i - (ChevronCount - 1) * 0.5f) * ChevronSpacing * direction;
            float slideOffset = Mathf.Sin(phase * Mathf.PI * 2f) * SlideRange * 0.5f * direction;
            chevrons[i].anchoredPosition = new Vector2(baseX + slideOffset, 0f);

            // Alpha: pulse between 0.3 and 1.0.
            float alpha = Mathf.Lerp(0.4f, 1f, (Mathf.Sin(phase * Mathf.PI * 2f) + 1f) * 0.5f);
            // Scale: subtle pulse.
            float scale = Mathf.Lerp(0.85f, 1.15f, (Mathf.Sin(phase * Mathf.PI * 2f) + 1f) * 0.5f);

            chevronTexts[i].color = new Color(1f, 1f, 1f, alpha);
            chevrons[i].localScale = Vector3.one * scale;
        }
    }
}
