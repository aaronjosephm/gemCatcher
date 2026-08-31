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
    private GameObject sunglassLensL;
    private GameObject sunglassLensR;
    private GameObject sunglassBridge;
    private GameObject sunglassArmL;
    private GameObject sunglassArmR;
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

    static readonly Color lensColor = new Color(0.05f, 0.05f, 0.1f, 1f);
    static readonly Color frameColor = new Color(0.08f, 0.08f, 0.08f, 1f);
    const float SZ = -0.505f; // slightly in front of eyes

    /// <summary>Add sunglasses overlay to face.</summary>
    public void ApplySunglasses()
    {
        if (hasSunglasses) return;
        hasSunglasses = true;

        // Wide rectangular lenses covering each eye
        sunglassLensL = CreateFacePart("SunglassLensL",
            new Vector3(-0.22f, 0.12f, SZ), new Vector3(0.22f, 0.14f, 0.01f), lensColor);
        sunglassLensR = CreateFacePart("SunglassLensR",
            new Vector3(0.22f, 0.12f, SZ), new Vector3(0.22f, 0.14f, 0.01f), lensColor);

        // Bridge connecting the two lenses
        sunglassBridge = CreateFacePart("SunglassBridge",
            new Vector3(0f, 0.12f, SZ), new Vector3(0.12f, 0.04f, 0.01f), frameColor);

        // Arms extending toward the sides
        sunglassArmL = CreateFacePart("SunglassArmL",
            new Vector3(-0.35f, 0.12f, SZ), new Vector3(0.06f, 0.03f, 0.01f), frameColor);
        sunglassArmR = CreateFacePart("SunglassArmR",
            new Vector3(0.35f, 0.12f, SZ), new Vector3(0.06f, 0.03f, 0.01f), frameColor);
    }

    /// <summary>Remove sunglasses overlay.</summary>
    public void RemoveSunglasses()
    {
        if (!hasSunglasses) return;
        hasSunglasses = false;
        if (sunglassLensL != null) Destroy(sunglassLensL);
        if (sunglassLensR != null) Destroy(sunglassLensR);
        if (sunglassBridge != null) Destroy(sunglassBridge);
        if (sunglassArmL != null) Destroy(sunglassArmL);
        if (sunglassArmR != null) Destroy(sunglassArmR);
    }
}
