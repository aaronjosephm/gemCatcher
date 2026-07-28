using UnityEngine;

/// <summary>
/// Variant flavor applied to a falling gem at spawn time. Drives points,
/// life-effects, visuals, and miss-handling. The base prefab stays the same;
/// the variant just retints / overrides behavior.
/// </summary>
public enum SpecialGemType
{
    /// <summary>Standard gem — +20 points, normal visuals.</summary>
    Normal,
    /// <summary>Rare +100-point gem with bright gold visuals.</summary>
    Golden,
    /// <summary>DON'T catch — costs a life and breaks combo on contact. Falling through is the correct play.</summary>
    Bomb,
    /// <summary>Ultra-rare jackpot drop — +500 points. Spawned as a procedural brick-shaped GameObject from <see cref="GoldBarFactory"/>; bypasses the gem-pool tint path entirely so the visual is an actual gold bar, not a retinted gem.</summary>
    GoldBar,
}

public class FallingObject : MonoBehaviour
{
    public float fallSpeed = 4.0f;
    public float horizontalSpeed = 0.5f;
    private float initialFallSpeed; // Store the initial fall speed
    private float initialHorizontalSpeed; // Store the initial horizontal speed
    private Vector3 movementDirection;

    // Read-only accessor for the current velocity vector (used by trajectory prediction).
    public Vector3 MovementDirection => movementDirection;
    private Vector3 rotationSpeed;
    private float objectHalfWidth;
    private float objectHalfHeight;
    private Camera mainCamera;
    private float leftBoundary;
    private float rightBoundary;
    private float bottomBoundary;

    // Collision settings
    public float bounceFactor = 0.8f; // How much velocity is preserved after bouncing
    public LayerMask obstacleLayer; // Layer for obstacles

    // Trail effect
    private TrailRenderer trailRenderer;

    // Cached prefab scale captured the first time ApplyScaleFactor runs (or in
    // Start, whichever happens first). All subsequent rescales multiply this
    // baseline by the requested factor so we never compound shrinkage across
    // pooled re-uses.
    private Vector3 originalScale = Vector3.one;
    private bool originalScaleCaptured = false;

    // Decomposed scale state. Final transform.localScale is always
    //   originalScale * (currentBaseScaleFactor * currentSpecialScaleFactor)
    // so the score-driven shrink (set via ApplyScaleFactor) and the uniform
    // special-gem size override (set via ApplySpecialType, e.g. 2x for Bombs)
    // can vary independently without fighting each other. Gold Bars don't
    // need a per-axis stretch field because they're spawned as a separate
    // procedural GameObject (see GoldBarFactory) instead of a stretched gem.
    private float currentBaseScaleFactor = 1f;
    private float currentSpecialScaleFactor = 1f;

    [Header("Special Gem Sizing")]
    [Range(0.5f, 4f)]
    [Tooltip("Visual scale multiplier applied to BOMB gems on top of the score-driven gem-shrink factor. >1 makes bombs harder to dodge by occupying more of the play area horizontally and vertically. The catch hitbox tracks visual size automatically.")]
    public float bombScaleMultiplier = 2f;

    // ---- Special-gem variant ------------------------------------------------
    // Set by ObjectPooler at spawn time via ApplySpecialType. Read by GemCatcher
    // (catch dispatch) and Update (suppress miss penalty for bombs). Pooled gems
    // reset to Normal between spawns so a Bomb-this-cycle never leaks back as a
    // Bomb-next-cycle.

    public SpecialGemType specialType { get; private set; } = SpecialGemType.Normal;

    // ---- Power-up state ----------------------------------------------------
    // When isPowerUp is true, this falling gem is acting as a power-up pickup
    // (a normal gem prefab repainted with the power-up's theme color and
    // wrapped in a fiery aura — see PowerUpFireEffect). Catch dispatch in
    // GemCatcher checks this flag FIRST and routes through the power-up
    // activation path, bypassing the regular variant scoring switch. Set
    // exclusively by ApplyPowerUpType / cleared by ClearPowerUp; the regular
    // SpecialGemType is forced to Normal underneath while a power-up is
    // active so a single gem can never be both a Bomb and a power-up.
    public bool isPowerUp { get; private set; } = false;
    public PowerUpType powerUpType { get; private set; } = PowerUpType.WiderCatcher;

