using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Orchestrates Rush Mode spawning using the decision-first
/// <see cref="WaveGeneratorV2"/> pipeline. Manages wave memory,
/// seeded generation, and debug visualization.
/// </summary>
public class SpawnDirector : MonoBehaviour
{
    [Tooltip("Drag the RushConfig ScriptableObject here.")]
    public RushConfig config;

    // ---- runtime state --------------------------------------------------
    private float roundStartTime;
    private float nextWaveSpawnY;        // world-Y where the next wave starts
    private WaveDefinition activeWave;   // wave currently being materialized
    private DecisionPlan activePlan;     // plan for current wave
    private int activeRowIndex;          // next row to spawn in activeWave
    private float lastRowSpawnTime;

    // Wave generation state
    private WaveMemory waveMemory;
    private int runSeed;
    private int waveIndex;

    // Debug stats
    private int totalWavesGenerated;
    private int totalRejectedAttempts;
    private WaveDefinition lastSpawnedWave; // for Gizmo drawing
    private DecisionPlan lastSpawnedPlan;   // for Gizmo overlay
    private WaveGeneratorV2.GenerateResult lastResult; // for debug details
    private WaveDefinition.Row lastWaveFinalRow; // for cross-wave reachability
    private float activeTierPause; // current tier's wave pause

    // Magnet power-up drop
    private float nextMagnetDropTime;
    private const float MagnetDropInterval = 45f;
    private GameObject magnetPrefab;
    private bool pendingMagnet;

    // Shield power-up drop
    private float nextShieldDropTime;
    private const float ShieldDropInterval = 45f;
    private GameObject shieldPrefab;
    private bool pendingShield;

    // Dice (swap) power-up drop
    private float nextDiceDropTime;
    private const float DiceDropInterval = 90f;
    private GameObject dicePrefab;
    private bool pendingDice;
    private bool diceEnabled; // only levels 2+

    // MasterGem (invincibility) power-up drop
    private float nextMasterGemDropTime;
    private const float MasterGemDropInterval = 180f; // every 3 minutes
    private bool pendingMasterGem;

    // Key drop (level unlock)
    private GameObject keyPrefab;
    private bool pendingKey;
    private bool keyDropped; // only one key per round
    private int keyDropScore; // score threshold to trigger key drop

    // Tutorial intro
    private int tutorialStep; // 0 = move right, 1 = move left, 2+ = done
    private bool tutorialActive;
    private float tutorialWaveSpawnTime;
    private bool tutorialWaveInFlight;
    private TutorialOverlay tutorialOverlay;

    // Pool references (grabbed from ObjectPooler at Start)
    private ObjectPooler pooler;

    // Single rock pool (all same size).
    private List<GameObject> rockPool;

    // Pre-loaded rock prefabs.
    private GameObject[] rockPrefabs;
    private const int RockPoolSize = 30;

