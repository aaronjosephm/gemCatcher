using UnityEngine;
using UnityEngine.UI;

public class CatcherManager : MonoBehaviour
{
    public GameObject catcherPrefab; // The catcher (cube) prefab
    private GameObject catcherInstance;

    public int numberOfSlots = 8; // Number of sections (slots) at the bottom
    public float slotHeight = 1.0f; // Height of the slot areas at the bottom
    public float slotWidth; // Width will be dynamically calculated based on the screen size

    private Vector3[] slotPositions; // Store the positions of the slots

    // UI elements
    public Text scoreText; // Reference to UI Text component for displaying score
    public Text placementTimerText; // Text to display the remaining placement time

    // Visual feedback
    public GameObject slotHighlightPrefab; // Optional: prefab for highlighting the selected slot
    private GameObject[] slotHighlights; // Array to store slot highlight instances

    // Placement phase
    private bool isPlacementPhase = false;
    private float placementTimer = 0f;
    private ObjectPooler objectPooler;

    // Tracking for the rotate-during-placement / settle-after animation.
    private bool wasInPlacementPhase = false;
    private Quaternion catcherSettleStart;
    private float catcherSettleTimer = -1f;

    // Trajectory prediction
    public LineRenderer trajectoryLine;
    public int trajectoryPoints = 10;
    public float trajectoryTimeStep = 0.1f;

    [Header("Glass Catcher Appearance")]
    [Tooltip("Tint and transparency of the glass catcher. Alpha controls how see-through it is.")]
    public Color glassColor = new Color(0.65f, 0.85f, 1.00f, 0.30f);
    [Range(0f, 1f)]
    [Tooltip("How smooth/shiny the glass surface is. 1 = mirror-like.")]
    public float glassSmoothness = 0.95f;
    [Range(0f, 1f)]
    [Tooltip("Metallic factor. Keep low for glass; a touch of metallic gives it some sheen.")]
    public float glassMetallic = 0.1f;

    [Header("Placement Phase Spin")]
    [Tooltip("Degrees per second the catcher spins while the placement countdown is running.")]
    public float catcherSpinSpeed = 120f;
    [Tooltip("Local axis around which the catcher spins during placement.")]
    public Vector3 catcherSpinAxis = new Vector3(0.4f, 1f, 0.2f);
    [Tooltip("Seconds the catcher takes to settle back to upright once the placement phase ends.")]
    public float catcherSettleDuration = 0.25f;

    [Header("Sparkle")]
    [Tooltip("If true, a subtle particle sparkle is attached to the catcher.")]
    public bool enableSparkle = true;
    [Range(0f, 30f)]
    [Tooltip("Particles spawned per second. Keep small for a tasteful shimmer.")]
    public float sparkleRate = 5f;
    [Range(0.01f, 0.3f)]
    public float sparkleSize = 0.07f;
    public Color sparkleColor = new Color(1f, 1f, 1f, 0.9f);

    void Start()
    {
        // Find the object pooler and subscribe to its lifecycle events.
        objectPooler = FindObjectOfType<ObjectPooler>();
        if (objectPooler != null)
        {
            objectPooler.GemSpawned += OnGemSpawned;
        }

        // Lay out slots inside the safe play area (excludes phone notches / camera lens
        // and lifts off the bottom of the screen). See ScreenPadding for the bounds.
        float playLeft = ScreenPadding.WorldLeft;
        float playRight = ScreenPadding.WorldRight;
        float playBottom = ScreenPadding.WorldBottom;
        float playWidth = Mathf.Max(0.01f, playRight - playLeft);
        slotWidth = playWidth / numberOfSlots;

        // Initialize slot positions
        slotPositions = new Vector3[numberOfSlots];
        slotHighlights = new GameObject[numberOfSlots];

        float startX = playLeft + slotWidth / 2.0f; // Starting x position for the slots

        // Catcher sits inside the safe area's bottom edge (raised above the gesture/home bar).
        for (int i = 0; i < numberOfSlots; i++)
        {
            slotPositions[i] = new Vector3(startX + i * slotWidth, playBottom + slotHeight / 2.0f, 0f);

            // Create slot highlights if prefab is assigned
            if (slotHighlightPrefab != null)
            {
                slotHighlights[i] = Instantiate(slotHighlightPrefab, slotPositions[i], Quaternion.identity);
                slotHighlights[i].transform.localScale = new Vector3(slotWidth * 0.9f, slotHeight * 0.9f, 1f);
                slotHighlights[i].SetActive(false); // Initially inactive
            }
        }

        // Create initial catcher in the middle slot
        PlaceCatcherInSlot(numberOfSlots / 2);

        // Subscribe to score change events
        GemCatcher.OnScoreChanged += UpdateScoreDisplay;

        // Initialize score display
        UpdateScoreDisplay(0);

        // Initialize trajectory line if assigned
        if (trajectoryLine != null)
        {
            trajectoryLine.positionCount = trajectoryPoints;
            trajectoryLine.enabled = false;
        }
    }

