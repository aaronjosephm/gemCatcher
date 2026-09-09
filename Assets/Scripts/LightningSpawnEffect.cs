using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Pooled 3D lightning strike. Bolt layers, sparks, materials, and audio sources
/// are prewarmed so spawning a gem does not create short-lived rendering objects.
/// </summary>
public sealed class LightningSpawnEffect : MonoBehaviour
{
    private const int Segments = 12;
    private const float Duration = 0.18f;
    private const float BoltLength = 4f;
    private const float Jitter = 0.45f;
    private const int LayerCount = 4;
    private const int InitialBoltPoolSize = 2;
    private const int InitialSparkPoolSize = 20;
    private const int AudioSourcePoolSize = 2;

    private static readonly float[] LayerWidths = { 3.5f, 1.8f, 0.8f, 0.4f };
    private static readonly Color[] LayerColors =
    {
        new Color(0.3f, 0.4f, 1f, 0.25f),
        new Color(0.5f, 0.7f, 1f, 0.5f),
        new Color(0.8f, 0.9f, 1f, 0.85f),
        new Color(1f, 1f, 1f, 1f),
    };
    private static readonly float[] LayerZ = { 0.02f, 0.01f, 0f, -0.01f };

    private static readonly List<LightningSpawnEffect> BoltPool =
        new List<LightningSpawnEffect>(InitialBoltPoolSize);
    private static readonly List<LightningSpark> SparkPool =
        new List<LightningSpark>(InitialSparkPoolSize);

    private static GameObject poolRoot;
    private static Transform boltPoolRoot;
    private static Transform sparkPoolRoot;
    private static Material[] boltMaterials;
    private static Material sparkMaterial;
    private static AudioClip zapClip;
    private static AudioSource[] zapSources;
    private static int nextZapSource;

    private LineRenderer[] layers;
    private Vector3[] basePoints;
    private float timer;
    private Vector3 strikePoint;
    private Vector3 origin;
    private int flickerFrames;
    private bool initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        BoltPool.Clear();
        SparkPool.Clear();
        poolRoot = null;
        boltPoolRoot = null;
        sparkPoolRoot = null;
        boltMaterials = null;
        sparkMaterial = null;
        zapClip = null;
        zapSources = null;
        nextZapSource = 0;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Prewarm()
    {
        EnsurePool();
    }

    /// <summary>Create a lightning bolt that strikes the given world position.</summary>
    public static void Strike(Vector3 targetPosition)
    {
        EnsurePool();

        LightningSpawnEffect effect = null;
        for (int i = 0; i < BoltPool.Count; i++)
        {
            if (!BoltPool[i].gameObject.activeSelf)
            {
                effect = BoltPool[i];
                break;
            }
        }

        if (effect == null)
        {
            effect = CreateBolt();
        }

        effect.Activate(targetPosition);
        SpawnSparks(targetPosition);
        PlayZap(targetPosition);
    }

    private static void EnsurePool()
    {
        if (poolRoot != null) return;

        poolRoot = new GameObject("Lightning Effect Pool");
        poolRoot.hideFlags = HideFlags.DontSave;
        DontDestroyOnLoad(poolRoot);

        boltPoolRoot = CreatePoolChild("Bolts");
        sparkPoolRoot = CreatePoolChild("Sparks");
        CreateSharedMaterials();
        CreateAudioSources();

        for (int i = 0; i < InitialBoltPoolSize; i++)
        {
            CreateBolt();
        }

        for (int i = 0; i < InitialSparkPoolSize; i++)
        {
            CreateSpark();
        }

        zapClip = Resources.Load<AudioClip>("Audio/LightningZap");
    }

