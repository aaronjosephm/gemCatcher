using UnityEngine;

/// <summary>
/// Creates Catchy's face on the front of the catcher cube using small
/// procedural quads. Reacts to gem catches (open mouth smile) and
/// misses (crying face), then reverts to default smile.
/// </summary>
public class CatchyFace : MonoBehaviour
{
    // Face parts we need to modify for expressions
    private GameObject leftEye;
    private GameObject rightEye;
    private GameObject smileMid;
    private GameObject smileLeft;
    private GameObject smileRight;

    // Cry expression extras
    private GameObject mouthOpen;
    private GameObject tearLeft;
    private GameObject tearRight;

    // Happy open mouth
    private GameObject happyMouth;

    // Sunglasses overlay parts
    private readonly System.Collections.Generic.List<GameObject> sunglassParts = new System.Collections.Generic.List<GameObject>();
    private bool hasSunglasses;

    private float expressionTimer;
    private const float ExpressionDuration = 0.6f;
    private bool isExpressing;

    static readonly Color dark = new Color(0.1f, 0.1f, 0.15f, 1f);
    static readonly Color tearColor = new Color(0.3f, 0.6f, 1f, 1f);
    const float Z = -0.502f;

    void Start()
    {
        BuildDefaultFace();
        BuildHappyParts();
        BuildSadParts();
        ShowDefault();

        GemCatcher.OnGemCaught += OnCaught;
        GemCatcher.OnGemMissed += OnMissed;
        GemCatcher.OnBombHit += OnBombHit;
    }

    void OnDestroy()
    {
        GemCatcher.OnGemCaught -= OnCaught;
        GemCatcher.OnGemMissed -= OnMissed;
        GemCatcher.OnBombHit -= OnBombHit;
    }

    void Update()
    {
        if (!isExpressing) return;
        expressionTimer -= Time.deltaTime;
        if (expressionTimer <= 0f)
        {
            isExpressing = false;
            ShowDefault();
        }
    }

    void OnCaught(int amount, Vector3 worldPos)
    {
        ShowHappy();
        expressionTimer = ExpressionDuration;
        isExpressing = true;
    }

    void OnMissed(int amount, Vector3 worldPos)
    {
        ShowSad();
        expressionTimer = ExpressionDuration;
        isExpressing = true;
    }

    void OnBombHit(Vector3 worldPos)
    {
        ShowSad();
        expressionTimer = ExpressionDuration;
        isExpressing = true;
    }

    // ---- Face building ----

    void BuildDefaultFace()
    {
        leftEye = CreateFacePart("LeftEye", new Vector3(-0.22f, 0.12f, Z), new Vector3(0.14f, 0.14f, 0.01f), dark);
        rightEye = CreateFacePart("RightEye", new Vector3(0.22f, 0.12f, Z), new Vector3(0.14f, 0.14f, 0.01f), dark);
        smileMid = CreateFacePart("SmileMid", new Vector3(0f, -0.18f, Z), new Vector3(0.32f, 0.06f, 0.01f), dark);
        smileLeft = CreateFacePart("SmileLeft", new Vector3(-0.16f, -0.14f, Z), new Vector3(0.06f, 0.06f, 0.01f), dark);
        smileRight = CreateFacePart("SmileRight", new Vector3(0.16f, -0.14f, Z), new Vector3(0.06f, 0.06f, 0.01f), dark);
    }

    void BuildHappyParts()
    {
        // Open mouth (taller rectangle below smile)
        happyMouth = CreateFacePart("HappyMouth", new Vector3(0f, -0.22f, Z), new Vector3(0.24f, 0.14f, 0.01f), dark);
        happyMouth.SetActive(false);
    }

    void BuildSadParts()
    {
        // Open sad mouth (small O shape)
        mouthOpen = CreateFacePart("SadMouth", new Vector3(0f, -0.2f, Z), new Vector3(0.16f, 0.12f, 0.01f), dark);

        // Tears (small vertical streaks below eyes)
        tearLeft = CreateFacePart("TearLeft", new Vector3(-0.22f, -0.02f, Z), new Vector3(0.06f, 0.14f, 0.01f), tearColor);
        tearRight = CreateFacePart("TearRight", new Vector3(0.22f, -0.02f, Z), new Vector3(0.06f, 0.14f, 0.01f), tearColor);

        mouthOpen.SetActive(false);
        tearLeft.SetActive(false);
        tearRight.SetActive(false);
    }