    void Start()
    {
        pooler = FindObjectOfType<ObjectPooler>();

        if (GameState.Mode != GameState.GameMode.Rush)
        {
            enabled = false;
            return;
        }

        if (config == null)
        {
            // Try loading a default from Resources.
            config = Resources.Load<RushConfig>("RushConfig");
        }

        if (config == null)
        {
            // Create a runtime default so Rush Mode works out of the box
            // before the user creates a ScriptableObject asset.
            config = ScriptableObject.CreateInstance<RushConfig>();
            Debug.Log("[SpawnDirector] Using default RushConfig (create Assets/Resources/RushConfig via Assets → Create → Gem Catch → Rush Config to customize).");
        }

        BuildRockPool();
        roundStartTime = Time.time;
        nextWaveSpawnY = ScreenPadding.WorldTop + 2f;

        // Initialize wave memory and seeded generation.
        waveMemory = new WaveMemory(6);
        runSeed = System.Environment.TickCount;
        waveIndex = 0;

        // Load magnet prefab.
        magnetPrefab = Resources.Load<GameObject>("PowerUps/Magnet_V1_0");
        nextMagnetDropTime = MagnetDropInterval;

        // Load shield prefab.
        shieldPrefab = Resources.Load<GameObject>("PowerUps/Shield_V2_1");
        nextShieldDropTime = 40f;

        // Load dice (swap) prefab — only available on levels 2+.
        diceEnabled = LevelManager.SelectedLevel != LevelManager.LevelId.Cave;
        dicePrefab = Resources.Load<GameObject>("PowerUps/Dice_V3_0");
        nextDiceDropTime = 60f;

        // MasterGem (invincibility) — 5s for testing.
        nextMasterGemDropTime = MasterGemDropInterval;

        // Key drop — load prefab and determine score threshold.
        keyPrefab = Resources.Load<GameObject>("PowerUps/Key_V3_0");
        keyDropScore = LevelManager.GetKeyDropScore();
        keyDropped = keyDropScore <= 0; // no key needed if nothing to unlock
        pendingKey = false;

        // Subscribe to score changes to trigger key drop.
        if (!keyDropped && RoundManager.Instance != null)
        {
            RoundManager.Instance.OnScoreChanged += OnScoreChangedForKey;
        }

        if (config.logValidation)
            Debug.Log($"[SpawnDirector] Run seed: {runSeed}");

        // Tutorial intro — only on first-ever play.
        if (LevelManager.SelectedLevel == LevelManager.LevelId.Cave
            && PlayerPrefs.GetInt("TutorialCompleted", 0) == 0)
        {
            tutorialActive = true;
            tutorialStep = 0;
            tutorialWaveInFlight = false;
            tutorialWaveSpawnTime = Time.time + 1.5f; // short delay before first tutorial wave

            // Create the arrow overlay.
            GameObject overlayGo = new GameObject("TutorialOverlay", typeof(TutorialOverlay));
            tutorialOverlay = overlayGo.GetComponent<TutorialOverlay>();
        }
    }

    void BuildRockPool()
    {
        rockPool = new List<GameObject>();

        // Load specific rock prefabs.
        var loaded = new List<GameObject>();
        GameObject r1 = Resources.Load<GameObject>("Rocks/Rock1B");
        GameObject r5 = Resources.Load<GameObject>("Rocks/Rock5B");
        if (r1 != null) loaded.Add(r1);
        if (r5 != null) loaded.Add(r5);
        rockPrefabs = loaded.ToArray();
        if (rockPrefabs.Length == 0)
        {
            Debug.LogWarning("[SpawnDirector] No rock prefabs in Resources/Rocks/.");
            return;
        }

        RushConfig.HazardSize size = config.rockSize;

        for (int i = 0; i < RockPoolSize; i++)
        {
            GameObject prefab = rockPrefabs[i % rockPrefabs.Length];
            GameObject obj = Instantiate(prefab);
            obj.name = $"Rock_{i}";
            obj.SetActive(false);

            obj.transform.localScale = Vector3.one * size.scale;

            FallingObject fo = obj.GetComponent<FallingObject>();
            if (fo == null) fo = obj.AddComponent<FallingObject>();
            fo.SetHazard(true);

            // Strip all existing colliders from prefab.
            foreach (Collider c in obj.GetComponentsInChildren<Collider>())
                Object.Destroy(c);

            Rigidbody prefabRb = obj.GetComponent<Rigidbody>();
            if (prefabRb != null) Object.Destroy(prefabRb);

            SphereCollider sc = obj.AddComponent<SphereCollider>();
            sc.radius = size.colliderRadius;
            sc.isTrigger = true;

            rockPool.Add(obj);
        }

        Debug.Log($"[SpawnDirector] Built rock pool: {rockPool.Count} instances, scale={size.scale}");
    }