    // Cached "natural" material colors so we can restore them when a pooled gem
    // is reused with a different (or no) special type. Captured lazily on first
    // ApplySpecialType call.
    private Color originalAlbedo;
    private Color originalEmission;
    private bool originalColorsCaptured = false;
    private bool emissionWasEnabled = false;
    private Color originalTrailStart;
    private Color originalTrailEnd;
    private bool originalTrailCaptured = false;

    [Header("Visual")]
    [Tooltip("Color used for the catch burst particles. Auto-detected from gem name if left at default.")]
    public Color burstColor = Color.clear;

    /// <summary>
    /// Returns the burst color, auto-detecting from the gem name if not
    /// explicitly set in the prefab.
    /// </summary>
    public Color GetBurstColor()
    {
        if (burstColor != Color.clear) return burstColor;

        // Infer from gem name.
        string n = gameObject.name.ToLowerInvariant();
        if (n.Contains("green") || n.Contains("emerald"))
            return new Color(0.2f, 0.9f, 0.3f);
        if (n.Contains("red") || n.Contains("ruby"))
            return new Color(0.95f, 0.2f, 0.2f);
        if (n.Contains("star"))
            return new Color(0.95f, 0.2f, 0.2f);
        if (n.Contains("topaz"))
            return new Color(1f, 0.5f, 0.1f);
        if (n.Contains("heart") || n.Contains("pink"))
            return new Color(0.95f, 0.3f, 0.6f);
        if (n.Contains("blue") || n.Contains("sapphire"))
            return new Color(0.2f, 0.6f, 1f);
        if (n.Contains("purple") || n.Contains("amethyst") || n.Contains("violet"))
            return new Color(0.7f, 0.2f, 0.9f);
        if (n.Contains("orange"))
            return new Color(1f, 0.5f, 0.1f);
        if (n.Contains("diamond") || n.Contains("white"))
            return new Color(0.85f, 0.92f, 1f);

        // Fallback: warm gold.
        return new Color(1f, 0.95f, 0.7f);
    }

    void Start()
    {
        // Initialize components and boundaries
        InitializeComponents();
        CaptureOriginalScaleIfNeeded();
    }

    // Method to reset the object when it's reused from the pool
    public void ResetObject()
    {
        // Re-initialize components in case anything has changed
        InitializeComponents();
        // Defensive cleanup of any leftover power-up state from a previous
        // life. ApplyPowerUpType / ApplySpecialType already handle this when
        // called, but calling ClearPowerUp here too ensures any future spawn
        // path that calls ResetObject without immediately repainting the
        // gem (e.g. an editor tool) can't leave a stale fire effect parented
        // to a normal-looking gem.
        ClearPowerUp();
    }

    // Sets the score-driven base scale factor. Final localScale is
    // recomputed as originalScale * factor * specialScaleFactor so a
    // simultaneously-active special-gem size override (e.g. 2x bombs) is
    // preserved. The first call captures the prefab scale as the baseline.
    // Also refreshes the cached renderer half-extents so wall-bounce math
    // stays accurate when this is called on a gem that's already in flight.
    public void ApplyScaleFactor(float factor)
    {
        CaptureOriginalScaleIfNeeded();
        currentBaseScaleFactor = factor;
        ApplyComposedScale();
    }

    // Recomputes localScale from originalScale * base * special and
    // refreshes the renderer half-extents cache. Centralized so any change
    // to either factor (score shrink, uniform special override) routes
    // through a single source of truth.
    private void ApplyComposedScale()
    {
        transform.localScale = originalScale * (currentBaseScaleFactor * currentSpecialScaleFactor);
        RefreshBoundsCache();
    }

    private void RefreshBoundsCache()
    {
        Renderer r = GetComponent<Renderer>();
        if (r != null)
        {
            objectHalfWidth = r.bounds.extents.x;
            objectHalfHeight = r.bounds.extents.y;
        }
    }

    // ---- Special-gem visual application ------------------------------------

