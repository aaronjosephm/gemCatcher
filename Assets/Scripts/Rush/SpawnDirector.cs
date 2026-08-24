using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Orchestrates Rush Mode spawning. Replaces the simple timer loop in
/// ObjectPooler with wave-based procedural generation.
///
/// Attach to the same GameObject as ObjectPooler, or any persistent
/// object in the scene. Reads from <see cref="RushConfig"/> and drives
/// <see cref="ObjectPooler"/> to spawn pooled gems and hazards.
/// </summary>
public class SpawnDirector : MonoBehaviour
{
    [Tooltip("Drag the RushConfig ScriptableObject here.")]
    public RushConfig config;

    // ---- runtime state --------------------------------------------------
    private float roundStartTime;
    private float nextWaveSpawnY;        // world-Y where the next wave starts
    private WaveDefinition activeWave;   // wave currently being materialized
    private int activeRowIndex;          // next row to spawn in activeWave
    private float lastRowSpawnTime;

    // Debug stats
    private int totalWavesGenerated;
    private int totalRejectedAttempts;
    private WaveDefinition lastSpawnedWave; // for Gizmo drawing

    // Pool references (grabbed from ObjectPooler at Start)
    private ObjectPooler pooler;

    // Hazard instances keyed by size index for O(1) pool lookup.
    // Each size gets its own mini-pool so we can apply the correct
    // scale + collider without fighting pooled reuse.
    private Dictionary<int, List<GameObject>> sizedHazardPools;

    // Pre-loaded rock prefabs.
    private GameObject[] rockPrefabs;
    private const int PoolPerSize = 6;

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

