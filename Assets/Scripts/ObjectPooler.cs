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
    [Range(0f, 1f)]
    [Tooltip("Chance per gem spawn that the gem is a Heart gem (+1 life on catch). Skipped if the player is already at full lives.")]
    public float heartGemChance = 0.03f;
    [Tooltip("Don't spawn Heart gems while lives are at or above this cap (prevents wasted hearts). Should match GemCatcher.MAX_LIVES so hearts spawn anywhere below the ceiling.")]
    public int heartLivesCap = 10;

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
    private float nextSpawnTime;
    private float currentFallSpeed;
    private float currentSpawnInterval;

    // Timer for catcher placement
    private float placementTimer = 3.0f;
    private bool isPlacementPhase = false;

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

        // Initialize the pools
        objectPool = new List<GameObject>();
        obstaclePool = new List<GameObject>();
        activeObstacles = new List<GameObject>();

        // Set initial difficulty values
        currentFallSpeed = initialFallSpeed;
        currentSpawnInterval = initialSpawnInterval;

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

        // Initialize gem pool
        foreach (GameObject prefab in objectPrefabs)
        {
            for (int i = 0; i < poolSizePerPrefab; i++)
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
        // game-over screen / next round.
        for (int i = activePickups.Count - 1; i >= 0; i--)
        {
            if (activePickups[i] != null) Destroy(activePickups[i]);
        }
        activePickups.Clear();
    }

    void Update()
    {
        // Wait on the main menu — no spawns, no countdowns, no difficulty progression
        // until the player presses Play.
        if (!GameState.IsPlaying) return;

        // Freeze gem/obstacle spawning once the player runs out of lives.
        if (GemCatcher.IsGameOver) return;

        bool gemInactive = currentActiveGem == null || !currentActiveGem.activeInHierarchy;

        // If the gem became inactive (caught or fell off-screen) mid-placement, end the phase now
        // rather than letting it tick down on a non-existent gem. Skip this for
        // pickup-only cycles — those have no gem in flight by design and should
        // run their full 3-second placement so the player can reposition.
        if (gemInactive && isPlacementPhase && !placementForPickupOnly)
        {
            EndPlacementPhase(applySpeedup: false);
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
            dropCounter++;
            bool wantPickup = ShouldDropPickupOnCycle(dropCounter);

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
            placementTimer = 3.0f;

            PlacementPhaseStarted?.Invoke(placementTimer);
        }

        // Update the placement timer
        if (isPlacementPhase)
        {
            placementTimer -= Time.deltaTime;

            PlacementTimerUpdated?.Invoke(placementTimer);

            if (placementTimer <= 0)
            {
                EndPlacementPhase(applySpeedup: true);
            }
        }
    }

    private void EndPlacementPhase(bool applySpeedup)
    {
        if (!isPlacementPhase) return;

        isPlacementPhase = false;
        // Reset the pickup-only flag so the next cycle starts with a clean
        // assumption (gem cycle by default).
        placementForPickupOnly = false;

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

    void SpawnGem()
    {
        // Daily mode: pick the prefab type via the seeded RNG so the gem
        // sequence is identical for every player. Normal mode just grabs any
        // inactive instance from the shared pool, same as before.
        GameObject obj = null;
        if (gemCapForRound > 0 && objectPrefabs != null && objectPrefabs.Length > 0)
        {
            int prefabIdx = rng.Next(0, objectPrefabs.Length);
            string targetName = objectPrefabs[prefabIdx] != null ? objectPrefabs[prefabIdx].name : null;
            obj = GetInactivePooledObjectByPrefabName(objectPool, targetName);
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

                // Roll for a special-gem variant (Golden / Bomb / Heart) and
                // apply the visual tint + override flag. Done after movement
                // setup so the variant doesn't change physics behavior — just
                // visuals and catch-time scoring rules.
                fallingObj.ApplySpecialType(RollSpecialType());
            }

            obj.SetActive(true);
            currentActiveGem = obj;
        }
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
    bool TrySpawnPowerUp()
    {
        // Drop dead references first.
        for (int i = activePickups.Count - 1; i >= 0; i--)
        {
            if (activePickups[i] == null) activePickups.RemoveAt(i);
        }

        if (activePickups.Count >= maxActivePickups) return false;

        int typeCount = System.Enum.GetValues(typeof(PowerUpType)).Length;
        PowerUpType type = (PowerUpType)rng.Next(0, typeCount);

        float maxX = Mathf.Max(0.1f, Mathf.Min(spawnXRange, ScreenPadding.WorldRight - 0.3f));
        float minX = Mathf.Min(-0.1f, Mathf.Max(-spawnXRange, ScreenPadding.WorldLeft + 0.3f));
        float x = RngRange(minX, maxX);

        GameObject pickup = PowerUpPickup.Create(type, new Vector3(x, ScreenPadding.WorldTop, 0f));
        if (pickup == null) return false;

        activePickups.Add(pickup);
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
        return GemCatcher.Score >= smallGemScoreThreshold ? smallGemScaleFactor : 1f;
    }

    // Picks a special-gem variant (or Normal) for the next spawn. Uses the
    // seeded RNG so daily mode produces the same special-gem sequence for
    // every player. Order: Heart → Golden → Bomb → Normal, so each chance
    // value reads "chance OUT OF the remaining mass after earlier rolls". The
    // total of all three should stay below 1 — if they sum past 1, Normal
    // simply never spawns.
    private SpecialGemType RollSpecialType()
    {
        // Suppress hearts when the player is already loaded up so we never
        // waste a slot on an unneeded life. The slot reverts to a normal gem
        // for that spawn cycle (no re-roll — keeps daily-mode RNG stream
        // deterministic with the same number of advances).
        bool heartAllowed = GemCatcher.Lives < heartLivesCap;

        float r = RngFloat();
        if (heartAllowed && r < heartGemChance) return SpecialGemType.Heart;
        r -= heartGemChance;
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
            if (inFlight != null)
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
}