    // ---- Expressions ----

    void ShowDefault()
    {
        // Normal smile
        smileMid.SetActive(true);
        smileLeft.SetActive(true);
        smileRight.SetActive(true);
        leftEye.transform.localScale = new Vector3(0.14f, 0.14f, 0.01f);
        rightEye.transform.localScale = new Vector3(0.14f, 0.14f, 0.01f);

        // Hide others
        happyMouth.SetActive(false);
        mouthOpen.SetActive(false);
        tearLeft.SetActive(false);
        tearRight.SetActive(false);
    }

    void ShowHappy()
    {
        // Keep smile + add open mouth
        smileMid.SetActive(true);
        smileLeft.SetActive(true);
        smileRight.SetActive(true);
        happyMouth.SetActive(true);

        // Squint eyes (shorter height = happy squint)
        leftEye.transform.localScale = new Vector3(0.16f, 0.08f, 0.01f);
        rightEye.transform.localScale = new Vector3(0.16f, 0.08f, 0.01f);

        // Hide sad
        mouthOpen.SetActive(false);
        tearLeft.SetActive(false);
        tearRight.SetActive(false);
    }

    void ShowSad()
    {
        // Hide smile, show sad mouth + tears
        smileMid.SetActive(false);
        smileLeft.SetActive(false);
        smileRight.SetActive(false);
        happyMouth.SetActive(false);

        mouthOpen.SetActive(true);
        tearLeft.SetActive(true);
        tearRight.SetActive(true);

        // Wide eyes (taller = surprised/sad)
        leftEye.transform.localScale = new Vector3(0.12f, 0.18f, 0.01f);
        rightEye.transform.localScale = new Vector3(0.12f, 0.18f, 0.01f);
    }

    // ---- Helper ----