    void Update()
    {
        if (!GameState.IsPlaying || GemCatcher.IsGameOver) return;

        // --- Tutorial phase: scripted intro waves before normal gameplay ---
        if (tutorialActive)
        {
            UpdateTutorial();
            return; // skip normal wave generation during tutorial
        }

        float elapsed = Time.time - roundStartTime;
        RushConfig.DifficultyTier tier = config.GetTier(elapsed);

        // Mark power-ups as pending when timers elapse.
        // They'll replace the next gem slot in the wave grid.
        if (elapsed >= nextMagnetDropTime)
        {
            nextMagnetDropTime = elapsed + MagnetDropInterval;
            pendingMagnet = true;
        }
        if (elapsed >= nextShieldDropTime)
        {
            nextShieldDropTime = elapsed + ShieldDropInterval;
            pendingShield = true;
        }
        if (elapsed >= nextDiceDropTime)
        {
            if (diceEnabled)
            {
                nextDiceDropTime = elapsed + DiceDropInterval;
                pendingDice = true;
            }
            else
            {
                nextDiceDropTime = float.MaxValue;
            }
        }
        if (elapsed >= nextMasterGemDropTime)
        {
            nextMasterGemDropTime = elapsed + MasterGemDropInterval;
            pendingMasterGem = true;
        }

        // If no active wave, generate a new one.
        if (activeWave == null)
        {
            var result = WaveGeneratorV2.Generate(
                config, tier, GetPlayAreaLeft(), GetPlayAreaRight(),
                waveMemory, lastWaveFinalRow, runSeed, waveIndex);

            activeWave = result.wave;
            activePlan = result.plan;
            activeWave.fallSpeed = tier.fallSpeed;
            activeTierPause = tier.wavePauseOverride;
            activeRowIndex = 0;
            lastRowSpawnTime = Time.time;
            lastSpawnedWave = activeWave;
            lastSpawnedPlan = activePlan;
            lastResult = result;
            totalWavesGenerated++;
            totalRejectedAttempts += result.candidatesRejected;
            waveIndex++;

            // Track final row for cross-wave validation.
            if (activeWave.rows.Count > 0)
                lastWaveFinalRow = activeWave.rows[activeWave.rows.Count - 1];

            if (config.logValidation)
            {
                Debug.Log($"[SpawnDirector] Wave #{totalWavesGenerated}: {activeWave.archetypeName}, " +
                          $"{activeWave.rows.Count} rows, speed={tier.fallSpeed:F1}, " +
                          $"interest={result.interestScore:F1}, candidates={result.candidatesGenerated}, " +
                          $"rejected={result.candidatesRejected}, seed={result.plan?.seed}");
                if (result.scoreBreakdown != null)
                    Debug.Log($"  Score: {result.scoreBreakdown}");
            }
        }

        // Spawn rows on a timed cadence based on fall speed and row spacing.
        if (activeRowIndex < activeWave.rows.Count)
        {
            float timeBetweenRows = config.rowSpacing / activeWave.fallSpeed;
            if (Time.time - lastRowSpawnTime >= timeBetweenRows || activeRowIndex == 0)
            {
                SpawnRow(activeWave.rows[activeRowIndex], activeWave.fallSpeed);
                activeRowIndex++;
                lastRowSpawnTime = Time.time;
            }
        }
        else
        {
            // Wave is fully spawned. Wait for the wave pause before generating the next.
            float effectivePause = activeTierPause > 0f ? activeTierPause : config.wavePause;
            float pauseTime = effectivePause / activeWave.fallSpeed;
            if (Time.time - lastRowSpawnTime >= pauseTime)
            {
                activeWave = null;
            }
        }
    }

