using UnityEngine;

/// <summary>
/// Spawns a 3D lightning bolt effect that strikes a target point from below.
/// Uses multiple overlapping LineRenderers at different widths and colors
/// to create a glowing, volumetric look with depth.
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
        GameObject go = new GameObject("LightningBolt");
        LightningSpawnEffect effect = go.AddComponent<LightningSpawnEffect>();
        effect.strikePoint = targetPosition;

        // Origin below the strike point, random X offset
        float offsetX = Random.Range(-1.2f, 1.2f);
        effect.origin = new Vector3(
            targetPosition.x + offsetX,
            targetPosition.y - BoltLength,
            targetPosition.z
        );
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

            // Additive unlit material for glow blending
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetColor("_BaseColor", layerColors[l]);
            // Make outer layers additive-like by using transparent blend
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

        // Flicker every 2 frames
        flickerFrames++;
        if (flickerFrames >= 2)
        {
            flickerFrames = 0;
            GenerateBolt();
        }

        // Fade out all layers
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
        // Generate the base path once, then offset each layer slightly for depth
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

        // Apply to each layer with slight Z and X offsets for parallax/volume
        for (int l = 0; l < LayerCount; l++)
        {
            if (layers[l] == null) continue;
            for (int i = 0; i < Segments; i++)
            {
                Vector3 p = basePoints[i];
                p.z += layerZ[l];
                // Outer layers get slight random offset for volume
                if (l < 2 && i > 0 && i < Segments - 1)
                {
                    p.x += Random.Range(-0.05f, 0.05f);
                }
                layers[l].SetPosition(i, p);
            }
        }
    }
}
