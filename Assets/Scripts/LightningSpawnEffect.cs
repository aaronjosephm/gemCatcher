using UnityEngine;

/// <summary>
/// Spawns a brief lightning bolt effect that strikes a target point from below.
/// Uses a LineRenderer with jagged segments. The bolt flickers for a short
/// duration then destroys itself.
/// </summary>
public class LightningSpawnEffect : MonoBehaviour
{
    private const int Segments = 10;
    private const float Duration = 0.15f;
    private const float BoltLength = 4f;
    private const float Jitter = 0.4f;
    private const float Width = 0.08f;

    private LineRenderer lr;
    private float timer;
    private Vector3 strikePoint;
    private Vector3 origin;
    private int flickerFrames;

    /// <summary>
    /// Create a lightning bolt that strikes the given world position from below.
    /// </summary>
    public static void Strike(Vector3 targetPosition)
    {
        GameObject go = new GameObject("LightningBolt");
        LightningSpawnEffect effect = go.AddComponent<LightningSpawnEffect>();
        effect.strikePoint = targetPosition;

        // Origin is below the strike point, offset randomly on X
        float offsetX = Random.Range(-1.5f, 1.5f);
        effect.origin = new Vector3(
            targetPosition.x + offsetX,
            targetPosition.y - BoltLength,
            targetPosition.z
        );
    }

    void Awake()
    {
        lr = gameObject.AddComponent<LineRenderer>();
        lr.positionCount = Segments;
        lr.startWidth = Width;
        lr.endWidth = Width * 0.4f;
        lr.useWorldSpace = true;
        lr.sortingOrder = 50;

        // Bright unlit material
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetColor("_BaseColor", new Color(0.7f, 0.85f, 1f, 1f));
        lr.material = mat;

        // Gradient: white core fading to blue at the origin
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.5f, 0.6f, 1f), 0f),
                new GradientColorKey(Color.white, 0.7f),
                new GradientColorKey(Color.white, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.6f, 0f),
                new GradientAlphaKey(1f, 0.5f),
                new GradientAlphaKey(1f, 1f)
            }
        );
        lr.colorGradient = gradient;

        timer = Duration;
        GenerateBolt();
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        // Flicker: regenerate bolt shape every 2 frames for electric feel
        flickerFrames++;
        if (flickerFrames >= 2)
        {
            flickerFrames = 0;
            GenerateBolt();
        }

        // Fade out
        float alpha = timer / Duration;
        lr.startWidth = Width * alpha;
        lr.endWidth = Width * 0.4f * alpha;
    }

    void GenerateBolt()
    {
        for (int i = 0; i < Segments; i++)
        {
            float t = i / (float)(Segments - 1);
            Vector3 point = Vector3.Lerp(origin, strikePoint, t);

            // Don't jitter the endpoints
            if (i > 0 && i < Segments - 1)
            {
                point.x += Random.Range(-Jitter, Jitter);
                point.y += Random.Range(-Jitter * 0.3f, Jitter * 0.3f);
            }

            lr.SetPosition(i, point);
        }
    }
}
