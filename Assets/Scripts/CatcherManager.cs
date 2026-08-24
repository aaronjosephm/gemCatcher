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

    // Drag-to-reposition: once the player presses in the catcher band (or on the
    // catcher itself), the catcher follows the pointer's X smoothly. Sound /
    // haptic fire once on finger-up, not while sliding.
    private bool isDraggingCatcher = false;

    // Tracking for the rotate-during-placement / settle-after animation.
    private bool wasInPlacementPhase = false;
    private Quaternion catcherSettleStart;
    private float catcherSettleTimer = -1f;

    // Trajectory prediction
    public LineRenderer trajectoryLine;
    public int trajectoryPoints = 10;
    public float trajectoryTimeStep = 0.1f;

    [Header("Catcher Appearance")]
    [Tooltip("Tint of the catcher.")]
    public Color glassColor = new Color(0.65f, 0.85f, 1.00f, 1.00f);
    [Range(0f, 1f)]
    [Tooltip("Catcher transparency. 0 = fully transparent, 1 = fully opaque.")]
    public float glassOpacity = 1f;
    [Range(0f, 1f)]
    [Tooltip("Surface smoothness. Keep low so the catcher stays a solid color and does not mirror falling gems.")]
    public float glassSmoothness = 0.25f;
    [Range(0f, 1f)]
    [Tooltip("Metallic factor. Keep at 0 so gem colors are not reflected into the catcher.")]
    public float glassMetallic = 0f;

    [Header("Placement Phase Spin")]
    [Tooltip("Degrees per second the catcher spins while the placement countdown is running.")]
    public float catcherSpinSpeed = 120f;
    [Tooltip("Local axis around which the catcher spins during placement.")]
    public Vector3 catcherSpinAxis = new Vector3(0.4f, 1f, 0.2f);
    [Tooltip("Seconds the catcher takes to settle back to upright once the placement phase ends.")]
    public float catcherSettleDuration = 0.25f;

    [Header("Difficulty: Shrinking Catcher")]
    [Range(0.1f, 1f)]
    [Tooltip("Uniform scale factor applied to the catcher from the start of a round. Lower = harder out of the gate. The catcher visually shrinks to this value while the player is below smallCatcherScoreThreshold, then drops further to smallCatcherScaleFactor once they cross it.")]
    public float baseCatcherScaleFactor = 0.7f;
    [Tooltip("Score at which the catcher shrinks further. Set very high to disable the second-stage shrink.")]
    public int smallCatcherScoreThreshold = 2000;
    [Range(0.1f, 1f)]
    [Tooltip("Uniform scale factor applied to the catcher once smallCatcherScoreThreshold is reached. Should be smaller than baseCatcherScaleFactor for the difficulty bump to be perceptible.")]
    public float smallCatcherScaleFactor = 0.5f;

    [Header("Sparkle")]
    [Tooltip("If true, a subtle particle sparkle is attached to the catcher.")]
    public bool enableSparkle = true;
    [Range(0f, 30f)]
    [Tooltip("Particles spawned per second. Keep small for a tasteful shimmer.")]
    public float sparkleRate = 5f;
    [Range(0.01f, 0.3f)]
    public float sparkleSize = 0.07f;
    public Color sparkleColor = new Color(1f, 1f, 1f, 0.9f);

    // Tracks the running "feedback" pose offset. Update() interpolates this back to
    // identity each frame; OnGemCaught/OnGemMissed kick it away from identity so the
    // catcher squashes / jolts in response to gameplay events.
    private Vector3 catcherBaseScale = Vector3.one;
    private Vector3 feedbackScale = Vector3.one;
    private Vector3 feedbackPositionOffset = Vector3.zero;
    private float feedbackJitterRemaining = 0f;
    private float feedbackJitterMagnitude = 0f;
    // Authoritative "rest" position for the current slot — Update() overlays the
    // feedback offset on top of this each frame so jolts/squashes can't drift the
    // catcher away from its slot.
    private Vector3 catcherAnchorPosition;

    // Wider-Catcher power-up scales the catcher's X size up smoothly on activate
    // and back down on expire. Held outside `feedbackScale` so squash/jitter
    // animations multiply with it without fighting it.
    private float widerCatcherFactor = 1f;
    private float widerCatcherTargetFactor = 1f;

    // Score-driven uniform shrink. Activates when the player crosses
    // smallCatcherScoreThreshold; lerped here so the size change reads as a
    // smooth difficulty ramp rather than a teleport.
    private float smallCatcherFactor = 1f;
    private float smallCatcherTargetFactor = 1f;

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
        // Visual feedback on gameplay events.
        GemCatcher.OnGemCaught += HandleGemCaughtFeedback;
        GemCatcher.OnGemMissed += HandleGemMissedFeedback;
        // Wider-Catcher power-up — smoothly scale the cube's X axis on activate / expire.
        PowerUpManager.OnActivated += HandlePowerUpActivated;
        PowerUpManager.OnExpired += HandlePowerUpExpired;

        // Initialize score display. Pull from GemCatcher.Score (rather than
        // hard-coding 0) so that on a hot-reload or scene re-entry, the
        // catcher's small-catcher target syncs with whatever the current
        // score actually is. In practice this is 0 at round start.
        UpdateScoreDisplay(GemCatcher.Score);
        // Snap the lerped factor to the target on first frame so we don't
        // animate from 1.0 → 0.5 if the round somehow starts mid-game.
        smallCatcherFactor = smallCatcherTargetFactor;

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

        HandleCatcherPlacementInput();

        UpdateCatcherSpin();
        UpdateCatcherFeedback();
    }

    // Tap or drag during the placement countdown. Drag follows the finger
    // smoothly along X (no slot snapping mid-drag). Sound / haptic play once
    // when the finger lifts, not while sliding.
    // Rush Mode: tap left/right half of screen to step in that direction.
    void HandleCatcherPlacementInput()
    {
        // In continuous mode the catcher is always movable.
        bool canMove = isPlacementPhase
                       || GameState.Mode == GameState.GameMode.Rush;
        if (!canMove)
        {
            isDraggingCatcher = false;
            return;
        }

        if (Camera.main == null) return;

        // Rush Mode: tap-to-move instead of drag.
        if (GameState.Mode == GameState.GameMode.Rush)
        {
            HandleRushTapInput();
            return;
        }

        Vector3 pointerWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        pointerWorld.z = 0f;

        if (Input.GetMouseButtonDown(0))
        {
            bool startDrag = GetSlotFromClick(pointerWorld) != -1
                || IsPointerNearCatcher(pointerWorld);
            if (startDrag)
            {
                isDraggingCatcher = true;
                // Silent move under the finger immediately — feedback waits for lift.
                MoveCatcherToX(pointerWorld.x, playFeedback: false);
            }
        }
        else if (Input.GetMouseButton(0) && isDraggingCatcher)
        {
            MoveCatcherToX(pointerWorld.x, playFeedback: false);
        }
        else if (Input.GetMouseButtonUp(0) && isDraggingCatcher)
        {
            // Final position under the finger, then one confirmation sound.
            MoveCatcherToX(pointerWorld.x, playFeedback: true);
            isDraggingCatcher = false;
        }
    }

    // Rush Mode tap controls: tap left half → move one column left,
    // tap right half → move one column right.
    private int rushCurrentColumn = 2; // Start in center column (0-indexed)
    void HandleRushTapInput()
    {
        if (catcherInstance == null) return;

        if (Input.GetMouseButtonDown(0))
        {
            float screenMid = Screen.width * 0.5f;
            if (Input.mousePosition.x < screenMid)
            {
                rushCurrentColumn = Mathf.Max(0, rushCurrentColumn - 1);
            }
            else
            {
                rushCurrentColumn = Mathf.Min(RushColumns.Count - 1, rushCurrentColumn + 1);
            }

            float targetX = RushColumns.GetColumnX(rushCurrentColumn);
            MoveCatcherToX(targetX, playFeedback: true);
        }
    }

    // Smooth free-X placement along the catcher row. Clamped so the catcher's
    // body stays inside the safe play area.
    void MoveCatcherToX(float worldX, bool playFeedback)
    {
        float catcherY = ScreenPadding.WorldBottom + slotHeight / 2.0f + LevelManager.CurrentConfig.catcherYOffset;
        float halfExtent = GetCatcherHalfWidth();
        float minX = ScreenPadding.WorldLeft + halfExtent;
        float maxX = ScreenPadding.WorldRight - halfExtent;
        if (minX > maxX)
        {
            // Degenerate (catcher wider than play area) — pin to center.
            float mid = (ScreenPadding.WorldLeft + ScreenPadding.WorldRight) * 0.5f;
            minX = maxX = mid;
        }
        float x = Mathf.Clamp(worldX, minX, maxX);
        MoveCatcherTo(new Vector3(x, catcherY, 0f), playFeedback);
    }

    float GetCatcherHalfWidth()
    {
        // Prefer live renderer bounds when available; fall back to slot width
        // scaled by the same factors UpdateCatcherFeedback applies.
        if (catcherInstance != null)
        {
            Renderer r = catcherInstance.GetComponentInChildren<Renderer>();
            if (r != null) return Mathf.Max(0.05f, r.bounds.extents.x);
        }
        return (slotWidth * 0.45f) * widerCatcherFactor * smallCatcherFactor;
    }

    // Tweens the catcher back to its rest pose after a catch (squash) or miss (jitter)
    // event nudged it away. Runs every frame so feedback is independent of frame rate.
    void UpdateCatcherFeedback()
    {
        if (catcherInstance == null) return;

        // Smoothly ease scale back to (1,1,1); the multiplicative form means a "0.78
        // squash" recovers exponentially without overshooting.
        feedbackScale = Vector3.Lerp(feedbackScale, Vector3.one, Mathf.Clamp01(14f * Time.deltaTime));

        // Compute the desired offset from the miss-jitter, then ease toward it.
        Vector3 targetOffset = Vector3.zero;
        if (feedbackJitterRemaining > 0f)
        {
            feedbackJitterRemaining -= Time.deltaTime;
            float t = Mathf.Clamp01(feedbackJitterRemaining / 0.18f);
            float amp = feedbackJitterMagnitude * t;
            targetOffset = new Vector3(
                (Random.value - 0.5f) * 2f * amp,
                (Random.value - 0.5f) * amp,
                0f);
        }
        feedbackPositionOffset = Vector3.Lerp(feedbackPositionOffset, targetOffset, Mathf.Clamp01(20f * Time.deltaTime));

        // Smoothly ease wider-catcher factor toward its target. The target jumps
        // to 1.6 on activate and back to 1.0 on expire; the lerp keeps it from
        // snapping and breaking the player's depth perception of the catcher.
        widerCatcherFactor = Mathf.Lerp(
            widerCatcherFactor,
            widerCatcherTargetFactor,
            Mathf.Clamp01(8f * Time.deltaTime));

        // Same smoothing for the score-driven small-catcher shrink — slower
        // so the player has a beat to register the size change.
        smallCatcherFactor = Mathf.Lerp(
            smallCatcherFactor,
            smallCatcherTargetFactor,
            Mathf.Clamp01(4f * Time.deltaTime));

        // Apply: anchor (the actual slot) + transient offset, with combined scale.
        // Order of operations:
        //   baseScale × feedback (squash/jitter, transient)
        //     → multiply X by widerCatcherFactor (power-up, X-only)
        //     → multiply ALL axes by smallCatcherFactor (difficulty, uniform)
        // GemCatcher.IsGemWithinCatcherBounds reads collider.size × lossyScale
        // every frame, so the catch hitbox tracks the visual size automatically.
        Transform t2 = catcherInstance.transform;
        t2.position = catcherAnchorPosition + feedbackPositionOffset;
        Vector3 finalScale = Vector3.Scale(catcherBaseScale, feedbackScale);
        finalScale.x *= widerCatcherFactor;
        finalScale *= smallCatcherFactor;
        t2.localScale = finalScale;
    }

    void HandlePowerUpActivated(PowerUpType type, float duration)
    {
        if (type == PowerUpType.WiderCatcher)
        {
            widerCatcherTargetFactor = PowerUpManager.WiderCatcherWidthFactor;
        }
    }

    void HandlePowerUpExpired(PowerUpType type)
    {
        if (type == PowerUpType.WiderCatcher)
        {
            widerCatcherTargetFactor = 1f;
        }
    }

    // Squash the catcher quickly to (1.25, 0.78, 1.25) on a successful catch so it looks
    // like it absorbed an impact, then UpdateCatcherFeedback eases it back.
    void HandleGemCaughtFeedback(int amount, Vector3 worldPosition)
    {
        feedbackScale = new Vector3(1.25f, 0.78f, 1.25f);
    }

    // Brief jitter on a miss — feels like the catcher took the hit even though it didn't
    // catch the gem.
    void HandleGemMissedFeedback(int amount, Vector3 worldPosition)
    {
        feedbackJitterRemaining = 0.18f;
        feedbackJitterMagnitude = 0.12f;
        feedbackScale = new Vector3(0.92f, 1.08f, 0.92f);
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

        // Drive the difficulty shrink off score crossings. Below the
        // threshold the catcher sits at baseCatcherScaleFactor (the
        // out-of-the-gate baseline); above the threshold it drops to the
        // tighter smallCatcherScaleFactor for the rest of the round (Score
        // is monotonic non-decreasing now that misses don't subtract points).
        smallCatcherTargetFactor = newScore >= smallCatcherScoreThreshold
            ? smallCatcherScaleFactor
            : baseCatcherScaleFactor;
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
            return GetSlotFromX(clickPosition.x);
        }

        // If the click was outside the slot area
        return -1;
    }

    // Maps a world X coordinate to a slot index, clamping into the playable
    // horizontal range. Used by drag so the catcher keeps tracking even when
    // the finger is above the catcher band.
    int GetSlotFromX(float worldX)
    {
        float startX = ScreenPadding.WorldLeft;
        float playWidth = Mathf.Max(0.01f, ScreenPadding.WorldRight - ScreenPadding.WorldLeft);
        // Clamp into the play area so dragging past the edges parks on the
        // end slots instead of returning -1 and freezing the catcher.
        float clampedX = Mathf.Clamp(worldX, startX, startX + playWidth - 0.001f);
        int slotIndex = (int)((clampedX - startX) / slotWidth);
        if (slotIndex < 0 || slotIndex >= numberOfSlots) return -1;
        return slotIndex;
    }

    // Generous hit test so the player can grab the catcher by its visual body,
    // not only by the thin slot strip under it.
    bool IsPointerNearCatcher(Vector3 worldPos)
    {
        if (catcherInstance == null) return false;
        Vector3 catcherPos = catcherAnchorPosition;
        float halfW = slotWidth * 0.75f * widerCatcherFactor * smallCatcherFactor;
        float halfH = slotHeight * 0.85f;
        return Mathf.Abs(worldPos.x - catcherPos.x) <= halfW
            && Mathf.Abs(worldPos.y - catcherPos.y) <= halfH;
    }

    void PlaceCatcherInSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= numberOfSlots) return;
        MoveCatcherTo(slotPositions[slotIndex], playFeedback: true);
    }

    void MoveCatcherTo(Vector3 worldPosition, bool playFeedback)
    {
        // Highlight the nearest slot under the catcher (visual only).
        int nearestSlot = GetSlotFromX(worldPosition.x);
        for (int i = 0; i < slotHighlights.Length; i++)
        {
            if (slotHighlights[i] != null)
            {
                slotHighlights[i].SetActive(i == nearestSlot);
            }
        }

        if (catcherInstance == null)
        {
            catcherInstance = Instantiate(catcherPrefab, worldPosition, Quaternion.identity);
            catcherInstance.tag = "Catcher";

            // Kinematic Rigidbody prevents physics forces from moving/rotating the catcher.
            Rigidbody rb = catcherInstance.GetComponent<Rigidbody>();
            if (rb == null) rb = catcherInstance.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            ApplyGlassAppearance(catcherInstance);
            AddSparkleEffect(catcherInstance);
            // Ensure CatchZone is attached for trigger-based catch detection.
            if (catcherInstance.GetComponent<CatchZone>() == null)
            {
                catcherInstance.AddComponent<CatchZone>();
            }
            // Attach Catchy's face to the front of the cube.
            if (catcherInstance.GetComponent<CatchyFace>() == null)
            {
                catcherInstance.AddComponent<CatchyFace>();
            }
            catcherBaseScale = catcherInstance.transform.localScale;
            feedbackScale = Vector3.one;
        }
        else
        {
            catcherInstance.transform.position = worldPosition;
        }
        catcherAnchorPosition = worldPosition;

        if (!playFeedback) return;

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.Play("CatcherMove");
        }
        if (HapticManager.Instance != null)
        {
            HapticManager.Instance.Trigger(HapticManager.Intensity.Light);
        }
    }

    // Called by ObjectPooler when a new gem is spawned
    void OnGemSpawned()
    {
        // Skip the auto-reset to middle if a power-up pickup from the previous
        // cycle is still mid-air. Otherwise the catcher gets ripped out from
        // under a pickup the player just lined up — the next gem's cycle fires
        // GemSpawned ~2.5 seconds after the pickup spawn, and pickups take
        // longer than that to reach the catch line. Player can still tap to
        // reposition during the new gem's placement phase if they want.
        if (objectPooler != null && objectPooler.HasActivePickupInFlight) return;

        // Reset the catcher position to the middle slot when a new gem spawns
        PlaceCatcherInSlot(numberOfSlots / 2);
    }

    // Swaps every Renderer on the catcher prefab over to the solid catcher material.
    // Called once when the catcher is first instantiated.
    void ApplyGlassAppearance(GameObject catcherObject)
    {
        if (catcherObject == null) return;

        Material glass = CreateGlassMaterial();

        Renderer[] renderers = catcherObject.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            Material[] mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = glass;
            r.sharedMaterials = mats;
        }
    }

    // Adds a sparkle particle system to the catcher with a procedural 4-point
    // star texture. Each sparkle flashes in, holds briefly, then fades out with
    // a gentle rotation — looks like light glinting off glass/crystal.
    void AddSparkleEffect(GameObject catcherObject)
    {
        if (!enableSparkle || catcherObject == null) return;

        GameObject sparkleHost = new GameObject("Sparkles");
        sparkleHost.transform.SetParent(catcherObject.transform, false);
        sparkleHost.transform.localPosition = Vector3.zero;
        sparkleHost.transform.localRotation = Quaternion.identity;
        sparkleHost.transform.localScale = Vector3.one;

        ParticleSystem ps = sparkleHost.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 5f;
        main.loop = true;
        main.startLifetime = 0.5f;
        main.startSize = new ParticleSystem.MinMaxCurve(sparkleSize * 0.6f, sparkleSize * 1.4f);
        main.startSpeed = 0.05f;
        main.startColor = sparkleColor;
        main.maxParticles = 40;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        // Random rotation so each star is oriented differently.
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        var emission = ps.emission;
        emission.rateOverTime = sparkleRate;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = Vector3.one * 0.9f;

        // Size over lifetime: scale up fast, hold, then shrink — a "flash" pop.
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 0f);
        sizeCurve.AddKey(0.15f, 1f);
        sizeCurve.AddKey(0.6f, 1f);
        sizeCurve.AddKey(1f, 0f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // Fade-in / fade-out alpha.
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
                new GradientAlphaKey(1f, 0.15f),
                new GradientAlphaKey(1f, 0.5f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = g;

        // Gentle spin while alive.
        var rotOverLifetime = ps.rotationOverLifetime;
        rotOverLifetime.enabled = true;
        rotOverLifetime.z = new ParticleSystem.MinMaxCurve(-1f, 1f);

        // Use the procedural star texture for a proper sparkle look.
        ParticleSystemRenderer renderer = sparkleHost.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                                ?? Shader.Find("Sprites/Default");
            if (particleShader != null)
            {
                Material mat = new Material(particleShader);
                mat.mainTexture = CreateSparkleStarTexture();
                // Additive blend for bright sparkle on any surface.
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 1f);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = 3100;
                renderer.material = mat;
            }
        }

        ps.Play();
    }

    // Procedural 4-point star texture for sparkle particles.
    static Texture2D s_sparkleStarTex;
    static Texture2D CreateSparkleStarTexture()
    {
        if (s_sparkleStarTex != null) return s_sparkleStarTex;

        int size = 64;
        s_sparkleStarTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        s_sparkleStarTex.filterMode = FilterMode.Bilinear;
        s_sparkleStarTex.wrapMode = TextureWrapMode.Clamp;
        float center = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - center) / center;
                float dy = (y - center) / center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                // Four-point star: bright cross rays + central glow.
                float hRay = Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Abs(dy) * 4f), 2f)
                           * Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Abs(dx)), 0.8f);
                float vRay = Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Abs(dx) * 4f), 2f)
                           * Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Abs(dy)), 0.8f);
                float glow = Mathf.Pow(Mathf.Max(0f, 1f - dist * 1.5f), 3f);

                float a = Mathf.Clamp01(Mathf.Max(hRay, vRay) + glow);
                s_sparkleStarTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        s_sparkleStarTex.Apply(false, true);
        return s_sparkleStarTex;
    }

    Material CreateGlassMaterial()
    {
        // Try URP Lit first, fall back to Built-in Standard.
        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");
        Material mat = new Material(shader);

        Color col = glassColor;
        col.a = glassOpacity;

        bool transparent = glassOpacity < 1f;

        if (mat.HasProperty("_BaseColor"))
        {
            // URP Lit path
            mat.SetColor("_BaseColor", col);
            mat.SetFloat("_Smoothness", glassSmoothness);
            mat.SetFloat("_Metallic", glassMetallic);

            if (transparent)
            {
                // Switch URP Lit to Transparent surface type.
                mat.SetFloat("_Surface", 1f); // 0 = Opaque, 1 = Transparent
                mat.SetFloat("_Blend", 0f);   // 0 = Alpha blend
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }

            // Disable specular highlights so gems don't tint the catcher.
            mat.SetFloat("_SpecularHighlights", 0f);
            mat.SetFloat("_EnvironmentReflections", 0f);
            mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            // Ensure catcher stays below bloom threshold (no emission).
            mat.SetColor("_EmissionColor", Color.black);
            mat.DisableKeyword("_EMISSION");
        }
        else
        {
            // Built-in Standard fallback
            if (transparent)
            {
                mat.SetFloat("_Mode", 3f); // Transparent
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else
            {
                mat.SetFloat("_Mode", 0f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                mat.SetInt("_ZWrite", 1);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.DisableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            }
            mat.color = col;
            mat.SetFloat("_Glossiness", glassSmoothness);
            mat.SetFloat("_Metallic", glassMetallic);
            mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            mat.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
            mat.SetFloat("_SpecularHighlights", 0f);
            mat.SetFloat("_GlossyReflections", 0f);
        }

        return mat;
    }

    void OnDestroy()
    {
        // Unsubscribe from events when this object is destroyed
        GemCatcher.OnScoreChanged -= UpdateScoreDisplay;
        GemCatcher.OnGemCaught -= HandleGemCaughtFeedback;
        GemCatcher.OnGemMissed -= HandleGemMissedFeedback;
        PowerUpManager.OnActivated -= HandlePowerUpActivated;
        PowerUpManager.OnExpired -= HandlePowerUpExpired;

        if (objectPooler != null)
        {
            objectPooler.GemSpawned -= OnGemSpawned;
        }
    }
}