    /// <summary>
    /// Apply (or revert) a special-gem variant. Called by ObjectPooler at spawn
    /// time. Caches the prefab's natural colors on first call so passing
    /// SpecialGemType.Normal restores the original look exactly. Also rescales
    /// the gem (e.g. Bombs render bombScaleMultiplier times larger) so a
    /// dangerous bomb is harder to dodge purely by occupying more space.
    /// </summary>
    public void ApplySpecialType(SpecialGemType type)
    {
        specialType = type;
        // Clear any leftover power-up state from the previous time this
        // pooled instance was used. A gem that's now being respawned as a
        // regular variant can't ALSO be carrying a power-up flame from its
        // last life. ClearPowerUp is idempotent and cheap, so it's safe to
        // call unconditionally.
        ClearPowerUp();

        // Update the uniform special-scale factor and recompute localScale.
        // Captured-then-restored on Normal so a pooled gem that was previously
        // a Bomb doesn't carry over its size override to its next life.
        // Bombs render at bombScaleMultiplier; everything else renders at 1x.
        CaptureOriginalScaleIfNeeded();
        currentSpecialScaleFactor = type == SpecialGemType.Bomb ? bombScaleMultiplier : 1f;
        ApplyComposedScale();

        Renderer r = GetComponent<Renderer>();
        if (r != null)
        {
            // .material auto-instances on first access — that's fine for pooled
            // objects; they each get their own material instance once and reuse
            // it for every spawn.
            Material m = r.material;
            CaptureOriginalColorsIfNeeded(m);

            VariantPalette pal = GetPalette(type);
            if (m.HasProperty("_Color"))
            {
                m.color = type == SpecialGemType.Normal ? originalAlbedo : pal.albedo;
            }
            if (m.HasProperty("_EmissionColor"))
            {
                if (type == SpecialGemType.Normal)
                {
                    m.SetColor("_EmissionColor", originalEmission);
                    if (!emissionWasEnabled) m.DisableKeyword("_EMISSION");
                }
                else
                {
                    m.SetColor("_EmissionColor", pal.emission);
                    m.EnableKeyword("_EMISSION");
                }
            }
        }

        TrailRenderer tr = GetComponent<TrailRenderer>();
        if (tr != null)
        {
            CaptureOriginalTrailIfNeeded(tr);
            VariantPalette pal = GetPalette(type);
            if (type == SpecialGemType.Normal)
            {
                tr.startColor = originalTrailStart;
                tr.endColor = originalTrailEnd;
            }
            else
            {
                tr.startColor = pal.trailStart;
                tr.endColor = pal.trailEnd;
            }
        }

        // ClearPowerUp at the top of this method already tore down any
        // leftover power-up flame from a previous pool cycle. Variant gems
        // (Normal / Golden / Bomb / GoldBar) never carry a flame of their
        // own — the magenta fiery aura is reserved exclusively for the
        // ExtraLife power-up, which routes through ApplyPowerUpType — so
        // there's nothing more to attach here.
    }

    /// <summary>
    /// Tags this falling object as a particular variant for catch-time
    /// scoring purposes WITHOUT touching the renderer / trail / scale.
    /// Used by <see cref="ObjectPooler.SpawnGoldBar"/> for procedural Gold
    /// Bars — the bar already has its own gold material and brick-shaped
    /// mesh from <see cref="GoldBarFactory"/>, so retinting it via the
    /// regular palette path would erase the polished metallic look.
    /// </summary>
    public void SetSpecialTypeWithoutVisuals(SpecialGemType type)
    {
        specialType = type;
    }

