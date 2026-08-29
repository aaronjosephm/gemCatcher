using System;
using System.Collections.Generic;
using UnityEngine;

public class ObjectPooler : MonoBehaviour
{
    [System.Serializable]
    public class DifficultyLevel
    {
        public float fallSpeed = 4.0f;
        public float spawnInterval = 3.0f; // Increased to give player time to place catcher
        public int scoreThreshold = 10;
    }

    public GameObject[] objectPrefabs;  // Array of prefabs to be pooled
    public int poolSizePerPrefab = 5;   // Number of objects to pool per prefab
    public float initialFallSpeed = 3.0f; // Initial speed at which objects fall
    public float placementPhaseFallSpeed = 0.5f; // Slower speed during placement phase
    public float spawnXRange = 0.65f;    // Range in the X-axis where objects can spawn
    public float initialSpawnInterval = 3.0f; // Time between spawns initially

    // Obstacle settings
    public GameObject[] obstaclePrefabs; // Array of obstacle prefabs
    public int maxObstacles = 3; // Maximum number of obstacles on screen
    public float obstacleSpawnChance = 0.3f; // Chance to spawn an obstacle with each gem

    [Header("Power-Up Pickups")]
    [Tooltip("Number of opening drops that are guaranteed gems (no power-ups). The first power-up appears on the drop AFTER this many drops have happened. Defaults to 10 so players warm up on plain gems before the first power-up arrives.")]
    public int powerUpWarmupDrops = 10;
    [Tooltip("Cadence between power-ups after the warmup ends. With powerUpWarmupDrops=10 and powerUpEveryNDrops=10, power-ups arrive on drops 11, 21, 31, ... — exactly one every ten cycles, fully deterministic.")]
    public int powerUpEveryNDrops = 10;
    [Tooltip("Hard cap on simultaneous active pickups. Kept at 1 so only one pickup is ever in flight at a time — the player must catch (or miss) it before another appears. Acts as a safety net; the deterministic schedule already prevents back-to-back pickups.")]
    public int maxActivePickups = 1;

    [Header("Internal Test Modes")]
    [Tooltip("DEV-ONLY. When enabled, every spawn cycle is a power-up — no regular gems, no obstacles, no warmup, no cadence — and the spawner cycles deterministically through all PowerUpType values in enum order (WiderCatcher → Shield → DoubleScore → ExtraLife → repeat) so you can eyeball each variant's mesh + tint + fiery aura in one session. Daily-mode gem cap is also bypassed so the round doesn't end after 30 'gems' in test mode. MUST be off in shipped builds.")]
    public bool powerUpOnlyTestMode = false;

    [Tooltip("Score threshold at which gems start spawning at half size. Set to a very large number to disable.")]
    public int smallGemScoreThreshold = 1000;
    [Range(0.1f, 1f)]
    [Tooltip("Multiplier applied to gem localScale once smallGemScoreThreshold is reached.")]
    public float smallGemScaleFactor = 0.5f;

    [Header("Special Gem Frequency")]
    [Range(0f, 1f)]
    [Tooltip("Chance per gem spawn that the gem is a Golden gem (+100 points).")]
    public float goldenGemChance = 0.05f;
    [Range(0f, 1f)]
    [Tooltip("Chance per gem spawn that the gem is a Bomb (don't catch — costs a life and breaks combo on contact).")]
    public float bombGemChance = 0.07f;

    // Difficulty progression
    public DifficultyLevel[] difficultyLevels;
    private int currentDifficultyLevel = 0;

    // Daily Challenge tuning. Used only when GameState.Mode == Daily; the linear
    // ramp replaces the score-threshold tiers above so every player faces the
    // exact same speed curve regardless of how many gems they catch.
    [Header("Daily Challenge Curve")]
    [Tooltip("Fall speed for the final gem of a daily round (linear ramp from initialFallSpeed).")]
    public float dailyMaxFallSpeed = 5.5f;
    [Tooltip("Spawn interval at the end of a daily round (linear ramp from initialSpawnInterval).")]
    public float dailyMinSpawnInterval = 2.0f;

    private List<GameObject> objectPool; // The pool of objects
    private List<GameObject> obstaclePool; // The pool of obstacles
    private List<GameObject> activeObstacles; // Currently active obstacles
    private GameObject currentActiveGem; // Currently active gem
    private List<GameObject> activePickups = new List<GameObject>(); // Active power-up pickups

    /// <summary>
    /// True while at least one power-up pickup is mid-air (spawned but neither
    /// caught nor fallen off-screen). Used by <see cref="CatcherManager"/> to
    /// suppress the middle-slot auto-reset that normally fires on every new
    /// gem spawn — without this, a pickup that's still falling will get its
    /// catcher snatched out from under it the instant the next gem cycle
    /// begins (~2.5 seconds after the pickup spawn).
    /// </summary>
    public bool HasActivePickupInFlight
    {
        get
        {
            for (int i = 0; i < activePickups.Count; i++)
            {
                GameObject p = activePickups[i];
                if (p != null && p.activeInHierarchy) return true;
            }
            return false;
        }
    }

    private float nextSpawnTime;
    private float currentFallSpeed;
    private float currentSpawnInterval;

    // Timer for catcher placement
    private float placementTimer = 3.0f;
    private float placementDuration = 3.0f;
    private bool isPlacementPhase = false;
    private float blinkTimer = 0f;

    // True when the current placement cycle dropped a pickup instead of a gem.
    // The placement phase still runs (so the player can reposition the catcher
    // and still has the catcher-spin visual), but the early-termination check
    // — which normally kills the phase as soon as the active gem despawns —
    // must be skipped because there's no gem at all this cycle.
    private bool placementForPickupOnly = false;

    // Total number of spawn cycles since round start (1-indexed). Drives the
    // deterministic power-up cadence: pickups spawn on cycles where this
    // counter exceeds powerUpWarmupDrops AND has the right modulus relative
    // to powerUpEveryNDrops. Reset on round restart and game over.
    private int dropCounter = 0;

    // Internal-test-mode index. Cycles WiderCatcher → Shield → DoubleScore →
    // ExtraLife → repeat across pickup spawns when powerUpOnlyTestMode is on
    // so the developer sees every variant in deterministic order without
    // having to wait for RNG to surface each one. Reset on round restart so
    // a fresh play session always starts at WiderCatcher.
    private int testModePowerUpIndex = 0;