    private static Transform CreatePoolChild(string name)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(poolRoot.transform, false);
        return child.transform;
    }

    private static void CreateSharedMaterials()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Sprites/Default");
        if (shader == null) return;

        boltMaterials = new Material[LayerCount];
        for (int i = 0; i < LayerCount; i++)
        {
            boltMaterials[i] = CreateLineMaterial(
                shader,
                LayerColors[i],
                i < 2,
                3000 + i,
                $"Lightning Bolt Layer {i}");
        }

        sparkMaterial = CreateLineMaterial(
            shader,
            Color.white,
            true,
            3010,
            "Lightning Spark Shared Material");
    }

    private static Material CreateLineMaterial(
        Shader shader,
        Color color,
        bool additive,
        int renderQueue,
        string materialName)
    {
        Material material = new Material(shader)
        {
            name = materialName,
            hideFlags = HideFlags.DontSave,
        };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);

        if (additive)
        {
            material.SetFloat("_Surface", 1f);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.One);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = renderQueue;
        }

        return material;
    }

    private static void CreateAudioSources()
    {
        zapSources = new AudioSource[AudioSourcePoolSize];
        for (int i = 0; i < zapSources.Length; i++)
        {
            GameObject audioObject = new GameObject($"ZapAudio_{i + 1}");
            audioObject.transform.SetParent(poolRoot.transform, false);

            AudioSource source = audioObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            zapSources[i] = source;
        }
    }

    private static LightningSpawnEffect CreateBolt()
    {
        GameObject go = new GameObject($"LightningBolt_{BoltPool.Count + 1}");
        go.transform.SetParent(boltPoolRoot, false);
        go.SetActive(false);

        LightningSpawnEffect effect = go.AddComponent<LightningSpawnEffect>();
        effect.Initialize();
        BoltPool.Add(effect);
        return effect;
    }

    private static LightningSpark CreateSpark()
    {
        GameObject go = new GameObject($"LightningSpark_{SparkPool.Count + 1}");
        go.transform.SetParent(sparkPoolRoot, false);
        go.SetActive(false);

        LightningSpark spark = go.AddComponent<LightningSpark>();
        spark.Initialize(sparkMaterial);
        SparkPool.Add(spark);
        return spark;
    }

    private static void SpawnSparks(Vector3 position)
    {
        int sparkCount = Random.Range(6, 10);
        for (int i = 0; i < sparkCount; i++)
        {
            LightningSpark spark = null;
            for (int j = 0; j < SparkPool.Count; j++)
            {
                if (!SparkPool[j].gameObject.activeSelf)
                {
                    spark = SparkPool[j];
                    break;
                }
            }

            if (spark == null)
            {
                spark = CreateSpark();
            }

            spark.Activate(position);
        }
    }

    private static void PlayZap(Vector3 position)
    {
        if (zapClip == null || zapSources == null || zapSources.Length == 0) return;

        int selectedIndex = nextZapSource;
        for (int i = 0; i < zapSources.Length; i++)
        {
            int candidateIndex = (nextZapSource + i) % zapSources.Length;
            if (!zapSources[candidateIndex].isPlaying)
            {
                selectedIndex = candidateIndex;
                break;
            }
        }

        AudioSource source = zapSources[selectedIndex];
        source.transform.position = position;
        source.clip = zapClip;
        source.volume = SoundManager.SfxVolume;
        source.Play();
        nextZapSource = (selectedIndex + 1) % zapSources.Length;
    }

    private void Initialize()
    {
        if (initialized) return;

        layers = new LineRenderer[LayerCount];
        basePoints = new Vector3[Segments];

        for (int i = 0; i < LayerCount; i++)
        {
            GameObject layerObject;
            if (i == 0)
            {
                layerObject = gameObject;
            }
            else
            {
                layerObject = new GameObject($"BoltLayer_{i}");
                layerObject.transform.SetParent(transform, false);
            }

            LineRenderer line = layerObject.AddComponent<LineRenderer>();
            line.positionCount = Segments;
            line.startWidth = LayerWidths[i];
            line.endWidth = LayerWidths[i] * 0.3f;
            line.useWorldSpace = true;
            line.sortingOrder = 50 + i;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            if (boltMaterials != null)
            {
                line.sharedMaterial = boltMaterials[i];
            }
            layers[i] = line;
        }

        initialized = true;
    }

    private void Activate(Vector3 targetPosition)
    {
        Initialize();

        strikePoint = targetPosition;
        transform.position = targetPosition;
        origin = new Vector3(
            targetPosition.x + Random.Range(-1.2f, 1.2f),
            targetPosition.y - BoltLength,
            targetPosition.z);
        timer = Duration;
        flickerFrames = 0;

        for (int i = 0; i < LayerCount; i++)
        {
            layers[i].startWidth = LayerWidths[i];
            layers[i].endWidth = LayerWidths[i] * 0.3f;
        }

        gameObject.SetActive(true);
        GenerateBolt();
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            gameObject.SetActive(false);
            return;
        }

        flickerFrames++;
        if (flickerFrames >= 2)
        {
            flickerFrames = 0;
            GenerateBolt();
        }

        float alpha = timer / Duration;
        for (int i = 0; i < LayerCount; i++)
        {
            layers[i].startWidth = LayerWidths[i] * alpha;
            layers[i].endWidth = LayerWidths[i] * 0.3f * alpha;
        }
    }

    private void GenerateBolt()
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

        for (int layer = 0; layer < LayerCount; layer++)
        {
            for (int i = 0; i < Segments; i++)
            {
                Vector3 point = basePoints[i];
                point.z += LayerZ[layer];
                if (layer < 2 && i > 0 && i < Segments - 1)
                {
                    point.x += Random.Range(-0.05f, 0.05f);
                }
                layers[layer].SetPosition(i, point);
            }
        }
    }
}