    void Update()
    {
        // Check if we're in the placement phase
        if (objectPooler != null)
        {
            isPlacementPhase = objectPooler.IsInPlacementPhase();
            placementTimer = objectPooler.GetPlacementTimeRemaining();

            // Update the placement timer text
            UpdatePlacementTimerDisplay();

            // Update trajectory prediction if in placement phase
            if (isPlacementPhase && trajectoryLine != null)
            {
                UpdateTrajectoryPrediction();
            }
            else if (trajectoryLine != null)
            {
                trajectoryLine.enabled = false;
            }
        }

        // The catcher can be repositioned freely while the placement countdown is active —
        // every click during the phase moves it to the clicked slot.
        if (isPlacementPhase && Input.GetMouseButtonDown(0))
        {
            Vector3 clickPosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            clickPosition.z = 0f;

            int slotIndex = GetSlotFromClick(clickPosition);
            if (slotIndex != -1)
            {
                PlaceCatcherInSlot(slotIndex);
            }
        }

        UpdateCatcherSpin();
    }

    // Spin the catcher while the placement phase is active so the player can see at a glance
    // that they can still pick a slot. When the phase ends, ease the catcher back to upright.
    void UpdateCatcherSpin()
    {
        if (catcherInstance == null)
        {
            wasInPlacementPhase = isPlacementPhase;
            return;
        }

        if (isPlacementPhase)
        {
            Vector3 axis = catcherSpinAxis.sqrMagnitude > 0f ? catcherSpinAxis.normalized : Vector3.up;
            catcherInstance.transform.Rotate(axis, catcherSpinSpeed * Time.deltaTime, Space.Self);
            catcherSettleTimer = -1f; // Cancel any in-progress settle.
        }
        else if (wasInPlacementPhase)
        {
            // Phase just ended this frame — kick off the settle animation.
            catcherSettleStart = catcherInstance.transform.rotation;
            catcherSettleTimer = 0f;
        }
        else if (catcherSettleTimer >= 0f)
        {
            catcherSettleTimer += Time.deltaTime;
            float t = catcherSettleDuration > 0f
                ? Mathf.Clamp01(catcherSettleTimer / catcherSettleDuration)
                : 1f;
            // Ease-out cubic for a soft landing.
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            catcherInstance.transform.rotation = Quaternion.Slerp(catcherSettleStart, Quaternion.identity, eased);
            if (t >= 1f) catcherSettleTimer = -1f;
        }

        wasInPlacementPhase = isPlacementPhase;
    }

    void UpdateScoreDisplay(int newScore)
    {
        if (scoreText != null)
        {
            scoreText.text = "Score: " + newScore;
        }
    }

    void UpdatePlacementTimerDisplay()
    {
        if (placementTimerText != null)
        {
            if (isPlacementPhase)
            {
                placementTimerText.text = "Place Catcher: " + placementTimer.ToString("F1") + "s";
                placementTimerText.gameObject.SetActive(true);
            }
            else
            {
                placementTimerText.gameObject.SetActive(false);
            }
        }
    }

    void UpdateTrajectoryPrediction()
    {
        // Pull the active gem directly from the pooler instead of scanning the scene.
        GameObject activeGem = objectPooler != null ? objectPooler.CurrentActiveGem : null;
        if (activeGem == null || !activeGem.activeInHierarchy)
        {
            trajectoryLine.enabled = false;
            return;
        }

        FallingObject fallingObj = activeGem.GetComponent<FallingObject>();
        if (fallingObj == null)
        {
            trajectoryLine.enabled = false;
            return;
        }

        trajectoryLine.enabled = true;

        // Use the gem's actual movement direction so the prediction respects the real sign.
        Vector3 position = activeGem.transform.position;
        Vector3 velocity = fallingObj.MovementDirection;

        trajectoryLine.SetPosition(0, position);

        // Bounce trajectory off the same play-area walls the gem actually uses.
        float left = ScreenPadding.WorldLeft;
        float right = ScreenPadding.WorldRight;
        for (int i = 1; i < trajectoryPoints; i++)
        {
            position += velocity * trajectoryTimeStep;

            if (position.x < left)
            {
                position.x = left;
                velocity.x = -velocity.x;
            }
            else if (position.x > right)
            {
                position.x = right;
                velocity.x = -velocity.x;
            }

            trajectoryLine.SetPosition(i, position);
        }
    }