    /// <summary>
    /// Read-only accessor: which <see cref="PowerUpType"/> the test-mode
    /// spawner will use on its next pickup. Used by the test-mode debug
    /// overlay in <see cref="UIManager"/> so the developer can see what's
    /// coming before it actually drops. Always returns a valid enum value
    /// regardless of <see cref="powerUpOnlyTestMode"/>'s state — callers
    /// should gate their UI on the toggle separately.
    /// </summary>
    public PowerUpType TestModeNextPowerUp
    {
        get
        {
            int typeCount = System.Enum.GetValues(typeof(PowerUpType)).Length;
            return (PowerUpType)(testModePowerUpIndex % System.Math.Max(1, typeCount));
        }
    }

    // ---- Deterministic RNG (Daily Challenge) ---------------------------------
    // All random choices that affect gameplay (gem type, X position, horizontal
    // motion, obstacle chance/position) route through this instance instead of
    // UnityEngine.Random.Range. In Normal mode it's seeded with the wall clock;
    // in Daily mode it's seeded with DailyChallenge.TodaySeed, which gives every
    // player on Earth the identical gem sequence on a given UTC date.
    private System.Random rng;

    // Daily-mode bookkeeping. gemCapForRound > 0 caps total gems and triggers
    // GemCatcher.EndGame() when the cap is reached.
    private int gemCapForRound;
    private int gemsSpawnedThisRound;

    // MasterGem (Deep Space only) — spawns once when score reaches threshold.
    private GameObject masterGemInstance;
    private bool masterGemSpawned;
    private const int MasterGemScoreThreshold = 100;

    /// <summary>
    /// Experimental mode: gems spawn continuously (every 0.5s) with no
    // ---- Rush Mode ---------------------------------------------------------
    // (Wave-based spawning is handled entirely by SpawnDirector.)

    // Public events for gameplay state changes. Subscribers should unsubscribe in OnDestroy.
    public event Action GemSpawned;
    public event Action<float> PlacementPhaseStarted; // payload = duration
    public event Action<float> PlacementTimerUpdated; // payload = remaining time
    public event Action PlacementPhaseEnded;

    // Public accessor so other scripts (e.g. CatcherManager) don't need FindObjectsOfType.
    public GameObject CurrentActiveGem => currentActiveGem;

    void Start()
    {
        // Seed the RNG before any spawn-side code runs. Daily mode uses a
        // date-derived seed so iOS, Android, and standalone players see the
        // same gem sequence on the same UTC day.
        if (GameState.Mode == GameState.GameMode.Daily)
        {
            rng = new System.Random(DailyChallenge.TodaySeed);
            gemCapForRound = DailyChallenge.GemsPerRound;
        }
        else
        {
            rng = new System.Random();
            gemCapForRound = 0; // 0 = no cap, run forever
        }
        gemsSpawnedThisRound = 0;

        // PowerUpManager survives scene reloads (DontDestroyOnLoad), so any
        // active power-ups from the previous round would otherwise carry over.
        // Wipe state silently — the new round's HUD is fresh, nothing to hide.
        PowerUpManager.ClearAll();

        // Combo / milestone state is in static classes that survive scene
        // reloads. Clear them so the new round starts at zero combo and no
        // milestones reached.
        ComboManager.ClearSilently();
        MilestoneTracker.ResetForNewRound();

        // Fresh round — no spawn cycles have happened yet, so the deterministic
        // cadence resets. First powerUpWarmupDrops cycles will be gems-only,
        // then every powerUpEveryNDrops cycles thereafter is a guaranteed
        // power-up.
        dropCounter = 0;
        placementForPickupOnly = false;
        // Test mode also restarts at the first PowerUpType so each session
        // begins with WiderCatcher and walks through the enum in order.
        testModePowerUpIndex = 0;

        // Initialize the pools
        objectPool = new List<GameObject>();
        obstaclePool = new List<GameObject>();
        activeObstacles = new List<GameObject>();

        // Set initial difficulty values — override from LevelManager if not in
        // Daily mode (daily mode uses its own ramp parameters).
        ApplyLevelDifficulty();

        // If no difficulty levels are defined, create a default progression
        if (difficultyLevels == null || difficultyLevels.Length == 0)
        {
            difficultyLevels = new DifficultyLevel[]
            {
                new DifficultyLevel { fallSpeed = 3.0f, spawnInterval = 3.0f, scoreThreshold = 0 },
                new DifficultyLevel { fallSpeed = 3.5f, spawnInterval = 2.8f, scoreThreshold = 10 },
                new DifficultyLevel { fallSpeed = 4.0f, spawnInterval = 2.6f, scoreThreshold = 25 },
                new DifficultyLevel { fallSpeed = 4.5f, spawnInterval = 2.4f, scoreThreshold = 50 },
                new DifficultyLevel { fallSpeed = 5.0f, spawnInterval = 2.2f, scoreThreshold = 100 }
            };
        }

        // Rush Mode needs a larger gem pool for cluster spawning.
        int effectivePoolSize = poolSizePerPrefab;
        if (GameState.Mode == GameState.GameMode.Rush)
            effectivePoolSize = 20;

        // Initialize gem pool
        foreach (GameObject prefab in objectPrefabs)
        {
            for (int i = 0; i < effectivePoolSize; i++)
            {
                GameObject obj = Instantiate(prefab);
                obj.SetActive(false); // Initially hide the object

                // Set up the falling object component
                FallingObject fallingObj = obj.GetComponent<FallingObject>();
                if (fallingObj != null)
                {
                    // Initialize with normal speed, but it will be set to slow speed when spawned
                    fallingObj.fallSpeed = currentFallSpeed;
                }

                objectPool.Add(obj);
            }
        }

        // Load level-specific extra gem prefabs from Resources.
        var cfg = LevelManager.CurrentConfig;
        placementDuration = cfg.placementDuration > 0f ? cfg.placementDuration : 3f;
        if (cfg.extraGemPrefabs != null)
        {
            foreach (string path in cfg.extraGemPrefabs)
            {
                GameObject extraPrefab = Resources.Load<GameObject>(path);
                if (extraPrefab == null) continue;
                for (int i = 0; i < effectivePoolSize; i++)
                {
                    GameObject obj = Instantiate(extraPrefab);
                    obj.SetActive(false);
                    FallingObject fallingObj = obj.GetComponent<FallingObject>();
                    if (fallingObj != null)
                        fallingObj.fallSpeed = currentFallSpeed;
                    objectPool.Add(obj);
                }
            }
        }

        // Load MasterGem for Deep Space level.
        if (cfg.id == LevelManager.LevelId.Space)
        {
            GameObject masterPrefab = Resources.Load<GameObject>("Gems/MasterGem");
            if (masterPrefab != null)
            {
                masterGemInstance = Instantiate(masterPrefab);
                masterGemInstance.SetActive(false);
                // Scale to match regular gems (prefab is 1x, game gems are 4x).
                masterGemInstance.transform.localScale = new Vector3(4f, 4f, 4f);
                // Center child meshes on the pivot so it moves like a normal gem
                // (the prefab has children offset from center which causes orbiting).
                foreach (Transform child in masterGemInstance.transform)
                {
                    child.localPosition = Vector3.zero;
                }
                FallingObject fo = masterGemInstance.GetComponent<FallingObject>();
                if (fo == null) fo = masterGemInstance.AddComponent<FallingObject>();
                fo.fallSpeed = currentFallSpeed;
                // NOT added to objectPool — kept separate so it can't be randomly selected.
            }
        }
        masterGemSpawned = false;

        // Initialize obstacle pool if there are obstacle prefabs
        if (obstaclePrefabs != null && obstaclePrefabs.Length > 0)
        {
            foreach (GameObject prefab in obstaclePrefabs)
            {
                for (int i = 0; i < maxObstacles; i++)
                {
                    GameObject obj = Instantiate(prefab);
                    obj.SetActive(false); // Initially hide the obstacle
                    obstaclePool.Add(obj);
                }
            }
        }

        // Subscribe to score change events to update difficulty
        GemCatcher.OnScoreChanged += CheckDifficultyProgression;
        GemCatcher.OnGameOver += HandleGameOver;

        // Start the spawning process
        nextSpawnTime = Time.time + currentSpawnInterval;

        // In Rush mode, attach SpawnDirector to handle wave-based spawning.
        if (GameState.Mode == GameState.GameMode.Rush)
        {
            SpawnDirector director = GetComponent<SpawnDirector>();
            if (director == null) director = gameObject.AddComponent<SpawnDirector>();
        }
    }

