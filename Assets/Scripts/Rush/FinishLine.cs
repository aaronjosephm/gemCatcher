using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Full-width level-unlock marker. It falls with the active Rush wave and
/// unlocks the next level when it crosses the catcher's vertical position.
/// </summary>
public sealed class FinishLine : MonoBehaviour
{
    private const float LineHeight = 0.9f;
    private const float HorizontalOverscan = 0.4f;
    private const int CheckerRows = 4;

    private Transform visual;
    private Texture2D checkerTexture;
    private Material runtimeMaterial;
    private float fallSpeed;
    private LevelManager.LevelId targetLevel;
    private bool initialized;
    private bool crossed;

    public float VisualWidth => visual != null ? visual.localScale.x : 0f;
    public bool HasCrossed => crossed;

    public static FinishLine Create(
        float spawnY,
        float speed,
        LevelManager.LevelId levelToUnlock)
    {
        GameObject root = new GameObject("FinishLine");
        FinishLine finishLine = root.AddComponent<FinishLine>();
        finishLine.Initialize(spawnY, speed, levelToUnlock);
        return finishLine;
    }

    public void Initialize(
        float spawnY,
        float speed,
        LevelManager.LevelId levelToUnlock)
    {
        fallSpeed = Mathf.Max(0.1f, speed);
        targetLevel = levelToUnlock;
        BuildVisual();
        FitToPlayArea(spawnY);
        initialized = true;
    }

    void Update()
    {
        if (!initialized || !GameState.IsPlaying || GemCatcher.IsGameOver)
        {
            return;
        }

        transform.Translate(Vector3.down * (fallSpeed * Time.deltaTime), Space.World);
        FitToPlayArea(transform.position.y);

        if (!crossed && transform.position.y <= GetCrossingHeight())
        {
            CompleteCrossing();
        }

        if (transform.position.y + LineHeight * 0.5f < ScreenPadding.WorldBottom)
        {
            Destroy(gameObject);
        }
    }

    void BuildVisual()
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "CheckeredPlane";
        quad.transform.SetParent(transform, false);
        quad.transform.localPosition = Vector3.zero;
        quad.transform.localRotation = Quaternion.identity;
        visual = quad.transform;

        Collider collider = quad.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        float width = Mathf.Max(0.1f, ScreenPadding.WorldWidth + HorizontalOverscan);
        checkerTexture = CreateCheckerTexture(width);

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Texture")
            ?? Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogError("[FinishLine] No compatible unlit shader is available.");
            return;
        }

        runtimeMaterial = new Material(shader)
        {
            name = "Finish Line Checkers (runtime)",
            hideFlags = HideFlags.DontSave,
            mainTexture = checkerTexture,
        };
        if (runtimeMaterial.HasProperty("_BaseMap"))
        {
            runtimeMaterial.SetTexture("_BaseMap", checkerTexture);
        }
        if (runtimeMaterial.HasProperty("_BaseColor"))
        {
            runtimeMaterial.SetColor("_BaseColor", Color.white);
        }
        if (runtimeMaterial.HasProperty("_Cull"))
        {
            runtimeMaterial.SetFloat("_Cull", (float)CullMode.Off);
        }

        Renderer lineRenderer = quad.GetComponent<Renderer>();
        lineRenderer.sharedMaterial = runtimeMaterial;
        lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.lightProbeUsage = LightProbeUsage.Off;
        lineRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        lineRenderer.sortingOrder = 100;
    }

    void FitToPlayArea(float y)
    {
        float left = ScreenPadding.WorldLeft;
        float right = ScreenPadding.WorldRight;
        transform.position = new Vector3((left + right) * 0.5f, y, -0.75f);
        if (visual != null)
        {
            visual.localScale =
                new Vector3(Mathf.Max(0.1f, right - left + HorizontalOverscan), LineHeight, 1f);
        }
    }

    Texture2D CreateCheckerTexture(float worldWidth)
    {
        int columns = Mathf.Clamp(
            Mathf.RoundToInt(worldWidth / LineHeight * CheckerRows),
            8,
            96);
        Texture2D texture = new Texture2D(
            columns, CheckerRows, TextureFormat.RGBA32, false)
        {
            name = "Finish Line Checkers (runtime)",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave,
        };

        Color black = new Color(0.02f, 0.02f, 0.02f, 1f);
        Color[] pixels = new Color[columns * CheckerRows];
        for (int row = 0; row < CheckerRows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                pixels[row * columns + column] =
                    (row + column) % 2 == 0 ? Color.white : black;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return texture;
    }

    float GetCrossingHeight()
    {
        GameObject catcher = CatcherManager.Instance != null
            ? CatcherManager.Instance.CatcherInstance
            : null;
        return catcher != null ? catcher.transform.position.y : ScreenPadding.WorldBottom;
    }

    void CompleteCrossing()
    {
        crossed = true;
        if (LevelManager.IsUnlocked(targetLevel))
        {
            return;
        }

        LevelManager.UnlockLevel(targetLevel);
        LevelManager.LevelConfig config = LevelManager.GetConfig(targetLevel);
        UIManager.Instance?.SpawnBannerNotification(
            $"{config.displayName} UNLOCKED!",
            new Color(1f, 0.85f, 0.2f));
        SoundManager.Instance?.PlayWithPitch("GemCaught", 1.8f);
    }

    void OnDestroy()
    {
        if (runtimeMaterial != null)
        {
            Destroy(runtimeMaterial);
        }
        if (checkerTexture != null)
        {
            Destroy(checkerTexture);
        }
    }
}