    int GetSlotFromClick(Vector3 clickPosition)
    {
        // Catcher sits in a band of slotHeight above the safe-area bottom. Allow taps
        // anywhere from the very bottom of the camera up to the top of the catcher
        // band so the clickable area still feels generous on phones with thick bezels.
        float catcherBandTop = ScreenPadding.WorldBottom + slotHeight;
        float bottomY = -Camera.main.orthographicSize;

        if (clickPosition.y >= bottomY && clickPosition.y <= catcherBandTop)
        {
            float startX = ScreenPadding.WorldLeft;
            int slotIndex = (int)((clickPosition.x - startX) / slotWidth);

            // Ensure the slotIndex is within the valid range
            if (slotIndex >= 0 && slotIndex < numberOfSlots)
            {
                return slotIndex;
            }
        }

        // If the click was outside the slot area
        return -1;
    }

    void PlaceCatcherInSlot(int slotIndex)
    {
        // Deactivate all slot highlights first
        for (int i = 0; i < slotHighlights.Length; i++)
        {
            if (slotHighlights[i] != null)
            {
                slotHighlights[i].SetActive(i == slotIndex); // Only activate the selected slot
            }
        }

        // If catcher doesn't exist, create it
        if (catcherInstance == null)
        {
            catcherInstance = Instantiate(catcherPrefab, slotPositions[slotIndex], Quaternion.identity);
            catcherInstance.tag = "Catcher"; // Ensure it has the correct tag
            ApplyGlassAppearance(catcherInstance);
            AddSparkleEffect(catcherInstance);
        }
        else
        {
            // Move the existing catcher to the new position
            catcherInstance.transform.position = slotPositions[slotIndex];
        }

        // Play sound effect if available
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.Play("CatcherMove");
        }
    }

    // Called by ObjectPooler when a new gem is spawned
    void OnGemSpawned()
    {
        // Reset the catcher position to the middle slot when a new gem spawns
        PlaceCatcherInSlot(numberOfSlots / 2);
    }

    // Swaps every Renderer on the catcher prefab over to a translucent glass material so
    // the player can see falling gems through it. Called once when the catcher is first
    // instantiated.
    void ApplyGlassAppearance(GameObject catcherObject)
    {
        if (catcherObject == null) return;

        Material glass = CreateGlassMaterial();

        Renderer[] renderers = catcherObject.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            // Replace every submaterial on the renderer with the glass material. Using a
            // single shared material keeps the per-instance Material count low (this only
            // ever runs once, but it's still tidier).
            Material[] mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = glass;
            r.sharedMaterials = mats;
        }
    }

    // Adds a low-rate particle system to the catcher so it gives off a subtle sparkle.
    // Tunable through the "Sparkle" header on this component.
    void AddSparkleEffect(GameObject catcherObject)
    {
        if (!enableSparkle || catcherObject == null) return;

        GameObject sparkleHost = new GameObject("Sparkles");
        sparkleHost.transform.SetParent(catcherObject.transform, false);
        sparkleHost.transform.localPosition = Vector3.zero;
        sparkleHost.transform.localRotation = Quaternion.identity;
        sparkleHost.transform.localScale = Vector3.one;

        ParticleSystem ps = sparkleHost.AddComponent<ParticleSystem>();
        // Stop the system before reconfiguring; calling Play at the end ensures it picks up
        // the latest module values cleanly.
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 5f;
        main.loop = true;
        main.startLifetime = 0.7f;
        main.startSize = sparkleSize;
        main.startSpeed = 0.15f;
        main.startColor = sparkleColor;
        main.maxParticles = 80;
        // World simulation so the sparkle "trails" stay put while the catcher moves between slots.
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;

        var emission = ps.emission;
        emission.rateOverTime = sparkleRate;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = Vector3.one * 0.9f;

        // Fade-in / fade-out alpha so each particle pops in and out gently.
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.35f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = g;

        // Default particle material — small bright sprites that work in the built-in pipeline.
        ParticleSystemRenderer renderer = sparkleHost.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            Shader particleShader = Shader.Find("Sprites/Default");
            if (particleShader != null)
            {
                renderer.material = new Material(particleShader);
            }
        }

        ps.Play();
    }

    Material CreateGlassMaterial()
    {
        // Built-in Render Pipeline (Standard shader). If the project later moves to URP,
        // swap this shader name for "Universal Render Pipeline/Lit".
        Shader standard = Shader.Find("Standard");
        Material mat = new Material(standard);

        // Configure the Standard shader for its "Transparent" rendering mode. This block
        // mirrors what the Inspector does when you switch the Rendering Mode dropdown.
        mat.SetFloat("_Mode", 3f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHABLEND_ON");
        mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        mat.color = glassColor;
        mat.SetFloat("_Glossiness", glassSmoothness);
        mat.SetFloat("_Metallic", glassMetallic);

        return mat;
    }

    void OnDestroy()
    {
        // Unsubscribe from events when this object is destroyed
        GemCatcher.OnScoreChanged -= UpdateScoreDisplay;

        if (objectPooler != null)
        {
            objectPooler.GemSpawned -= OnGemSpawned;
        }
    }
}
