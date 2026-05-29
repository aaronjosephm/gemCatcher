using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Short-lived top-of-screen banner for milestone messages ("EXTRA LIFE!", "COMBO x3!",
/// etc). Animates in two phases:
///
///   1. Pop in: scale 0 → 1.15 with overshoot, then settle to 1.0.
///   2. Hold + fade: stays at 1.0 for <see cref="holdDuration"/> seconds, then fades
///      its alpha to 0 and grows slightly as it goes.
///
/// Banners auto-stack: a new banner positions itself BELOW any banners already in
/// flight, separated by <see cref="stackSlotHeight"/> pixels, so simultaneous
/// notifications (e.g. EXTRA LIFE + WIDE CATCHER) don't render on top of each other.
/// As older banners fade out and destroy, surviving banners smoothly slide up into
/// the freed slots.
///
/// Self-destructs at the end. Spawned by <c>UIManager.SpawnBannerNotification</c>;
/// callers configure the TMP component (font sizing, color) on the host GameObject
/// before adding this component and calling <see cref="Initialize"/>.
/// </summary>
[RequireComponent(typeof(TextMeshProUGUI))]
public class BannerNotification : MonoBehaviour
{
  public float popInDuration = 0.18f;
  public float settleDuration = 0.10f;
  public float holdDuration = 1.10f;
  public float fadeOutDuration = 0.35f;

  [Header("Stacking")]
  [Tooltip("Vertical spacing in pixels between stacked banners. New banners are placed this far BELOW the previous one; older banners slide up by this amount when one above them is destroyed. Defaults to slightly more than the banner sizeDelta height so the brief 1.15x pop-in scale doesn't overlap the banner above.")]
  public float stackSlotHeight = 140f;
  [Tooltip("How quickly a banner glides into its assigned stack slot. Higher = snappier, lower = floatier.")]
  public float stackLerpSpeed = 14f;

  // Live banners ordered from oldest (index 0, sits at base Y) to newest
  // (last index, sits stackSlotHeight*N pixels lower). Maintained by
  // OnEnable/OnDisable so destroying a banner automatically re-numbers the
  // rest and lets them slide up.
  private static readonly List<BannerNotification> activeBanners = new List<BannerNotification>();

  private TextMeshProUGUI tmp;
  private RectTransform rect;
  private Color baseColor;
  private float age;

  // Captured once when this banner registers with the stack — the Y the
  // caller (UIManager.SpawnBannerNotification) chose. Stack offsets are
  // applied relative to this so callers can spawn banners at any Y and the
  // stacking math still works.
  private float baseAnchoredY;
  private float currentOffsetY;
  private float targetOffsetY;

  // Reset the static list on Play Mode entry / domain reload so a stale
  // banner reference from a previous session can't leave a permanent gap
  // in the new round's stack.
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetStaticState()
  {
    activeBanners.Clear();
  }

  void Awake()
  {
    tmp = GetComponent<TextMeshProUGUI>();
    rect = GetComponent<RectTransform>();
  }

  void OnEnable()
  {
    if (rect == null) rect = GetComponent<RectTransform>();
    // Capture the caller-chosen anchored Y the first time we activate so the
    // stack offset is always relative to the intended position.
    baseAnchoredY = rect.anchoredPosition.y;

    activeBanners.Add(this);
    RebuildStackTargets();

    // New banner enters at its target slot directly so it doesn't appear
    // to "fall in" from above the topmost slot — only existing banners
    // animate to their new slots when this one joins.
    currentOffsetY = targetOffsetY;
    ApplyStackedPosition();
  }

  void OnDisable()
  {
    activeBanners.Remove(this);
    RebuildStackTargets();
  }

  // Recomputes targetOffsetY for every active banner based on its index in
  // the list. Called whenever a banner is added or removed.
  static void RebuildStackTargets()
  {
    for (int i = 0; i < activeBanners.Count; i++)
    {
      BannerNotification b = activeBanners[i];
      if (b == null) continue;
      // Index 0 = oldest, sits at baseAnchoredY (offset 0).
      // Index N = newest, sits N * stackSlotHeight pixels below.
      b.targetOffsetY = -i * b.stackSlotHeight;
    }
  }

  void ApplyStackedPosition()
  {
    if (rect == null) return;
    Vector2 ap = rect.anchoredPosition;
    ap.y = baseAnchoredY + currentOffsetY;
    rect.anchoredPosition = ap;
  }

  public void Initialize(string text, Color color)
  {
    if (tmp == null) tmp = GetComponent<TextMeshProUGUI>();
    if (rect == null) rect = GetComponent<RectTransform>();

    tmp.text = text;
    tmp.color = color;
    baseColor = color;
    age = 0f;
    rect.localScale = Vector3.zero;
  }

  void Update()
  {
    // Use unscaledDeltaTime so the banner plays at full speed during the hit-stop or
    // any other Time.timeScale dip.
    age += Time.unscaledDeltaTime;

    // Glide toward this banner's assigned stack slot. RebuildStackTargets fires
    // whenever a banner is added or removed, so a destroyed banner above us
    // changes our targetOffsetY and we slide up to fill the gap here.
    currentOffsetY = Mathf.Lerp(
      currentOffsetY,
      targetOffsetY,
      Mathf.Clamp01(stackLerpSpeed * Time.unscaledDeltaTime));
    ApplyStackedPosition();

    float popInEnd = popInDuration;
    float settleEnd = popInEnd + settleDuration;
    float holdEnd = settleEnd + holdDuration;
    float fadeOutEnd = holdEnd + fadeOutDuration;

    float scale;
    float alpha;

    if (age < popInEnd)
    {
      // 0 → 1.15 with quadratic ease-out (snappy entry).
      float t = age / popInEnd;
      scale = Mathf.Lerp(0f, 1.15f, 1f - (1f - t) * (1f - t));
      alpha = 1f;
    }
    else if (age < settleEnd)
    {
      // 1.15 → 1.0 settle.
      float t = (age - popInEnd) / settleDuration;
      scale = Mathf.Lerp(1.15f, 1.0f, 1f - (1f - t) * (1f - t));
      alpha = 1f;
    }
    else if (age < holdEnd)
    {
      scale = 1.0f;
      alpha = 1f;
    }
    else if (age < fadeOutEnd)
    {
      // Slight scale-up while fading so it feels like it's drifting away rather than
      // just blinking off.
      float t = (age - holdEnd) / fadeOutDuration;
      scale = Mathf.Lerp(1.0f, 1.06f, t);
      alpha = 1f - t;
    }
    else
    {
      Destroy(gameObject);
      return;
    }

    rect.localScale = new Vector3(scale, scale, 1f);
    Color c = baseColor;
    c.a = alpha;
    tmp.color = c;
  }
}
