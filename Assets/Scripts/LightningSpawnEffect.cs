using UnityEngine;

/// <summary>
/// Spawns a 3D lightning bolt effect that strikes a target point from below.
/// Uses multiple overlapping LineRenderers at different widths and colors
/// to create a glowing, volumetric look with depth, plus spark particles
/// that burst outward from the strike point.
/// </summary>
public class LightningSpawnEffect : MonoBehaviour
{
    private const int Segments = 12;
    private const float Duration = 0.18f;
    private const float BoltLength = 4f;
    private const float Jitter = 0.45f;

    // Multiple layers for 3D depth
    private const int LayerCount = 4;

    private LineRenderer[] layers;
    private float timer;
    private Vector3 strikePoint;
    private Vector3 origin;
    private int flickerFrames;
    private Vector3[] basePoints;

    // Layer configs: width multiplier, color, z-offset
    private static readonly float[] layerWidths = { 3.5f, 1.8f, 0.8f, 0.4f };
    private static readonly Color[] layerColors =
    {
        new Color(0.3f, 0.4f, 1f, 0.25f),   // outer glow (wide, faint blue)
        new Color(0.5f, 0.7f, 1f, 0.5f),    // mid glow
        new Color(0.8f, 0.9f, 1f, 0.85f),   // bright core
        new Color(1f, 1f, 1f, 1f),           // white-hot center
    };
    private static readonly float[] layerZ = { 0.02f, 0.01f, 0f, -0.01f };

    /// <summary>
    /// Create a lightning bolt that strikes the given world position from below.
    /// </summary>
    public static void Strike(Vector3 targetPosition)
    {
        // Lightning bolt
        GameObject go = new GameObject("LightningBolt");
        LightningSpawnEffect effect = go.AddComponent<LightningSpawnEffect>();
        effect.strikePoint = targetPosition;

        float offsetX = Random.Range(-1.2f, 1.2f);
        effect.origin = new Vector3(
            targetPosition.x + offsetX,
            targetPosition.y - BoltLength,
            targetPosition.z
        );

        // Spark burst at strike point
        SpawnSparks(targetPosition);

        // Play zap sound
        if (zapClip == null)
            zapClip = Resources.Load<AudioClip>("Audio/LightningZap");
        if (zapClip != null)
        {
            AudioSource.PlayClipAtPoint(zapClip, targetPosition, 1.0f);
        }
    }

    private static AudioClip zapClip;

    private static void SpawnSparks(Vector3 position)
    {
        int sparkCount = Random.Range(6, 10);
        for (int i = 0; i < sparkCount; i++)
        {
            GameObject sparkGo = new GameObject("Spark");
            LightningSpark spark = sparkGo.AddComponent<LightningSpark>();
            spark.Init(position);
        }
    }

    void Awake()
    {
        layers = new LineRenderer[LayerCount];

        for (int l = 0; l < LayerCount; l++)
        {
            GameObject layerGo = (l == 0) ? gameObject : new GameObject("BoltLayer" + l);
            if (l > 0) layerGo.transform.SetParent(transform, false);

            LineRenderer lr = layerGo.AddComponent<LineRenderer>();
            lr.positionCount = Segments;
            lr.startWidth = layerWidths[l];
            lr.endWidth = layerWidths[l] * 0.3f;
            lr.useWorldSpace = true;
            lr.sortingOrder = 50 + l;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 4;

            Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetColor("_BaseColor", layerColors[l]);
            if (l < 2)
            {
                mat.SetFloat("_Surface", 1);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = 3000 + l;
            }
            else
            {
                mat.SetColor("_BaseColor", layerColors[l]);
            }
            lr.material = mat;

            layers[l] = lr;
        }

        timer = Duration;
        basePoints = new Vector3[Segments];
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

        flickerFrames++;
        if (flickerFrames >= 2)
        {
            flickerFrames = 0;
            GenerateBolt();
        }

        float alpha = timer / Duration;
        for (int l = 0; l < LayerCount; l++)
        {
            if (layers[l] == null) continue;
            layers[l].startWidth = layerWidths[l] * alpha;
            layers[l].endWidth = layerWidths[l] * 0.3f * alpha;
        }
    }

    void GenerateBolt()
    {
        for (int i = 0; i < Segments; i++)
        {
            float t = i / (float)(Segments - 1);
            Vector3 point = Vector3.Lerp(origin, strikePoint, t);

            if (i > 0 && i < Segments - 1)
            {
                point.x += Random.Range(-Jitter, Jitter);
                point.y += Random.Range(-Jitter * 0.25f, Jitter * 0.25f);
            }
            basePoints[i] = point;
        }

        for (int l = 0; l < LayerCount; l++)
        {
            if (layers[l] == null) continue;
            for (int i = 0; i < Segments; i++)
            {
                Vector3 p = basePoints[i];
                p.z += layerZ[l];
                if (l < 2 && i > 0 && i < Segments - 1)
                {
                    p.x += Random.Range(-0.05f, 0.05f);
                }
                layers[l].SetPosition(i, p);
            }
        }
    }
}

/// <summary>
/// A single spark particle that flies outward from the lightning strike point.
/// Uses a short LineRenderer to create a glowing streak that moves, shrinks, and fades.
/// </summary>
public class LightningSpark : MonoBehaviour
{
    private const float SparkDuration = 0.35f;

    private Vector3 velocity;
    private float life;
    private LineRenderer lr;
    private float startWidth;
    private Color startColor;

    public void Init(Vector3 position)
    {
        transform.position = position;

        // Random outward direction (biased upward and to sides)
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float speed = Random.Range(3f, 8f);
        velocity = new Vector3(Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed * 0.7f, 0f);
        // Add slight gravity pull
        velocity.y += Random.Range(0.5f, 2f);

        life = SparkDuration * Random.Range(0.6f, 1.0f);

        // Vary between white-hot and electric blue
        float colorBlend = Random.Range(0f, 1f);
        if (colorBlend < 0.4f)
            startColor = new Color(1f, 1f, 1f, 1f);           // white-hot
        else if (colorBlend < 0.7f)
            startColor = new Color(0.7f, 0.85f, 1f, 1f);      // blue-white
        else
            startColor = new Color(1f, 0.9f, 0.4f, 1f);       // golden

        startWidth = Random.Range(0.08f, 0.2f);

        lr = gameObject.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.startWidth = startWidth;
        lr.endWidth = startWidth * 0.3f;
        lr.useWorldSpace = true;
        lr.sortingOrder = 55;
        lr.numCapVertices = 3;

        // Additive material for bright glow
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetColor("_BaseColor", startColor);
        mat.SetFloat("_Surface", 1);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = 3010;
        lr.material = mat;

        UpdatePositions();
    }

    void Update()
    {
        life -= Time.deltaTime;
        if (life <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        // Gravity and drag
        velocity.y -= 12f * Time.deltaTime;
        velocity *= (1f - 2.5f * Time.deltaTime);

        transform.position += velocity * Time.deltaTime;

        float frac = life / SparkDuration;
        lr.startWidth = startWidth * frac;
        lr.endWidth = startWidth * 0.3f * frac;

        // Fade color
        Color c = startColor;
        c.a = frac;
        lr.material.SetColor("_BaseColor", c);

        UpdatePositions();
    }

    void UpdatePositions()
    {
        // Streak: head at current pos, tail trailing behind along velocity
        Vector3 head = transform.position;
        Vector3 tail = head - velocity.normalized * (startWidth * 3f);
        lr.SetPosition(0, tail);
        lr.SetPosition(1, head);
    }
}