    void SpawnRow(WaveDefinition.Row row, float fallSpeed)
    {
        float spawnY = ScreenPadding.WorldTop + 1.5f;
        bool swapped = PowerUpManager.SwapActive;

        foreach (WaveDefinition.Slot slot in row.slots)
        {
            switch (slot.type)
            {
                case WaveDefinition.SlotType.Hazard:
                    if (swapped)
                        SpawnSwappedGemAt(slot.x, spawnY, fallSpeed);
                    else
                        SpawnHazardAt(slot.x, spawnY, fallSpeed, slot.hazardSizeIndex);
                    break;
                case WaveDefinition.SlotType.Gem:
                    if (swapped)
                        SpawnHazardAt(slot.x, spawnY, fallSpeed, 0);
                    else
                        SpawnGemAt(slot.x, spawnY, fallSpeed);
                    break;
                case WaveDefinition.SlotType.PoisonGem:
                    if (swapped)
                        SpawnHazardAt(slot.x, spawnY, fallSpeed, 0);
                    else
                        SpawnPoisonGemAt(slot.x, spawnY, fallSpeed);
                    break;
            }
        }

        // Debug visualization
        if (config.debugVisualization)
        {
            // Draw safe corridor in Scene view.
            Debug.DrawLine(
                new Vector3(row.safeMinX, spawnY, 0f),
                new Vector3(row.safeMaxX, spawnY, 0f),
                Color.green, 3f);
        }
    }

    void SpawnHazardAt(float x, float y, float fallSpeed, int sizeIdx)
    {
        if (rockPool == null) return;

        GameObject obj = GetInactive(rockPool);
        if (obj == null) return;

        obj.transform.position = new Vector3(x, y, 0f);
        obj.transform.rotation = Quaternion.identity;
        FallingObject fo = obj.GetComponent<FallingObject>();
        if (fo != null)
        {
            fo.ResetObject();
            fo.SetHazard(true);
            fo.verticalOnly = true;
            fo.horizontalSpeed = 0f;
            fo.fallSpeed = fallSpeed;
            fo.InitializeMovement(fallSpeed);
        }
        obj.SetActive(true);
    }

    void SpawnGemAt(float x, float y, float fallSpeed)
    {
        // If a power-up is pending, replace this gem slot with it.
        if (pendingMagnet)
        {
            pendingMagnet = false;
            SpawnMagnetPowerUp(x, y, fallSpeed);
            return;
        }
        if (pendingShield)
        {
            pendingShield = false;
            SpawnShieldPowerUp(x, y, fallSpeed);
            return;
        }
        if (pendingDice)
        {
            pendingDice = false;
            SpawnDicePowerUp(x, y, fallSpeed);
            return;
        }
        if (pendingMasterGem)
        {
            pendingMasterGem = false;
            SpawnMasterGemPowerUp(x, y, fallSpeed);
            return;
        }
        if (pendingKey)
        {
            pendingKey = false;
            SpawnKeyDrop(x, y, fallSpeed);
            return;
        }

        if (pooler == null) return;
        float elapsed = Time.time - roundStartTime;
        float redChance = config.GetTier(elapsed).redGemChance;
        pooler.SpawnRushGemAt(x, y, fallSpeed, redChance);
    }

    void SpawnPoisonGemAt(float x, float y, float fallSpeed)
    {
        if (pooler == null) return;
        pooler.SpawnRushPoisonGemAt(x, y, fallSpeed);
    }