    /// <summary>
    /// Convert this falling gem into a power-up pickup of the given type.
    /// Repaints the prefab's material and trail in the power-up's theme
    /// color, attaches a tinted fiery aura via <see cref="PowerUpFireEffect"/>,
    /// and flips the <see cref="isPowerUp"/> flag so the catch dispatch and
    /// the off-screen miss handler take the power-up path instead of the
    /// regular variant path.
    ///
    /// <para>The underlying <see cref="specialType"/> is forced to Normal —
    /// a gem can never be both a power-up AND a Bomb / Golden / GoldBar at
    /// the same time, since the catch routing for those would fight the
    /// power-up activation routing.</para>
    /// </summary>
    public void ApplyPowerUpType(PowerUpType type, Color tint)
    {
        // Force the variant back to Normal first — this also wipes any
        // leftover Bomb-scale override (currentSpecialScaleFactor) and
        // restores the prefab's natural colors before we overpaint with the
        // power-up tint, so we don't double-apply on a pooled gem that was
        // previously a Bomb.
        ApplySpecialType(SpecialGemType.Normal);

        isPowerUp = true;
        powerUpType = type;

        // Paint the gem body + trail in the power-up's theme color. Same
        // render-channel hooks as ApplySpecialType uses for variants — we
        // just reach for them again here with the power-up palette.
        Renderer r = GetComponent<Renderer>();
        if (r != null)
        {
            Material m = r.material;
            CaptureOriginalColorsIfNeeded(m);
            if (m.HasProperty("_Color")) m.color = tint;
            if (m.HasProperty("_EmissionColor"))
            {
                // Emission boosted ~1.4x so the gem reads as glowing under
                // the additive flame. Below 1.0 and the fire just looks
                // pasted on; above ~1.6 the gem itself starts to bloom out
                // and lose its shape silhouette.
                m.SetColor("_EmissionColor", tint * 1.4f);
                m.EnableKeyword("_EMISSION");
            }
        }

        TrailRenderer tr = GetComponent<TrailRenderer>();
        if (tr != null)
        {
            CaptureOriginalTrailIfNeeded(tr);
            Color trailHead = tint;
            trailHead.a = 0.95f;
            Color trailTail = tint;
            trailTail.a = 0f;
            tr.startColor = trailHead;
            tr.endColor = trailTail;
        }

        PowerUpFireEffect.Attach(gameObject, tint);
    }

    /// <summary>
    /// Strip the power-up state off this falling gem (flame off, flag off).
    /// Idempotent — safe to call on gems that were never power-ups. Material
    /// + trail color restoration is handled by the next ApplySpecialType
    /// call (which captures-then-restores the prefab's natural look on
    /// Normal); we don't need to roll those back here because every code
    /// path that exits power-up state does so via ApplySpecialType anyway.
    /// </summary>
    public void ClearPowerUp()
    {
        if (!isPowerUp) return;
        isPowerUp = false;
        // Don't touch powerUpType — leaving it at the last value is harmless
        // because callers gate on isPowerUp before reading it.
        PowerUpFireEffect.Detach(gameObject);
    }

    private void CaptureOriginalColorsIfNeeded(Material m)
    {
        if (originalColorsCaptured) return;
        if (m.HasProperty("_Color")) originalAlbedo = m.color;
        if (m.HasProperty("_EmissionColor")) originalEmission = m.GetColor("_EmissionColor");
        emissionWasEnabled = m.IsKeywordEnabled("_EMISSION");
        originalColorsCaptured = true;
    }

    private void CaptureOriginalTrailIfNeeded(TrailRenderer tr)
    {
        if (originalTrailCaptured) return;
        originalTrailStart = tr.startColor;
        originalTrailEnd = tr.endColor;
        originalTrailCaptured = true;
    }

    private struct VariantPalette
    {
        public Color albedo;
        public Color emission;
        public Color trailStart;
        public Color trailEnd;
    }

    private static VariantPalette GetPalette(SpecialGemType type)
    {
        switch (type)
        {
            case SpecialGemType.Golden:
                return new VariantPalette
                {
                    albedo = new Color(1.00f, 0.85f, 0.20f),
                    emission = new Color(1.40f, 1.10f, 0.30f) * 0.9f,
                    trailStart = new Color(1.00f, 0.95f, 0.55f, 0.95f),
                    trailEnd = new Color(1.00f, 0.80f, 0.10f, 0.0f),
                };
            case SpecialGemType.Bomb:
                return new VariantPalette
                {
                    albedo = new Color(0.13f, 0.13f, 0.16f),
                    emission = new Color(1.20f, 0.20f, 0.10f) * 0.8f,
                    trailStart = new Color(1.00f, 0.30f, 0.20f, 0.95f),
                    trailEnd = new Color(0.30f, 0.05f, 0.05f, 0.0f),
                };
            // GoldBar intentionally has no palette entry — Gold Bars are
            // spawned as a separate procedural GameObject (see GoldBarFactory)
            // with their material baked in, so they never traverse this
            // visual-tint code path. If someone calls ApplySpecialType with
            // GoldBar by mistake, they'll get the default white palette
            // (visible but obviously wrong) which surfaces the bug fast
            // instead of silently retinting a procedural bar.
            default:
                return new VariantPalette
                {
                    albedo = Color.white,
                    emission = Color.black,
                    trailStart = Color.white,
                    trailEnd = new Color(1, 1, 1, 0),
                };
        }
    }