        BuildSizedHazardPools();
        roundStartTime = Time.time;
        nextWaveSpawnY = ScreenPadding.WorldTop + 2f;
    }

    void BuildSizedHazardPools()
    {
        sizedHazardPools = new Dictionary<int, List<GameObject>>();

        // Load specific rock prefabs (uniform shapes only).
        var loaded = new List<GameObject>();
        GameObject r1 = Resources.Load<GameObject>("Rocks/Rock1B");
        GameObject r5 = Resources.Load<GameObject>("Rocks/Rock5B");
        if (r1 != null) loaded.Add(r1);
        if (r5 != null) loaded.Add(r5);
        rockPrefabs = loaded.ToArray();
        if (rockPrefabs == null || rockPrefabs.Length == 0)
        {
            Debug.LogWarning("[SpawnDirector] No rock prefabs in Resources/Rocks/.");
            return;
        }

        for (int sizeIdx = 0; sizeIdx < config.hazardSizes.Length; sizeIdx++)
        {
            RushConfig.HazardSize size = config.hazardSizes[sizeIdx];
            List<GameObject> pool = new List<GameObject>();

            for (int i = 0; i < PoolPerSize; i++)
            {
                GameObject prefab = rockPrefabs[i % rockPrefabs.Length];
                GameObject obj = Instantiate(prefab);
                obj.name = $"Hazard_{size.label}_{i}";
                obj.SetActive(false);

                obj.transform.localScale = Vector3.one * size.scale;

                FallingObject fo = obj.GetComponent<FallingObject>();
                if (fo == null) fo = obj.AddComponent<FallingObject>();
                fo.SetHazard(true);

                // Strip all existing colliders from prefab (asset pack may include MeshColliders).
                foreach (Collider c in obj.GetComponentsInChildren<Collider>())
                    Object.Destroy(c);

                // Remove any Rigidbody the prefab ships with.
                Rigidbody prefabRb = obj.GetComponent<Rigidbody>();
                if (prefabRb != null) Object.Destroy(prefabRb);

                // Set up a single trigger collider for CatchZone bounds checking.
                SphereCollider sc = obj.AddComponent<SphereCollider>();
                sc.radius = size.colliderRadius;
                sc.isTrigger = true;

                pool.Add(obj);
            }

            sizedHazardPools[sizeIdx] = pool;
            Debug.Log($"[SpawnDirector] Built pool for {size.label} rocks: {pool.Count} instances, scale={size.scale}, collider={size.colliderRadius}");
        }
    }

    void Update()
    {
        if (!GameState.IsPlaying || GemCatcher.IsGameOver) return;

        float elapsed = Time.time - roundStartTime;
        RushConfig.DifficultyTier tier = config.GetTier(elapsed);

        // If no active wave, generate a new one.
        if (activeWave == null)
        {
            int attempts;
            activeWave = WaveGenerator.Generate(config, tier, GetPlayAreaLeft(), GetPlayAreaRight(), out attempts);
            activeWave.fallSpeed = tier.fallSpeed;
            activeRowIndex = 0;
            lastRowSpawnTime = Time.time;
            lastSpawnedWave = activeWave;
            totalWavesGenerated++;
            totalRejectedAttempts += Mathf.Max(0, attempts - 1);

            if (config.logValidation)
            {
                Debug.Log($"[SpawnDirector] Wave #{totalWavesGenerated}: {activeWave.archetypeName}, " +
                          $"{activeWave.rows.Count} rows, speed={tier.fallSpeed:F1}, " +
                          $"attempts={attempts}, totalRejects={totalRejectedAttempts}");
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
            float pauseTime = config.wavePause / activeWave.fallSpeed;
            if (Time.time - lastRowSpawnTime >= pauseTime)
            {
                activeWave = null;
            }
        }
    }

    void SpawnRow(WaveDefinition.Row row, float fallSpeed)
    {
        float spawnY = ScreenPadding.WorldTop + 1.5f;

        foreach (WaveDefinition.Slot slot in row.slots)
        {
            switch (slot.type)
            {
                case WaveDefinition.SlotType.Hazard:
                    SpawnHazardAt(slot.x, spawnY, fallSpeed, slot.hazardSizeIndex);
                    break;
                case WaveDefinition.SlotType.Gem:
                    SpawnGemAt(slot.x, spawnY, fallSpeed);
                    break;
                case WaveDefinition.SlotType.PoisonGem:
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
        if (!sizedHazardPools.ContainsKey(sizeIdx)) return;

        List<GameObject> pool = sizedHazardPools[sizeIdx];
        GameObject obj = GetInactive(pool);
        if (obj == null) return;

        obj.transform.position = new Vector3(x, y, 0f);
        // Uniform upright rotation for consistent appearance.
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
        if (pooler == null) return;
        pooler.SpawnRushGemAt(x, y, fallSpeed);
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

            // Draw hazard zones in red.
            Gizmos.color = new Color(1f, 0f, 0f, 0.25f);
            foreach (var slot in row.slots)
            {
                if (slot.type == WaveDefinition.SlotType.Hazard)
                {
                    Gizmos.DrawWireCube(new Vector3(slot.x, y, 0f), new Vector3(slot.width, 0.3f, 0.1f));
                }
                else if (slot.type == WaveDefinition.SlotType.Gem)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawWireSphere(new Vector3(slot.x, y, 0f), 0.12f);
                    Gizmos.color = new Color(1f, 0f, 0f, 0.25f);
                }
                else if (slot.type == WaveDefinition.SlotType.PoisonGem)
                {
                    Gizmos.color = new Color(0.6f, 0f, 0.8f, 0.8f);
                    Gizmos.DrawWireSphere(new Vector3(slot.x, y, 0f), 0.12f);
                    Gizmos.color = new Color(1f, 0f, 0f, 0.25f);
                }
            }
        }

        // Display stats in Scene view via label.
        UnityEditor.Handles.Label(
            new Vector3(GetPlayAreaLeft(), spawnY + 1f, 0f),
            $"Waves: {totalWavesGenerated}  Rejects: {totalRejectedAttempts}");
    }
#endif

    void OnDestroy()
    {
        // Clean up pooled hazards.
        if (sizedHazardPools != null)
        {
            foreach (var kvp in sizedHazardPools)
            {
                foreach (GameObject obj in kvp.Value)
                {
                    if (obj != null) Destroy(obj);
                }
            }
        }
    }
}