    GameObject GetInactive(List<GameObject> pool)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            if (!pool[i].activeInHierarchy) return pool[i];
        }
        return null;
    }

    float GetPlayAreaLeft() => ScreenPadding.WorldLeft + 0.3f;
    float GetPlayAreaRight() => ScreenPadding.WorldRight - 0.3f;

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (config == null || !config.debugVisualization) return;
        if (lastSpawnedWave == null) return;

        float spawnY = ScreenPadding.WorldTop + 1.5f;

        for (int i = 0; i < lastSpawnedWave.rows.Count; i++)
        {
            var row = lastSpawnedWave.rows[i];
            float y = spawnY - row.yOffset;

            // Draw safe corridor in green.
            Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
            float corridorCenter = (row.safeMinX + row.safeMaxX) * 0.5f;
            float corridorWidth = row.SafeWidth;
            Gizmos.DrawCube(new Vector3(corridorCenter, y, 0f), new Vector3(corridorWidth, 0.15f, 0.1f));

            // Draw slots.
            foreach (var slot in row.slots)
            {
                if (slot.type == WaveDefinition.SlotType.Hazard)
                {
                    Gizmos.color = new Color(1f, 0f, 0f, 0.25f);
                    Gizmos.DrawWireCube(new Vector3(slot.x, y, 0f), new Vector3(slot.width, 0.3f, 0.1f));
                }
                else if (slot.type == WaveDefinition.SlotType.Gem)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawWireSphere(new Vector3(slot.x, y, 0f), 0.12f);
                }
                else if (slot.type == WaveDefinition.SlotType.PoisonGem)
                {
                    Gizmos.color = new Color(0.6f, 0f, 0.8f, 0.8f);
                    Gizmos.DrawWireSphere(new Vector3(slot.x, y, 0f), 0.12f);
                }
            }
        }

        // Draw route paths if available.
        if (lastSpawnedPlan?.routes != null)
        {
            foreach (var route in lastSpawnedPlan.routes.routes)
            {
                if (route.columns == null || route.columns.Length < 2) continue;

                Gizmos.color = route.isSafe
                    ? new Color(0f, 1f, 0f, 0.6f)
                    : new Color(1f, 1f, 0f, 0.6f);

                for (int i = 0; i < route.columns.Length - 1; i++)
                {
                    if (i >= lastSpawnedWave.rows.Count - 1) break;
                    float y1 = spawnY - lastSpawnedWave.rows[i].yOffset;
                    float y2 = spawnY - lastSpawnedWave.rows[i + 1].yOffset;
                    float x1 = RushColumns.GetColumnX(route.columns[i]);
                    float x2 = RushColumns.GetColumnX(route.columns[i + 1]);
                    Gizmos.DrawLine(new Vector3(x1, y1, 0f), new Vector3(x2, y2, 0f));
                }
            }
        }

        // Display stats.
        string label = $"Wave #{totalWavesGenerated}: {lastSpawnedWave.archetypeName}\n" +
                       $"Rejects: {totalRejectedAttempts}";
        if (lastResult != null)
        {
            label += $"\nInterest: {lastResult.interestScore:F1}";
            if (lastResult.scoreBreakdown != null)
                label += $"\n{lastResult.scoreBreakdown}";
        }
        if (lastSpawnedPlan != null)
            label += $"\nSeed: {lastSpawnedPlan.seed}";

        UnityEditor.Handles.Label(
            new Vector3(GetPlayAreaLeft(), spawnY + 1f, 0f), label);
    }