    void HandleGameOver()
    {
        // Yank the active gem off the screen and stop spawning. The Update loop early-exits
        // while GemCatcher.IsGameOver is true.
        if (currentActiveGem != null && currentActiveGem.activeInHierarchy)
        {
            currentActiveGem.SetActive(false);
        }
        if (isPlacementPhase)
        {
            EndPlacementPhase(applySpeedup: false);
        }
        placementForPickupOnly = false;
        dropCounter = 0;

        // Despawn any pickups still falling so they don't survive into the
        // game-over screen / next round. Pickups now ride on pooled gem
        // instances (see TrySpawnPowerUp), so we SetActive(false) rather
        // than Destroy — destroying would leak the pool slot and a HeartGem
        // / StarGem / TopazGem / GreenVolcom mesh would be permanently
        // unavailable for the rest of the run. ClearPowerUp tears down the
        // fiery aura so the next time this instance is pulled from the
        // pool it doesn't carry a stale flame.
        for (int i = activePickups.Count - 1; i >= 0; i--)
        {
            GameObject p = activePickups[i];
            if (p == null) continue;
            FallingObject fo = p.GetComponent<FallingObject>();
            if (fo != null) fo.ClearPowerUp();
            p.SetActive(false);
        }
        activePickups.Clear();

        // Also deactivate all pooled gems that are still in flight (Rush Mode
        // can have multiple gems active simultaneously).
        if (objectPool != null)
        {
            for (int i = 0; i < objectPool.Count; i++)
            {
                if (objectPool[i] != null && objectPool[i].activeInHierarchy)
                    objectPool[i].SetActive(false);
            }
        }
    }

    void Update()
    {
        // Wait on the main menu — no spawns, no countdowns, no difficulty progression
        // until the player presses Play.
        if (!GameState.IsPlaying) return;

        // Freeze gem/obstacle spawning once the player runs out of lives.
        if (GemCatcher.IsGameOver) return;

        // Rush Mode: tick heart gem timer.
        if (GameState.Mode == GameState.GameMode.Rush)
        {
            rushHeartGemTimer += Time.deltaTime;
            if (rushHeartGemTimer >= RushHeartGemInterval)
            {
                rushHeartGemTimer = 0f;
                rushHeartGemReady = true;
            }
        }

        bool gemInactive = currentActiveGem == null || !currentActiveGem.activeInHierarchy;

        // If the gem became inactive (caught or fell off-screen) mid-placement, end the phase now
        // rather than letting it tick down on a non-existent gem. Skip this for
        // pickup-only cycles — those have no gem in flight by design and should
        // run their full 3-second placement so the player can reposition.
        if (gemInactive && isPlacementPhase && !placementForPickupOnly)
        {
            EndPlacementPhase(applySpeedup: false);
        }

        // In tutorial mode, don't auto-spawn — TutorialManager drives spawns.
        // But still run placement phase blinking for tutorial-triggered spawns.
        if (GameState.IsTutorial)
        {
            UpdatePlacementBlink();
            return;
        }

        // In Daily mode, end the round once the gem cap is reached AND the last
        // gem is no longer in flight. Triggering EndGame() routes through the
        // existing OnGameOver path, so UIManager's game-over panel handles the
        // results screen the same way it does for a Normal-mode death.
        if (gemCapForRound > 0
            && gemsSpawnedThisRound >= gemCapForRound
            && gemInactive
            && !GemCatcher.IsGameOver)
        {
            GemCatcher.EndGame();
            return;
        }

        // ---- Rush Mode: wave-based spawning via SpawnDirector ----
        // SpawnDirector handles all gem/hazard spawning in Rush mode.
        // ObjectPooler just skips its own spawn loop.
        if (GameState.Mode == GameState.GameMode.Rush)
        {
            return;
        }

        // Check if the current gem is inactive and it's time to spawn a new one.
        // Pickup-only cycles also block re-entry until placementTimer elapses.
        if (gemInactive && !isPlacementPhase && Time.time >= nextSpawnTime)
        {
            // Daily mode: stop spawning once we've hit the per-round cap. The
            // block above ends the round on the next frame after the final gem
            // resolves.
            if (gemCapForRound > 0 && gemsSpawnedThisRound >= gemCapForRound)
            {
                return;
            }

            // Daily mode: smoothly ramp difficulty by gem index so the curve is
            // identical for every player. Normal mode keeps using the score-
            // threshold tiers in CheckDifficultyProgression.
            if (gemCapForRound > 0)
            {
                ApplyDailyDifficultyCurve();
            }

            // Clean up any inactive obstacles
            for (int i = activeObstacles.Count - 1; i >= 0; i--)
            {
                if (!activeObstacles[i].activeInHierarchy)
                {
                    activeObstacles.RemoveAt(i);
                }
            }

            // Advance the cycle counter and use the deterministic cadence to
            // decide gem vs. pickup. Counter is 1-indexed: cycles 1..warmup
            // are gems, then every (warmup + N*every) cycle is a power-up.
            // E.g. warmup=10, every=10 → pickups on drops 11, 21, 31, ...
            //
            // Internal test mode short-circuits the cadence and forces every
            // cycle to be a pickup. We also skip obstacle spawning and the
            // daily-mode gem cap below so the developer can sit and watch
            // power-ups roll in indefinitely.
            dropCounter++;
            bool wantPickup = powerUpOnlyTestMode || ShouldDropPickupOnCycle(dropCounter);

            // Pickup spawn can still fail (e.g. activePickups already at cap),
            // in which case fall through to a normal gem so the cycle isn't
            // empty — the next scheduled pickup attempt is unaffected.
            bool spawnedPickup = wantPickup && TrySpawnPowerUp();

            if (spawnedPickup)
            {
                // Pickup-only cycle: no gem, no count toward the daily cap, no
                // obstacle. The placement phase still runs so the catcher spins
                // and the player can reposition before the pickup arrives.
                placementForPickupOnly = true;
            }
            else if (powerUpOnlyTestMode)
            {
                // Test mode never falls through to a regular-gem spawn — if
                // TrySpawnPowerUp aborted (pool exhausted, max-active hit),
                // we just idle this cycle and try again next time. Keeps the
                // visual signal clean: while test mode is on, the player is
                // ONLY ever seeing power-ups.
                placementForPickupOnly = true;
            }
            else
            {
                placementForPickupOnly = false;

                SpawnGem();
                gemsSpawnedThisRound++;

                // Maybe spawn a new obstacle
                if (RngFloat() < obstacleSpawnChance && activeObstacles.Count < maxObstacles)
                {
                    SpawnObstacle();
                }

                GemSpawned?.Invoke();
            }

            // Set the next spawn time
            nextSpawnTime = Time.time + currentSpawnInterval;

            // Start the placement phase
            isPlacementPhase = true;
            placementTimer = placementDuration;
            blinkTimer = 0f;

            PlacementPhaseStarted?.Invoke(placementTimer);
        }

        // Update the placement timer and gem blinking
        if (isPlacementPhase)
        {
            UpdatePlacementBlink();
        }
    }