    GameObject CreateFacePart(string name, Vector3 localPos, Vector3 localScale, Color color)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Quad);
        part.name = name;
        part.transform.SetParent(transform, false);
        part.transform.localPosition = localPos;
        part.transform.localRotation = Quaternion.identity;
        part.transform.localScale = localScale;

        Destroy(part.GetComponent<Collider>());

        Renderer rend = part.GetComponent<Renderer>();
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetColor("_BaseColor", color);
        rend.material = mat;

        return part;
    }

    // ---- Sunglasses ----

    static readonly Color lensColor = new Color(0.04f, 0.04f, 0.08f, 1f);
    static readonly Color lensHighlight = new Color(0.15f, 0.2f, 0.3f, 1f);
    static readonly Color frameColor = new Color(0.06f, 0.06f, 0.08f, 1f); // black frame
    const float SZ = -0.505f;

    GameObject SG(string n, Vector3 p, Vector3 s, Color c)
    {
        var go = CreateFacePart(n, p, s, c);
        sunglassParts.Add(go);
        return go;
    }

    /// <summary>Add aviator sunglasses overlay to face.</summary>
    public void ApplySunglasses()
    {
        if (hasSunglasses) return;
        hasSunglasses = true;

        // Each aviator lens = teardrop shape built from stacked quads
        // Wide at top, tapers down
        float lx = -0.22f, rx = 0.22f;

        // Left lens — top wide part
        SG("SunglassL1", new Vector3(lx, 0.16f, SZ), new Vector3(0.24f, 0.06f, 0.01f), lensColor);
        // Left lens — main body
        SG("SunglassL2", new Vector3(lx, 0.10f, SZ), new Vector3(0.22f, 0.08f, 0.01f), lensColor);
        // Left lens — bottom taper
        SG("SunglassL3", new Vector3(lx, 0.04f, SZ), new Vector3(0.16f, 0.06f, 0.01f), lensColor);
        // Left lens — teardrop tip
        SG("SunglassL4", new Vector3(lx, 0.00f, SZ), new Vector3(0.10f, 0.04f, 0.01f), lensColor);
        // Left lens highlight (reflective glint)
        SG("SunglassHL", new Vector3(lx - 0.04f, 0.15f, -0.507f), new Vector3(0.06f, 0.03f, 0.01f), lensHighlight);

        // Right lens — mirror of left
        SG("SunglassR1", new Vector3(rx, 0.16f, SZ), new Vector3(0.24f, 0.06f, 0.01f), lensColor);
        SG("SunglassR2", new Vector3(rx, 0.10f, SZ), new Vector3(0.22f, 0.08f, 0.01f), lensColor);
        SG("SunglassR3", new Vector3(rx, 0.04f, SZ), new Vector3(0.16f, 0.06f, 0.01f), lensColor);
        SG("SunglassR4", new Vector3(rx, 0.00f, SZ), new Vector3(0.10f, 0.04f, 0.01f), lensColor);
        SG("SunglassHR", new Vector3(rx + 0.04f, 0.15f, -0.507f), new Vector3(0.06f, 0.03f, 0.01f), lensHighlight);

        // Top frame bar across both lenses
        SG("SunglassTopBar", new Vector3(0f, 0.19f, SZ), new Vector3(0.50f, 0.025f, 0.01f), frameColor);

        // Double bridge (two thin bars)
        SG("SunglassBridge1", new Vector3(0f, 0.16f, SZ), new Vector3(0.08f, 0.02f, 0.01f), frameColor);
        SG("SunglassBridge2", new Vector3(0f, 0.13f, SZ), new Vector3(0.06f, 0.02f, 0.01f), frameColor);

        // Arms extending to the sides
        SG("SunglassArmL", new Vector3(-0.38f, 0.17f, SZ), new Vector3(0.12f, 0.02f, 0.01f), frameColor);
        SG("SunglassArmR", new Vector3(0.38f, 0.17f, SZ), new Vector3(0.12f, 0.02f, 0.01f), frameColor);
    }

    /// <summary>Remove sunglasses overlay.</summary>
    public void RemoveSunglasses()
    {
        if (!hasSunglasses) return;
        hasSunglasses = false;
        foreach (var go in sunglassParts)
            if (go != null) Destroy(go);
        sunglassParts.Clear();
    }

    // ---- Eye Patch ----

    private readonly System.Collections.Generic.List<GameObject> eyepatchParts = new System.Collections.Generic.List<GameObject>();
    private bool hasEyepatch;

    static readonly Color patchColor = new Color(0.08f, 0.06f, 0.04f, 1f); // dark brown/black leather
    static readonly Color strapColor = new Color(0.1f, 0.08f, 0.06f, 1f);

    GameObject EP(string n, Vector3 p, Vector3 s, Color c)
    {
        var go = CreateFacePart(n, p, s, c);
        eyepatchParts.Add(go);
        return go;
    }

    /// <summary>Add eye patch overlay to face (covers left eye).</summary>
    public void ApplyEyepatch()
    {
        if (hasEyepatch) return;
        hasEyepatch = true;

        float ex = -0.22f; // left eye X

        // Patch — rounded-ish shape from stacked quads
        EP("EyepatchMain", new Vector3(ex, 0.12f, SZ), new Vector3(0.20f, 0.16f, 0.01f), patchColor);
        EP("EyepatchTop",  new Vector3(ex, 0.21f, SZ), new Vector3(0.14f, 0.04f, 0.01f), patchColor);
        EP("EyepatchBot",  new Vector3(ex, 0.03f, SZ), new Vector3(0.14f, 0.04f, 0.01f), patchColor);

        // Strap going diagonally across face — upper right
        EP("StrapUR", new Vector3(0.0f,  0.24f, SZ), new Vector3(0.34f, 0.03f, 0.01f), strapColor);
        EP("StrapTop", new Vector3(0.18f, 0.27f, SZ), new Vector3(0.12f, 0.03f, 0.01f), strapColor);
        // Strap going diagonally — lower right
        EP("StrapLR", new Vector3(0.0f,  0.0f, SZ),  new Vector3(0.34f, 0.03f, 0.01f), strapColor);
        EP("StrapBot", new Vector3(0.18f, -0.03f, SZ), new Vector3(0.12f, 0.03f, 0.01f), strapColor);
    }

    /// <summary>Remove eye patch overlay.</summary>
    public void RemoveEyepatch()
    {
        if (!hasEyepatch) return;
        hasEyepatch = false;
        foreach (var go in eyepatchParts)
            if (go != null) Destroy(go);
        eyepatchParts.Clear();
    }
}