#endif

    void SpawnMagnetPowerUp(float x, float y, float fallSpeed)
    {
        if (magnetPrefab == null)
        {
            Debug.LogWarning("[SpawnDirector] Magnet prefab not loaded!");
            return;
        }

        GameObject obj = Instantiate(magnetPrefab);
        obj.transform.position = new Vector3(x, y, 0f);
        obj.transform.localScale = Vector3.one * 1.15f;

        FallingObject fo = obj.GetComponent<FallingObject>();
        if (fo == null) fo = obj.AddComponent<FallingObject>();
        fo.ResetObject();
        fo.verticalOnly = true;
        fo.horizontalSpeed = 0f;
        fo.fallSpeed = fallSpeed;
        fo.InitializeMovement(fallSpeed);
        fo.isRushMagnet = true;

        var spinner = obj.AddComponent<SimpleSpinner>();
        spinner.speed = new Vector3(0f, 120f, 30f);

        if (obj.GetComponent<Collider>() == null)
        {
            SphereCollider sc = obj.AddComponent<SphereCollider>();
            sc.radius = 0.5f;
            sc.isTrigger = true;
        }

        // Blue glow via MaterialPropertyBlock on all renderers.
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", new Color(0.3f, 0.5f, 1f));
            mpb.SetColor("_EmissionColor", new Color(0.2f, 0.4f, 1f) * 2f);
            r.SetPropertyBlock(mpb);
        }

        obj.SetActive(true);

        if (config.logValidation)
            Debug.Log($"[SpawnDirector] Magnet power-up spawned at ({x:F2}, {y:F2})");
    }

    void SpawnShieldPowerUp(float x, float y, float fallSpeed)
    {
        if (shieldPrefab == null)
        {
            Debug.LogWarning("[SpawnDirector] Shield prefab not loaded!");
            return;
        }

        GameObject obj = Instantiate(shieldPrefab);
        obj.transform.position = new Vector3(x, y, 0f);
        obj.transform.localScale = Vector3.one * 1.725f;

        FallingObject fo = obj.GetComponent<FallingObject>();
        if (fo == null) fo = obj.AddComponent<FallingObject>();
        fo.ResetObject();
        fo.verticalOnly = true;
        fo.horizontalSpeed = 0f;
        fo.fallSpeed = fallSpeed;
        fo.InitializeMovement(fallSpeed);
        fo.isRushShield = true;

        var spinner = obj.AddComponent<SimpleSpinner>();
        spinner.speed = new Vector3(0f, 120f, 30f);

        if (obj.GetComponent<Collider>() == null)
        {
            SphereCollider sc = obj.AddComponent<SphereCollider>();
            sc.radius = 0.5f;
            sc.isTrigger = true;
        }

        // Golden glow via GemGlowVolume (same system gems use).
        var glow = obj.AddComponent<GemGlowVolume>();
        glow.glowColor = new Color(1f, 0.84f, 0f, 1f);
        glow.glowAlpha = 0.9f;
        glow.glowRadius = 1.5f;

        obj.SetActive(true);

        if (config.logValidation)
            Debug.Log($"[SpawnDirector] Shield power-up spawned at ({x:F2}, {y:F2})");
    }

    /// <summary>
    /// Spawn a gem using the upgrade type for the current level (used during swap).
    /// Always spawns the upgrade gem regardless of redGemChance.
    /// </summary>
    void SpawnSwappedGemAt(float x, float y, float fallSpeed)
    {
        if (pooler == null) return;
        // Force upgrade gem by passing redGemChance=1 (100% upgrade).
        pooler.SpawnRushGemAt(x, y, fallSpeed, 1f);
    }

    void SpawnDicePowerUp(float x, float y, float fallSpeed)
    {
        if (dicePrefab == null)
        {
            Debug.LogWarning("[SpawnDirector] Dice prefab not loaded!");
            return;
        }

        GameObject obj = Instantiate(dicePrefab);
        obj.transform.position = new Vector3(x, y, 0f);
        obj.transform.localScale = Vector3.one * 1.15f;

        FallingObject fo = obj.GetComponent<FallingObject>();
        if (fo == null) fo = obj.AddComponent<FallingObject>();
        fo.ResetObject();
        fo.verticalOnly = true;
        fo.horizontalSpeed = 0f;
        fo.fallSpeed = fallSpeed;
        fo.InitializeMovement(fallSpeed);
        fo.isRushDice = true;

        var spinner = obj.AddComponent<SimpleSpinner>();
        spinner.speed = new Vector3(0f, 120f, 30f);

        if (obj.GetComponent<Collider>() == null)
        {
            SphereCollider sc = obj.AddComponent<SphereCollider>();
            sc.radius = 0.5f;
            sc.isTrigger = true;
        }

        // Blue glow via GemGlowVolume.
        var glow = obj.AddComponent<GemGlowVolume>();
        glow.glowColor = new Color(0.2f, 0.5f, 1f, 1f);
        glow.glowAlpha = 0.9f;
        glow.glowRadius = 1.5f;

        obj.SetActive(true);

        if (config.logValidation)
            Debug.Log($"[SpawnDirector] Dice (swap) power-up spawned at ({x:F2}, {y:F2})");
    }

    void SpawnMasterGemPowerUp(float x, float y, float fallSpeed)
    {
        GameObject masterPrefab = Resources.Load<GameObject>("Gems/MasterGem");
        if (masterPrefab == null)
        {
            Debug.LogWarning("[SpawnDirector] MasterGem prefab not loaded!");
            return;
        }

        GameObject obj = Instantiate(masterPrefab);
        obj.transform.position = new Vector3(x, y, 0f);
        obj.transform.localScale = Vector3.one * 4f;

        // Center all child meshes so rotation doesn't orbit them off-screen.
        foreach (Transform child in obj.transform)
        {
            child.localPosition = Vector3.zero;
        }

        FallingObject fo = obj.GetComponent<FallingObject>();
        if (fo == null) fo = obj.AddComponent<FallingObject>();
        fo.ResetObject();
        fo.verticalOnly = true;
        fo.horizontalSpeed = 0f;
        fo.fallSpeed = fallSpeed;
        fo.InitializeMovement(fallSpeed);
        fo.isRushMasterGem = true;

        var spinner = obj.AddComponent<SimpleSpinner>();
        spinner.speed = new Vector3(0f, 90f, 0f);

        // White glow aura.
        var glow = obj.AddComponent<GemGlowVolume>();
        glow.glowColor = Color.white;
        glow.glowAlpha = 0.9f;
        glow.glowRadius = 2f;

        obj.SetActive(true);

        if (config.logValidation)
            Debug.Log($"[SpawnDirector] MasterGem (invincibility) spawned at ({x:F2}, {y:F2})");
    }

    void OnScoreChangedForKey(int score)
    {
        if (keyDropped) return;
        if (score >= keyDropScore)
        {
            keyDropped = true;
            pendingKey = true;
            if (RoundManager.Instance != null)
                RoundManager.Instance.OnScoreChanged -= OnScoreChangedForKey;
            Debug.Log($"[SpawnDirector] Key drop triggered at score {score} (threshold {keyDropScore})");
        }
    }

    void SpawnKeyDrop(float x, float y, float fallSpeed)
    {
        if (keyPrefab == null)
        {
            Debug.LogWarning("[SpawnDirector] Key prefab not loaded!");
            return;
        }

        GameObject obj = Instantiate(keyPrefab);
        obj.transform.position = new Vector3(x, y, 0f);
        obj.transform.localScale = Vector3.one * 1.15f; // same size as other power-ups

        // Zero child mesh offsets to prevent orbiting when spinning.
        foreach (Transform child in obj.transform)
            child.localPosition = Vector3.zero;

        FallingObject fo = obj.GetComponent<FallingObject>();
        if (fo == null) fo = obj.AddComponent<FallingObject>();
        fo.ResetObject();
        fo.verticalOnly = true;
        fo.horizontalSpeed = 0f;
        fo.fallSpeed = fallSpeed;
        fo.InitializeMovement(fo.fallSpeed);
        fo.isRushKey = true;

        var spinner = obj.AddComponent<SimpleSpinner>();
        spinner.speed = new Vector3(0f, 120f, 0f);

        if (obj.GetComponent<Collider>() == null)
        {
            SphereCollider sc = obj.AddComponent<SphereCollider>();
            sc.radius = 0.5f;
            sc.isTrigger = true;
        }

        // Golden glow
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.2f) * 3f);
            r.SetPropertyBlock(mpb);
        }

        // Add glow aura
        var glow = obj.AddComponent<GemGlowVolume>();
        glow.glowColor = new Color(1f, 0.85f, 0.2f, 1f);
        glow.glowRadius = 2f;

        obj.SetActive(true);

        if (config.logValidation)
            Debug.Log($"[SpawnDirector] Key drop spawned at ({x:F2}, {y:F2})");
    }

    // ─── Tutorial intro ─────────────────────────────────────────────────

    private GameObject tutorialGem; // track the gem so we know when it's caught
    private int tutorialScoreBefore; // score before tutorial wave spawned

    /// <summary>
    /// Called each frame during tutorial. Spawns scripted rock walls with a
    /// gap and a gem, shows directional arrows, advances steps on gem catch.
    /// </summary>
    void UpdateTutorial()
    {
        // Check if the tutorial gem has been deactivated (caught or missed).
        if (tutorialWaveInFlight && tutorialGem != null && !tutorialGem.activeInHierarchy)
        {
            int currentScore = RoundManager.Instance != null ? RoundManager.Instance.Score : 0;
            bool wasCaught = currentScore > tutorialScoreBefore;

            if (wasCaught)
            {
                // Gem was caught — advance to next step.
                tutorialWaveInFlight = false;
                if (tutorialOverlay != null) tutorialOverlay.HideArrows();
                tutorialStep++;
                if (tutorialStep >= 2)
                {
                    // Tutorial complete.
                    tutorialActive = false;
                    PlayerPrefs.SetInt("TutorialCompleted", 1);
                    PlayerPrefs.Save();
                    if (tutorialOverlay != null)
                    {
                        Destroy(tutorialOverlay.gameObject);
                        tutorialOverlay = null;
                    }
                    // Reset round timer so power-up timers start from now.
                    roundStartTime = Time.time;
                    Debug.Log("[SpawnDirector] Tutorial complete, starting normal gameplay.");
                    return;
                }
                // Pause before next tutorial wave.
                tutorialWaveSpawnTime = Time.time + 2f;
            }
            else
            {
                // Gem was missed — retry the same step after a pause.
                tutorialWaveInFlight = false;
                if (tutorialOverlay != null) tutorialOverlay.HideArrows();
                tutorialWaveSpawnTime = Time.time + 1.5f;
            }
        }

        // Spawn next tutorial wave when timer elapses.
        if (!tutorialWaveInFlight && Time.time >= tutorialWaveSpawnTime)
        {
            SpawnTutorialWave(tutorialStep);
            tutorialWaveInFlight = true;
        }
    }

    /// <summary>
    /// Spawns a wall of rocks with a gap on the specified side, plus a gem
    /// in the gap. Step 0 = gap on far right, step 1 = gap on far left.
    /// </summary>
    void SpawnTutorialWave(int step)
    {
        float left = GetPlayAreaLeft();
        float right = GetPlayAreaRight();
        float width = right - left;
        float spawnY = ScreenPadding.WorldTop + 1.5f;
        float fallSpeed = 2.5f; // slow so the player has time to react

        // Gap position: far right for step 0, far left for step 1.
        float gapCenter, gapWidth;
        gapWidth = width * 0.2f; // 20% of screen is the gap
        if (step == 0)
            gapCenter = right - gapWidth * 0.5f; // far right
        else
            gapCenter = left + gapWidth * 0.5f;  // far left

        float gapLeft = gapCenter - gapWidth * 0.5f;
        float gapRight = gapCenter + gapWidth * 0.5f;

        // Spawn rocks across the screen, skipping the gap area.
        float rockSpacing = 1.2f; // spacing between rock centers
        for (float x = left; x <= right; x += rockSpacing)
        {
            if (x >= gapLeft && x <= gapRight)
                continue; // skip the gap
            SpawnHazardAt(x, spawnY, fallSpeed, 0);
        }

        // Spawn a gem in the center of the gap.
        if (pooler != null)
            pooler.SpawnRushGemAt(gapCenter, spawnY, fallSpeed, 0f);

        // Track the gem so we know when it's caught.
        if (pooler != null && pooler.CurrentActiveGem != null)
            tutorialGem = pooler.CurrentActiveGem;

        tutorialScoreBefore = RoundManager.Instance != null ? RoundManager.Instance.Score : 0;

        // Show directional arrows.
        int arrowDir = (step == 0) ? 1 : -1;
        if (tutorialOverlay != null)
            tutorialOverlay.ShowArrows(arrowDir);

        Debug.Log($"[SpawnDirector] Tutorial wave {step}: gap at x={gapCenter:F1}, arrows={arrowDir}");
    }

    void OnDestroy()
    {
        // Unsubscribe from score changes.
        if (RoundManager.Instance != null)
            RoundManager.Instance.OnScoreChanged -= OnScoreChangedForKey;

        if (tutorialOverlay != null)
            Destroy(tutorialOverlay.gameObject);

        if (rockPool != null)
        {
            foreach (GameObject obj in rockPool)
            {
                if (obj != null) Destroy(obj);
            }
        }
    }
}