    private void UpdatePlacementBlink()
    {
        if (!isPlacementPhase) return;

        placementTimer -= Time.deltaTime;

        PlacementTimerUpdated?.Invoke(placementTimer);

        // Blink the gem: starts slow, gets faster as timer runs down
        if (currentActiveGem != null && currentActiveGem.activeInHierarchy)
        {
            float progress = 1f - Mathf.Clamp01(placementTimer / placementDuration);
            // Blink interval: 0.4s at start → 0.05s at end (lerp with ease-in)
            float blinkInterval = Mathf.Lerp(0.4f, 0.05f, progress * progress);

            blinkTimer += Time.deltaTime;
            if (blinkTimer >= blinkInterval)
            {
                blinkTimer = 0f;
                ToggleGemRenderers(currentActiveGem);
            }
        }

        if (placementTimer <= 0)
        {
            EndPlacementPhase(applySpeedup: true);
        }
    }

    private void EndPlacementPhase(bool applySpeedup)
    {
        if (!isPlacementPhase) return;

        isPlacementPhase = false;
        placementForPickupOnly = false;

        // Ensure gem is fully visible (not mid-blink)
        if (currentActiveGem != null && currentActiveGem.activeInHierarchy)
        {
            SetGemRenderersVisible(currentActiveGem, true);
        }

        if (applySpeedup && currentActiveGem != null && currentActiveGem.activeInHierarchy)
        {
            FallingObject fallingObj = currentActiveGem.GetComponent<FallingObject>();
            if (fallingObj != null)
            {
                fallingObj.UpdateFallSpeed(currentFallSpeed);
            }
        }

        PlacementPhaseEnded?.Invoke();
    }