    private void CaptureOriginalScaleIfNeeded()
    {
        if (originalScaleCaptured) return;
        originalScale = transform.localScale;
        originalScaleCaptured = true;
    }

    // Method to initialize the object with a specific speed
    public void InitializeMovement(float startingFallSpeed)
    {
        // Store the starting fall speed as the initial speed
        initialFallSpeed = startingFallSpeed;
        fallSpeed = startingFallSpeed;

        // Initialize movement direction with random horizontal component
        // Higher probability of diagonal movement
        float randomDirectionX = Random.Range(-1f, 1f);
        if (Mathf.Abs(randomDirectionX) < 0.3f) // If too vertical, make it more diagonal
        {
            randomDirectionX = Mathf.Sign(randomDirectionX) * Random.Range(0.3f, 0.8f);
        }

        // Calculate and store the actual horizontal speed
        initialHorizontalSpeed = randomDirectionX * horizontalSpeed;

        // Set the movement direction with the initial speeds
        movementDirection = new Vector3(initialHorizontalSpeed, -fallSpeed, 0f);

#if UNITY_EDITOR
        Debug.Log($"Gem initialized with fall speed: {fallSpeed}, horizontal speed: {initialHorizontalSpeed}");
#endif
    }

    // Helper method to initialize components and cache values
    private void InitializeComponents()
    {
        // Set random rotation speed
        rotationSpeed = new Vector3(
            Random.Range(0f, 30f),
            Random.Range(50f, 150f),
            Random.Range(0f, 50f)
        );

        // Cache object dimensions
        if (GetComponent<Renderer>() != null)
        {
            objectHalfWidth = GetComponent<Renderer>().bounds.extents.x;
            objectHalfHeight = GetComponent<Renderer>().bounds.extents.y;
        }

        // Cache camera and calculate boundaries
        mainCamera = Camera.main;
        CalculateBoundaries();

        // Get trail renderer if it exists
        trailRenderer = GetComponent<TrailRenderer>();
        if (trailRenderer != null)
        {
            trailRenderer.enabled = true;
        }
    }

    void Update()
    {
        // Recompute boundaries each frame so they track Screen.safeArea (changes on
        // device rotation / multitasking on mobile). Cheap to recompute.
        CalculateBoundaries();

        float dt = Time.deltaTime;

        // Rotate the object
        transform.Rotate(rotationSpeed * dt);

        // Move the object
        transform.Translate(movementDirection * dt, Space.World);

        // Check and enforce boundaries
        EnforceBoundaries();

        // Check for collisions with obstacles
        CheckObstacleCollisions();

        // Check if the object has fallen past the catcher line — this can only happen
        // if the gem was NOT caught (CatchZone deactivates caught gems via OnTriggerEnter),
        // so we treat reaching the bottom as a miss and deduct a life.
        // We use the same world-bottom that the catcher uses so the gem disappears
        // right at the catcher level instead of slipping behind the gesture bar.
        if (transform.position.y < bottomBoundary - objectHalfHeight)
        {
            // Bombs are SUPPOSED to be missed — letting one fall through is the
            // correct play, so it's silently retired with no miss penalty,
            // no combo break, no floating text.
            if (specialType == SpecialGemType.Bomb)
            {
                gameObject.SetActive(false);
                return;
            }

            // Power-up gems are also silent on miss — the player just loses
            // the buff opportunity. No life lost, no combo break, no
            // revocation of OTHER active power-ups. This matches the legacy
            // capsule-pickup behavior so a redundant power-up roll (e.g. a
            // Wide drop while Wide is already active) can't punish the
            // player for ignoring it.
            if (isPowerUp)
            {
                ClearPowerUp();
                gameObject.SetActive(false);
                return;
            }

            // Report the miss at the gem's last visible position (just above the bottom)
            // so the floating "-10" appears at the edge of play rather than off-screen.
            Vector3 reportPos = transform.position;
            reportPos.y = bottomBoundary + objectHalfHeight;
            GemCatcher.ReportGemMissed(reportPos);
            gameObject.SetActive(false);
        }
    }

