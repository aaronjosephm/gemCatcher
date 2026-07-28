using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Spawns and animates small sparkle stars around a UI element.
/// Each sparkle fades in, scales up, then fades out and respawns
/// at a random position around the parent RectTransform.
/// </summary>
public class TitleSparkle : MonoBehaviour
{
  [SerializeField] int sparkleCount = 12;
  [SerializeField] float minLifetime = 0.6f;
  [SerializeField] float maxLifetime = 1.4f;
  [SerializeField] float minSize = 16f;
  [SerializeField] float maxSize = 40f;
  [SerializeField] float spread = 1.15f; // multiplier beyond rect bounds

  // The logo content sits in the upper portion of the image.
  // These define the elliptical region (in normalized rect coords,
  // 0,0 = center) where sparkles spawn — on the perimeter only.
  [SerializeField] Vector2 logoCenter = new Vector2(0f, 0.12f); // slightly above center
  [SerializeField] Vector2 logoRadii = new Vector2(0.45f, 0.32f); // wide, short ellipse
  [SerializeField] float edgeThickness = 0.12f; // spawn within this band around the edge

  RectTransform parentRect;
  SparkleData[] sparkles;

  struct SparkleData
  {
    public RectTransform rect;
    public Image image;
    public float age;
    public float lifetime;
    public float targetSize;
    public Vector2 position;
  }

  void Start()
  {
    parentRect = GetComponent<RectTransform>();
    if (parentRect == null) return;

    sparkles = new SparkleData[sparkleCount];
    for (int i = 0; i < sparkleCount; i++)
    {
      GameObject go = new GameObject("Sparkle" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
      go.transform.SetParent(transform, false);

      RectTransform rt = go.GetComponent<RectTransform>();
      rt.sizeDelta = Vector2.zero;
      rt.anchorMin = new Vector2(0.5f, 0.5f);
      rt.anchorMax = new Vector2(0.5f, 0.5f);
      rt.pivot = new Vector2(0.5f, 0.5f);

      Image img = go.GetComponent<Image>();
      img.sprite = CreateStarSprite();
      img.raycastTarget = false;
      img.color = new Color(1f, 1f, 1f, 0f);

      sparkles[i] = new SparkleData
      {
        rect = rt,
        image = img,
        age = Random.Range(0f, 1f), // stagger initial spawns
        lifetime = 0f,
      };
      RespawnSparkle(ref sparkles[i]);
      // Stagger start times so they don't all appear at once.
      sparkles[i].age = Random.Range(0f, sparkles[i].lifetime);
    }
  }

  void Update()
  {
    if (sparkles == null) return;

    for (int i = 0; i < sparkles.Length; i++)
    {
      ref SparkleData s = ref sparkles[i];
      s.age += Time.unscaledDeltaTime;
      float t = Mathf.Clamp01(s.age / s.lifetime);

      // Fade in for first 30%, hold, fade out last 30%.
      float alpha;
      if (t < 0.3f)
        alpha = t / 0.3f;
      else if (t < 0.7f)
        alpha = 1f;
      else
        alpha = (1f - t) / 0.3f;

      // Scale: grow from 0 to targetSize over first 40%, hold.
      float scale = t < 0.4f ? (t / 0.4f) : 1f;
      float size = s.targetSize * scale;

      // Gentle rotation.
      float rot = s.age * 90f;

      s.image.color = new Color(1f, 0.95f, 0.7f, alpha * 0.85f);
      s.rect.sizeDelta = new Vector2(size, size);
      s.rect.localRotation = Quaternion.Euler(0f, 0f, rot);
      s.rect.anchoredPosition = s.position;

      if (t >= 1f)
        RespawnSparkle(ref s);
    }
  }

  void RespawnSparkle(ref SparkleData s)
  {
    Vector2 half = parentRect.rect.size * 0.5f;

    // Pick a random angle around the ellipse perimeter.
    float angle = Random.Range(0f, Mathf.PI * 2f);
    // Offset slightly inside/outside the edge for natural scatter.
    float radiusJitter = 1f + Random.Range(-edgeThickness, edgeThickness);

    float ex = logoRadii.x * radiusJitter * Mathf.Cos(angle);
    float ey = logoRadii.y * radiusJitter * Mathf.Sin(angle);

    s.position = new Vector2(
        (logoCenter.x + ex) * half.x * 2f,
        (logoCenter.y + ey) * half.y * 2f);
    s.lifetime = Random.Range(minLifetime, maxLifetime);
    s.targetSize = Random.Range(minSize, maxSize);
    s.age = 0f;
  }

  // Procedurally generates a small 4-point star texture.
  static Sprite starSprite;
  static Sprite CreateStarSprite()
  {
    if (starSprite != null) return starSprite;

    int size = 64;
    Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
    tex.filterMode = FilterMode.Bilinear;
    float center = size * 0.5f;

    for (int y = 0; y < size; y++)
    {
      for (int x = 0; x < size; x++)
      {
        float dx = (x - center) / center;
        float dy = (y - center) / center;
        float dist = Mathf.Sqrt(dx * dx + dy * dy);

        // 4-point star: use product of axis-aligned falloffs.
        float ax = 1f - Mathf.Abs(dx);
        float ay = 1f - Mathf.Abs(dy);
        // Cross shape: max of horizontal and vertical rays.
        float cross = Mathf.Max(
            ax * Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Abs(dy) * 3f), 1.5f),
            ay * Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Abs(dx) * 3f), 1.5f));
        // Central glow.
        float glow = Mathf.Pow(Mathf.Max(0f, 1f - dist), 3f);
        float alpha = Mathf.Clamp01(cross + glow);

        tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
      }
    }
    tex.Apply();
    starSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    return starSprite;
  }
}