    private static void ToggleGemRenderers(GameObject gem)
    {
        Renderer[] renderers = gem.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].enabled = !renderers[i].enabled;
    }

    private static void SetGemRenderersVisible(GameObject gem, bool visible)
    {
        Renderer[] renderers = gem.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].enabled = visible;
    }

    void SpawnGem()
    {
        // MasterGem spawn check: on Deep Space, once score reaches threshold,
        // spawn the MasterGem instead of a normal gem. One shot only.
        if (!masterGemSpawned && masterGemInstance != null
            && GemCatcher.Score >= MasterGemScoreThreshold)
        {
            masterGemSpawned = true;
            SpawnMasterGem();
            return;
        }

        // Roll the variant before selecting the pooled gem so the same
        // deterministic RNG stream drives variant selection in daily mode.
        SpecialGemType variant = RollSpecialType();

        // Prefab selection: any inactive pooled instance is fair game,
        // including HeartGem. In daily mode we still route the prefab choice
        // through the seeded RNG (one Next() advance per spawn) so every
        // player sees the same gem sequence on the same date.
        //
        // Golden gems MUST always use the HeartGem prefab (heart shape with
        // gold tint). Power-ups that ride on specific prefabs are handled
        // separately in TrySpawnPowerUp.
        GameObject obj;
        if (variant == SpecialGemType.Golden)
        {
            // Golden always uses HeartGem mesh.
            obj = GetInactivePooledObjectByPrefabName(objectPool, "HeartGem");
            if (obj == null) obj = GetRandomPooledObject(objectPool);
            // Advance the RNG so daily-mode sequences stay deterministic.
            if (gemCapForRound > 0 && objectPrefabs != null && objectPrefabs.Length > 0)
                rng.Next(0, objectPrefabs.Length);
        }
        else if (gemCapForRound > 0 && objectPrefabs != null && objectPrefabs.Length > 0)
        {
            int prefabIdx = rng.Next(0, objectPrefabs.Length);
            string targetName = objectPrefabs[prefabIdx] != null ? objectPrefabs[prefabIdx].name : null;
            obj = GetInactivePooledObjectByPrefabName(objectPool, targetName);
            // Pool exhausted for the rolled prefab: fall back to any inactive
            // instance so the spawn cycle isn't dropped on the floor.
            if (obj == null) obj = GetRandomPooledObject(objectPool);
        }
        else
        {
            obj = GetRandomPooledObject(objectPool);
        }

        if (obj != null)
        {
            // Spawn at the top of the safe play area (below any phone notch / camera lens)
            // and within the safe horizontal bounds. We clamp spawnXRange so a too-large
            // value in the Inspector doesn't push gems behind a notch on narrow phones.
            float maxX = Mathf.Max(0.1f, Mathf.Min(spawnXRange, ScreenPadding.WorldRight - 0.3f));
            float minX = Mathf.Min(-0.1f, Mathf.Max(-spawnXRange, ScreenPadding.WorldLeft + 0.3f));
            float randomX = RngRange(minX, maxX);
            obj.transform.position = new Vector3(randomX, ScreenPadding.WorldTop, 0f);

            // Update the falling object component - ALWAYS set to slow speed initially
            FallingObject fallingObj = obj.GetComponent<FallingObject>();
            if (fallingObj != null)
            {
                // Apply the current gem-scale factor BEFORE ResetObject so the
                // bounds cached inside InitializeComponents reflect the actual
                // visual size — otherwise wall-bounce boundaries would be
                // computed against the prefab size and the gem would bounce
                // visibly inside the wall when shrunk.
                fallingObj.ApplyScaleFactor(GetCurrentGemScaleFactor());

                // Reset the object to ensure it starts fresh
                fallingObj.ResetObject();

                // Start with a slower fall speed during placement phase
                fallingObj.fallSpeed = placementPhaseFallSpeed;

                // Set higher probability for diagonal movement
                float horizontalBias = RngRange(-0.8f, 0.8f); // Bias towards non-zero values
                fallingObj.horizontalSpeed = Mathf.Sign(horizontalBias) * RngRange(0.5f, 1.0f);

                // Make sure the object initializes with the slow speed
                fallingObj.InitializeMovement(placementPhaseFallSpeed);

                // Apply the variant we rolled at the top of SpawnGem (Normal /
                // Golden / Bomb). Done after movement setup so the
                // variant doesn't change physics behavior — just visuals and
                // catch-time scoring rules.
                fallingObj.ApplySpecialType(variant);
            }

            obj.SetActive(true);
            currentActiveGem = obj;

            // Lightning bolt effect at spawn point
            LightningSpawnEffect.Strike(obj.transform.position);
        }
    }

    /// <summary>
    /// Spawns the MasterGem. Called once when the player reaches the score
    /// threshold on Deep Space. Uses the same placement-phase flow as normal gems.
    /// </summary>
    void SpawnMasterGem()
    {
        GameObject obj = masterGemInstance;
        if (obj == null) return;

        float maxX = Mathf.Max(0.1f, Mathf.Min(spawnXRange, ScreenPadding.WorldRight - 0.3f));
        float minX = Mathf.Min(-0.1f, Mathf.Max(-spawnXRange, ScreenPadding.WorldLeft + 0.3f));
        float randomX = RngRange(minX, maxX);
        obj.transform.position = new Vector3(randomX, ScreenPadding.WorldTop, 0f);

        FallingObject fallingObj = obj.GetComponent<FallingObject>();
        if (fallingObj != null)
        {
            fallingObj.ApplyScaleFactor(1f);
            fallingObj.ResetObject();
            fallingObj.fallSpeed = placementPhaseFallSpeed;
            fallingObj.horizontalSpeed = 0f;
            fallingObj.InitializeMovement(placementPhaseFallSpeed);
            fallingObj.ApplySpecialType(SpecialGemType.MasterGem);
        }

        obj.SetActive(true);
        currentActiveGem = obj;

        LightningSpawnEffect.Strike(obj.transform.position);
        // Placement phase and GemSpawned are handled by the caller (Update loop).
    }

    // ---- Rush Mode helpers ------------------------------------------------

    /// <summary>
    /// Public entry point for SpawnDirector to spawn a gem at a specific
    /// position with a specific speed. Uses the gem pool.
    /// </summary>
    // Heart gem timer: spawns one guaranteed every 30 seconds.
    private float rushHeartGemTimer = 0f;
    private const float RushHeartGemInterval = 30f;
    private bool rushHeartGemReady = false;

    public void SpawnRushGemAt(float x, float y, float speed, float redGemChance = 0f)
    {
        // Heart gem spawns on a fixed 30-second timer.
        bool isHeart = rushHeartGemReady;
        if (isHeart) rushHeartGemReady = false;

        // Determine base/upgrade gem names and points based on level.
        string baseGem, upgradeGem;
        bool isDiamond = false;
        bool isRed = false;
        bool useUpgrade = false;

        var level = LevelManager.SelectedLevel;
        if (level == LevelManager.LevelId.Jungle)
        {
            // Level 2: RedDiamond (40pts) → DiamondGem (80pts)
            baseGem = "RedDiamond";
            upgradeGem = "DiamondGem";
            useUpgrade = !isHeart && redGemChance > 0f && UnityEngine.Random.value < redGemChance;
            isDiamond = useUpgrade;
            isRed = !useUpgrade; // base gem in level 2 is always red
        }
        else
        {
            // Level 1 (and default): GreenVolcom (20pts) → RedDiamond (40pts)
            baseGem = "GreenVolcom";
            upgradeGem = "RedDiamond";
            useUpgrade = !isHeart && redGemChance > 0f && UnityEngine.Random.value < redGemChance;
            isRed = useUpgrade;
        }

        string prefabName = isHeart ? "HeartGem" : (useUpgrade ? upgradeGem : baseGem);

        GameObject obj = GetInactivePooledObjectByPrefabName(objectPool, prefabName);
        if (obj == null) return;

        obj.transform.position = new Vector3(x, y, 0f);
        obj.transform.rotation = Quaternion.identity;
        FallingObject fo = obj.GetComponent<FallingObject>();
        if (fo != null)
        {
            fo.ApplyScaleFactor(GetCurrentGemScaleFactor());
            fo.ResetObject();
            fo.verticalOnly = true;
            fo.horizontalSpeed = 0f;
            fo.fallSpeed = speed;
            fo.InitializeMovement(speed);
            fo.isRushHeart = isHeart;
            fo.isRushRedGem = isRed;
            fo.isRushDiamondGem = isDiamond;
            fo.ApplySpecialType(SpecialGemType.Normal);

            // Tint heart gems red so they stand out.
            if (isHeart)
            {
                Renderer r = obj.GetComponent<Renderer>();
                if (r == null) r = obj.GetComponentInChildren<Renderer>();
                if (r != null)
                {
                    MaterialPropertyBlock mpb = new MaterialPropertyBlock();
                    r.GetPropertyBlock(mpb);
                    mpb.SetColor("_Color", new Color(1f, 0.15f, 0.15f, 1f));
                    mpb.SetColor("_BaseColor", new Color(1f, 0.15f, 0.15f, 1f));
                    mpb.SetColor("_EmissionColor", new Color(1f, 0.2f, 0.2f, 1f));
                    r.SetPropertyBlock(mpb);
                }
            }

            // Add white glow to diamond gems if not already present.
            if (isDiamond)
            {
                GemGlowVolume glow = obj.GetComponent<GemGlowVolume>();
                if (glow == null) glow = obj.AddComponent<GemGlowVolume>();
                glow.glowColor = new Color(1f, 1f, 1f, 1f);
                glow.glowAlpha = 0.85f;
                glow.glowRadius = 0.9f;
            }
        }
        obj.SetActive(true);
        currentActiveGem = obj;
    }

    /// <summary>
    /// Spawn a poison gem — looks like a gem but tinted purple. Costs a life if caught.
    /// </summary>
    public void SpawnRushPoisonGemAt(float x, float y, float speed)
    {
        GameObject obj = GetInactivePooledObjectByPrefabName(objectPool, "GreenVolcom");
        if (obj == null) return;

        obj.transform.position = new Vector3(x, y, 0f);
        obj.transform.rotation = Quaternion.identity;
        FallingObject fo = obj.GetComponent<FallingObject>();
        if (fo != null)
        {
            fo.ApplyScaleFactor(GetCurrentGemScaleFactor());
            fo.ResetObject();
            fo.verticalOnly = true;
            fo.horizontalSpeed = 0f;
            fo.fallSpeed = speed;
            fo.InitializeMovement(speed);
            fo.ApplySpecialType(SpecialGemType.Normal);
            fo.isPoisonGem = true;

            // Tint purple so observant players can distinguish.
            Renderer r = obj.GetComponent<Renderer>();
            if (r != null)
            {
                MaterialPropertyBlock mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", new Color(0.6f, 0.1f, 0.8f, 1f));
                mpb.SetColor("_EmissionColor", new Color(0.4f, 0f, 0.5f, 1f));
                r.SetPropertyBlock(mpb);
            }
        }
        obj.SetActive(true);
    }

    void SpawnObstacle()
    {
        // Get a random inactive obstacle from the pool
        GameObject obj = GetRandomPooledObject(obstaclePool);
        if (obj != null)
        {
            // Stay inside the safe play area; obstacles biased to the upper half so they
            // don't crowd the catcher band.
            float maxX = Mathf.Max(0.1f, Mathf.Min(spawnXRange, ScreenPadding.WorldRight - 0.3f));
            float minX = Mathf.Min(-0.1f, Mathf.Max(-spawnXRange, ScreenPadding.WorldLeft + 0.3f));
            float randomX = RngRange(minX, maxX);
            float playHeight = ScreenPadding.WorldTop - ScreenPadding.WorldBottom;
            float yMin = ScreenPadding.WorldBottom + playHeight * 0.25f;
            float yMax = ScreenPadding.WorldBottom + playHeight * 0.85f;
            float randomY = RngRange(yMin, yMax);
            obj.transform.position = new Vector3(randomX, randomY, 0f);

            // Random rotation
            obj.transform.rotation = Quaternion.Euler(0, 0, RngRange(0f, 360f));

            obj.SetActive(true);
            activeObstacles.Add(obj);
            LightningSpawnEffect.Strike(obj.transform.position);
        }
    }

    // Returns true if cycle index `cycle` (1-indexed) should drop a pickup
    // under the deterministic cadence. With defaults (warmup=10, every=10):
    //   1..10  → gems
    //   11     → pickup    (first power-up after the 10-drop warmup)
    //   12..20 → gems
    //   21     → pickup
    //   ...
    // Guarded against pathological tuning values (cadence < 1 disables pickups).
    bool ShouldDropPickupOnCycle(int cycle)
    {
        if (powerUpEveryNDrops < 1) return false;
        if (cycle <= powerUpWarmupDrops) return false;
        // Cycle relative to the first valid pickup slot. Pickup happens when
        // (cycle - (warmup + 1)) is a multiple of the cadence — i.e. at warmup+1,
        // warmup+1+every, warmup+1+2*every, ...
        return (cycle - (powerUpWarmupDrops + 1)) % powerUpEveryNDrops == 0;
    }

    // Spawn a power-up pickup at the top of the play area, replacing the gem
    // that would have spawned this cycle. Returns true on success so the
    // caller can skip its gem-spawn block. Caps simultaneous pickups as a
    // safety net (the deterministic schedule already prevents back-to-back
    // pickups under normal play). Uses the seeded RNG to choose pickup TYPE
    // so daily-mode pickup variety is identical for every player on a given
    // UTC day.
    //
    // Pickups now ride on the existing gem prefabs: each PowerUpType has a
    // designated mesh (Star = Wide, Topaz = Shield, Volcom = 2x, Heart =
    // ExtraLife) and the spawner pulls an inactive instance of that prefab
    // out of the regular gem pool. The standard FallingObject + GemCatcher
    // scripts handle motion + catch detection — we just paint the gem in the
    // power-up's theme color and attach a tinted fiery aura via
    // FallingObject.ApplyPowerUpType. This means power-ups now drift, bounce
    // off walls, and require the same trajectory prediction as a normal gem
    // catch (visual contract: "if it falls like a gem, you have to catch it
    // like a gem"). The off-screen miss is still silent — see
    // FallingObject.Update's isPowerUp branch.
    bool TrySpawnPowerUp()
    {
        // No power-ups in Rush Mode.
        if (GameState.Mode == GameState.GameMode.Rush) return false;

        // Drop dead references first.
        for (int i = activePickups.Count - 1; i >= 0; i--)
        {
            if (activePickups[i] == null || !activePickups[i].activeInHierarchy)
            {
                activePickups.RemoveAt(i);
            }
        }

        if (activePickups.Count >= maxActivePickups) return false;

        int typeCount = System.Enum.GetValues(typeof(PowerUpType)).Length;
        PowerUpType type;
        if (powerUpOnlyTestMode)
        {
            // Walk the enum in declaration order so a session in test mode
            // shows every variant before repeating. Wrap with modulo on
            // typeCount so adding a new PowerUpType in the future picks up
            // automatically without touching this branch.
            type = (PowerUpType)(testModePowerUpIndex % typeCount);
            testModePowerUpIndex++;
        }
        else
        {
            type = (PowerUpType)rng.Next(0, typeCount);
        }

        // Pull the gem instance whose prefab name matches this power-up's
        // designated mesh. If the pool has no free instance (every gem of
        // that mesh is currently in flight as a regular gem), we abort the
        // pickup spawn — the caller falls through to a regular gem this
        // cycle and the next scheduled pickup attempt covers the gap.
        string prefabName = PowerUpPickup.GemPrefabNameForType(type);
        if (string.IsNullOrEmpty(prefabName)) return false;

        GameObject pickup = GetInactivePooledObjectByPrefabName(objectPool, prefabName);
        if (pickup == null) return false;

        // Position + motion setup mirrors SpawnGem's so a power-up is visually
        // indistinguishable from a normal gem at spawn time apart from the
        // theme-colored body and the fiery aura. Same X range, same drift, same
        // placement-phase slow-fall.
        float maxX = Mathf.Max(0.1f, Mathf.Min(spawnXRange, ScreenPadding.WorldRight - 0.3f));
        float minX = Mathf.Min(-0.1f, Mathf.Max(-spawnXRange, ScreenPadding.WorldLeft + 0.3f));
        float x = RngRange(minX, maxX);
        pickup.transform.position = new Vector3(x, ScreenPadding.WorldTop, 0f);

        FallingObject fallingObj = pickup.GetComponent<FallingObject>();
        if (fallingObj != null)
        {
            // Reuse the score-driven gem-shrink so a power-up at 2000+
            // points is small like its sibling gems.
            fallingObj.ApplyScaleFactor(GetCurrentGemScaleFactor());
            fallingObj.ResetObject();
            fallingObj.fallSpeed = placementPhaseFallSpeed;

            float horizontalBias = RngRange(-0.8f, 0.8f);
            fallingObj.horizontalSpeed = Mathf.Sign(horizontalBias) * RngRange(0.5f, 1.0f);
            fallingObj.InitializeMovement(placementPhaseFallSpeed);

            // Apply the power-up paint AFTER motion init so ApplyPowerUpType's
            // call to ApplySpecialType(Normal) doesn't fight the motion setup
            // — Normal is the default state, so the call is essentially a
            // visual reset before we paint the power-up tint on top.
            fallingObj.ApplyPowerUpType(type, PowerUpPickup.ColorForType(type));
        }

        pickup.SetActive(true);
        activePickups.Add(pickup);
        LightningSpawnEffect.Strike(pickup.transform.position);
        // Track the active "gem" pointer so the cycle's catcher-placement /
        // early-termination logic still has something to watch — without
        // this, the placement phase can't tell when the power-up has been
        // caught vs. is still in flight.
        currentActiveGem = pickup;
        return true;
    }

    GameObject GetRandomPooledObject(List<GameObject> pool)
    {
        List<GameObject> inactiveObjects = new List<GameObject>();

        // Collect all inactive objects in the pool
        foreach (GameObject obj in pool)
        {
            if (!obj.activeInHierarchy)
            {
                inactiveObjects.Add(obj);
            }
        }

        // Return a random inactive object, if any exist
        if (inactiveObjects.Count > 0)
        {
            int randomIndex = rng.Next(0, inactiveObjects.Count);
            return inactiveObjects[randomIndex];
        }

        return null; // If all objects are active, return null
    }

    // Daily-mode helper: find an inactive instance whose prefab name matches.
    // Used so the *type* of gem is deterministic on a given day rather than
    // an artifact of which pooled instance happens to be free.
    private GameObject GetInactivePooledObjectByPrefabName(List<GameObject> pool, string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName)) return null;
        foreach (GameObject obj in pool)
        {
            if (obj == null || obj.activeInHierarchy) continue;
            // Pooled clones are named "<Prefab>(Clone)"; match by prefix.
            if (obj.name.StartsWith(prefabName, StringComparison.Ordinal))
            {
                return obj;
            }
        }
        return null;
    }

    // ---- RNG helpers ---------------------------------------------------------
    // Uniform float in [0, 1). Mirrors UnityEngine.Random.value.
    private float RngFloat() => (float)rng.NextDouble();

    // Uniform float in [min, max]. Mirrors UnityEngine.Random.Range(float, float).
    private float RngRange(float min, float max)
        => min + (max - min) * (float)rng.NextDouble();

    // Apply the linear daily-mode difficulty ramp based on how many gems have
    // been spawned so far. Called on every spawn.
    private void ApplyDailyDifficultyCurve()
    {
        if (gemCapForRound <= 0) return;
        // Progress in [0, 1] across the round. Use (gemsSpawned / max(cap-1, 1))
        // so the very last gem hits the peak speed.
        float t = gemCapForRound > 1
            ? (float)gemsSpawnedThisRound / (gemCapForRound - 1)
            : 1f;
        t = Mathf.Clamp01(t);
        currentFallSpeed = Mathf.Lerp(initialFallSpeed, dailyMaxFallSpeed, t);
        currentSpawnInterval = Mathf.Lerp(initialSpawnInterval, dailyMinSpawnInterval, t);
    }

    // Returns the localScale multiplier gems should spawn / be rescaled at
    // based on the current score. Above smallGemScoreThreshold gems shrink to
    // smallGemScaleFactor of their prefab size.
    private float GetCurrentGemScaleFactor()
    {
        if (GameState.Mode == GameState.GameMode.Rush) return 1f;
        return GemCatcher.Score >= smallGemScoreThreshold ? smallGemScaleFactor : 1f;
    }

    // Picks a special-gem variant (or Normal) for the next spawn. Uses the
    // seeded RNG so daily mode produces the same special-gem sequence for
    // every player. Order: Golden → Bomb → Normal, so each chance value reads
    // "chance OUT OF the remaining mass after earlier rolls". The total of
    // all chances should stay below 1 — if they sum past
    // 1, Normal simply never spawns.
    //
    // Note: random-roll Heart variant gems were removed — the only ways to
    // gain a life are now (1) catching the ExtraLife power-up and (2) the
    // every-third-catch combo award handled in GemCatcher.HandleVariantCatch.
    private SpecialGemType RollSpecialType()
    {
        float r = RngFloat();
        if (r < goldenGemChance) return SpecialGemType.Golden;
        r -= goldenGemChance;
        if (r < bombGemChance) return SpecialGemType.Bomb;
        return SpecialGemType.Normal;
    }

    void CheckDifficultyProgression(int newScore)
    {
        // Apply the small-gem rescale to the in-flight gem the moment the
        // score crosses the threshold so the player sees the size change
        // immediately rather than waiting for the next spawn cycle. Runs in
        // both modes — daily and normal — because it's a visual rule keyed
        // off score, not a difficulty-tier decision.
        if (currentActiveGem != null && currentActiveGem.activeInHierarchy)
        {
            FallingObject inFlight = currentActiveGem.GetComponent<FallingObject>();
            if (inFlight != null && GameState.Mode != GameState.GameMode.Rush)
            {
                float factor = newScore >= smallGemScoreThreshold ? smallGemScaleFactor : 1f;
                inFlight.ApplyScaleFactor(factor);
            }
        }

        // Daily mode uses a deterministic gem-index ramp instead of the
        // score-threshold tiers, so skip the tier check entirely.
        if (GameState.Mode == GameState.GameMode.Daily) return;

        // Check if we should advance to the next difficulty level
        for (int i = difficultyLevels.Length - 1; i >= 0; i--)
        {
            if (newScore >= difficultyLevels[i].scoreThreshold && i > currentDifficultyLevel)
            {
                currentDifficultyLevel = i;
                UpdateDifficultySettings();
#if UNITY_EDITOR
                Debug.Log($"Difficulty increased to level {currentDifficultyLevel + 1}! Speed: {currentFallSpeed}, Interval: {currentSpawnInterval}");
#endif
                break;
            }
        }
    }

    void UpdateDifficultySettings()
    {
        if (currentDifficultyLevel < difficultyLevels.Length)
        {
            DifficultyLevel level = difficultyLevels[currentDifficultyLevel];
            currentFallSpeed = level.fallSpeed;
            currentSpawnInterval = level.spawnInterval;
        }
    }

    /// <summary>
    /// Applies the selected level's difficulty parameters. Called once at the
    /// start of each round. In Daily mode the base speeds are still overridden
    /// by the daily ramp, but bomb/golden chances apply.
    /// </summary>
    void ApplyLevelDifficulty()
    {
        var cfg = LevelManager.CurrentConfig;
        initialFallSpeed = cfg.initialFallSpeed;
        initialSpawnInterval = cfg.initialSpawnInterval;
        bombGemChance = cfg.bombChance;
        goldenGemChance = cfg.goldenChance;
        dailyMaxFallSpeed = cfg.dailyMaxFallSpeed;
        dailyMinSpawnInterval = cfg.dailyMinSpawnInterval;
        currentFallSpeed = initialFallSpeed;
        currentSpawnInterval = initialSpawnInterval;
    }

    public float GetPlacementTimeRemaining()
    {
        return isPlacementPhase ? placementTimer : 0f;
    }

    public bool IsInPlacementPhase()
    {
        return isPlacementPhase;
    }

    void OnDestroy()
    {
        // Unsubscribe from events when this object is destroyed
        GemCatcher.OnScoreChanged -= CheckDifficultyProgression;
        GemCatcher.OnGameOver -= HandleGameOver;
    }

    // ---- Tutorial API -------------------------------------------------------

    /// <summary>
    /// Force-spawn a gem of the given type for the tutorial. Bypasses normal
    /// spawn cadence and difficulty — just drops a single gem for practice.
    /// </summary>
    public void TutorialSpawnGem(SpecialGemType type)
    {
        GameObject obj;
        // Golden gems always use the HeartGem prefab (same as normal spawning)
        if (type == SpecialGemType.Golden)
        {
            obj = GetInactivePooledObjectByPrefabName(objectPool, "HeartGem");
            if (obj == null) obj = GetRandomPooledObject(objectPool);
        }
        else
        {
            obj = GetRandomPooledObject(objectPool);
        }
        if (obj == null) return;

        float maxX = Mathf.Max(0.1f, Mathf.Min(spawnXRange, ScreenPadding.WorldRight - 0.3f));
        float minX = Mathf.Min(-0.1f, Mathf.Max(-spawnXRange, ScreenPadding.WorldLeft + 0.3f));
        float randomX = UnityEngine.Random.Range(minX, maxX);
        obj.transform.position = new Vector3(randomX, ScreenPadding.WorldTop, 0f);

        FallingObject fallingObj = obj.GetComponent<FallingObject>();
        if (fallingObj != null)
        {
            fallingObj.ApplyScaleFactor(1f);
            fallingObj.ResetObject();
            fallingObj.fallSpeed = placementPhaseFallSpeed;
            fallingObj.horizontalSpeed = UnityEngine.Random.Range(-0.5f, 0.5f);
            fallingObj.InitializeMovement(placementPhaseFallSpeed);
            fallingObj.ApplySpecialType(type);
        }

        obj.SetActive(true);
        currentActiveGem = obj;

        LightningSpawnEffect.Strike(obj.transform.position);

        // Start placement phase
        isPlacementPhase = true;
        placementTimer = placementDuration;
        blinkTimer = 0f;
        PlacementPhaseStarted?.Invoke(placementTimer);
    }

    /// <summary>
    /// Force-spawn a power-up of the given type for the tutorial.
    /// </summary>
    public void TutorialSpawnPowerUp(PowerUpType type)
    {
        string prefabName = PowerUpPickup.GemPrefabNameForType(type);
        GameObject pickup = GetInactivePooledObjectByPrefabName(objectPool, prefabName);
        if (pickup == null) pickup = GetRandomPooledObject(objectPool);
        if (pickup == null) return;

        float maxX = Mathf.Max(0.1f, Mathf.Min(spawnXRange, ScreenPadding.WorldRight - 0.3f));
        float minX = Mathf.Min(-0.1f, Mathf.Max(-spawnXRange, ScreenPadding.WorldLeft + 0.3f));
        float randomX = UnityEngine.Random.Range(minX, maxX);
        pickup.transform.position = new Vector3(randomX, ScreenPadding.WorldTop, 0f);

        FallingObject fallingObj = pickup.GetComponent<FallingObject>();
        if (fallingObj != null)
        {
            fallingObj.ApplyScaleFactor(1f);
            fallingObj.ResetObject();
            fallingObj.fallSpeed = placementPhaseFallSpeed;
            fallingObj.horizontalSpeed = UnityEngine.Random.Range(-0.3f, 0.3f);
            fallingObj.InitializeMovement(placementPhaseFallSpeed);
            fallingObj.ApplyPowerUpType(type, PowerUpPickup.ColorForType(type));
        }

        pickup.SetActive(true);
        currentActiveGem = pickup;

        LightningSpawnEffect.Strike(pickup.transform.position);

        // Start placement phase
        isPlacementPhase = true;
        placementTimer = placementDuration;
        blinkTimer = 0f;
        PlacementPhaseStarted?.Invoke(placementTimer);
    }
}