/// <summary>A pooled glowing streak emitted from a lightning strike.</summary>
public sealed class LightningSpark : MonoBehaviour
{
    private const float SparkDuration = 0.35f;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private Vector3 velocity;
    private float life;
    private LineRenderer line;
    private float startWidth;
    private Color startColor;
    private MaterialPropertyBlock propertyBlock;
    private bool initialized;

    public void Initialize(Material material)
    {
        if (initialized) return;

        line = gameObject.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.sortingOrder = 55;
        line.numCapVertices = 3;
        line.sharedMaterial = material;
        line.startColor = Color.white;
        line.endColor = Color.white;
        propertyBlock = new MaterialPropertyBlock();
        initialized = true;
    }

    public void Activate(Vector3 position)
    {
        transform.position = position;

        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float speed = Random.Range(3f, 8f);
        velocity = new Vector3(
            Mathf.Cos(angle) * speed,
            Mathf.Sin(angle) * speed * 0.7f,
            0f);
        velocity.y += Random.Range(0.5f, 2f);

        life = SparkDuration * Random.Range(0.6f, 1f);

        float colorBlend = Random.value;
        if (colorBlend < 0.4f)
            startColor = Color.white;
        else if (colorBlend < 0.7f)
            startColor = new Color(0.7f, 0.85f, 1f, 1f);
        else
            startColor = new Color(1f, 0.9f, 0.4f, 1f);

        startWidth = Random.Range(0.08f, 0.2f);
        line.startWidth = startWidth;
        line.endWidth = startWidth * 0.3f;
        SetColor(startColor);

        gameObject.SetActive(true);
        UpdatePositions();
    }

    private void Update()
    {
        life -= Time.deltaTime;
        if (life <= 0f)
        {
            gameObject.SetActive(false);
            return;
        }

        velocity.y -= 12f * Time.deltaTime;
        velocity *= 1f - 2.5f * Time.deltaTime;
        transform.position += velocity * Time.deltaTime;

        float fraction = life / SparkDuration;
        line.startWidth = startWidth * fraction;
        line.endWidth = startWidth * 0.3f * fraction;

        Color fadedColor = startColor;
        fadedColor.a = fraction;
        SetColor(fadedColor);

        UpdatePositions();
    }

    private void UpdatePositions()
    {
        Vector3 head = transform.position;
        Vector3 tail = head - velocity.normalized * (startWidth * 3f);
        line.SetPosition(0, tail);
        line.SetPosition(1, head);
    }

    private void SetColor(Color color)
    {
        propertyBlock.Clear();
        propertyBlock.SetColor(BaseColorId, color);
        propertyBlock.SetColor(ColorId, color);
        line.SetPropertyBlock(propertyBlock);
    }
}