    void CalculateBoundaries()
    {
        if (mainCamera != null)
        {
            // Bounce / miss inside the safe play area so gems never disappear behind a
            // notch or gesture bar.
            rightBoundary = ScreenPadding.WorldRight;
            leftBoundary = ScreenPadding.WorldLeft;
            bottomBoundary = ScreenPadding.WorldBottom;
        }
    }

    void EnforceBoundaries()
    {
        Vector3 position = transform.position;
        bool hitWall = false;

        // Check and enforce left boundary
        if (position.x - objectHalfWidth < leftBoundary)
        {
            position.x = leftBoundary + objectHalfWidth + 0.01f; // Add small buffer
            movementDirection.x = Mathf.Abs(movementDirection.x); // Ensure moving right
            hitWall = true;
        }

        // Check and enforce right boundary
        if (position.x + objectHalfWidth > rightBoundary)
        {
            position.x = rightBoundary - objectHalfWidth - 0.01f; // Add small buffer
            movementDirection.x = -Mathf.Abs(movementDirection.x); // Ensure moving left
            hitWall = true;
        }

        if (hitWall)
        {
            transform.position = position;

            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayWithRandomPitch("WallBounce", 0.85f, 1.15f);
            }
        }
    }

    void CheckObstacleCollisions()
    {
        // Cast a ray in the movement direction to detect obstacles. Length is
        // scaled to the actual per-frame movement so we don't over-cast.
        RaycastHit hit;
        float rayDistance = (movementDirection.magnitude * Time.deltaTime) + objectHalfWidth;

        if (Physics.Raycast(transform.position, movementDirection.normalized, out hit, rayDistance, obstacleLayer))
        {
            // Calculate reflection direction
            Vector3 normal = hit.normal;
            Vector3 reflection = Vector3.Reflect(movementDirection, normal);

            // Apply bounce with some energy loss
            movementDirection = reflection * bounceFactor;

            // Ensure we're still falling downward overall (adjust y if needed)
            if (movementDirection.y > 0)
            {
                movementDirection.y *= -0.5f; // Reverse and reduce upward movement
            }

            // Move slightly away from the collision point to prevent sticking
            transform.position = hit.point + (normal * objectHalfWidth * 1.1f);

            // Play bounce sound if available
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayWithRandomPitch("Bounce", 0.8f, 1.2f);
            }
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        // Check if we collided with an obstacle
        if (((1 << collision.gameObject.layer) & obstacleLayer) != 0)
        {
            // Get the collision normal
            Vector3 normal = collision.contacts[0].normal;

            // Calculate reflection direction
            Vector3 reflection = Vector3.Reflect(movementDirection, normal);

            // Apply bounce with some energy loss
            movementDirection = reflection * bounceFactor;

            // Ensure we're still falling downward overall (adjust y if needed)
            if (movementDirection.y > 0)
            {
                movementDirection.y *= -0.5f; // Reverse and reduce upward movement
            }

            // Play bounce sound if available
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayWithRandomPitch("Bounce", 0.8f, 1.2f);
            }
        }
    }

    // Public method to update fall speed (called by ObjectPooler when difficulty changes)
    public void UpdateFallSpeed(float newSpeed)
    {
        // Avoid divide-by-zero if InitializeMovement hasn't been called yet.
        float speedMultiplier = initialFallSpeed > 0f ? newSpeed / initialFallSpeed : 1f;

        fallSpeed = newSpeed;

        // Vector3 is a struct and cannot be null; update both axes unconditionally.
        movementDirection.y = -fallSpeed;

        // Preserve the horizontal direction (sign) but scale the magnitude.
        float currentHorizontalDirection = Mathf.Sign(movementDirection.x);
        float scaledHorizontalSpeed = Mathf.Abs(initialHorizontalSpeed) * speedMultiplier;
        movementDirection.x = currentHorizontalDirection * scaledHorizontalSpeed;

#if UNITY_EDITOR
        Debug.Log($"Speed updated - Vertical: {fallSpeed}, Horizontal: {movementDirection.x}, Multiplier: {speedMultiplier}");
#endif
    }
}
