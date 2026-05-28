using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

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

    // Difficulty progression
    public DifficultyLevel[] difficultyLevels;
    private int currentDifficultyLevel = 0;

    private List<GameObject> objectPool; // The pool of objects
    private List<GameObject> obstaclePool; // The pool of obstacles
    private List<GameObject> activeObstacles; // Currently active obstacles
    private GameObject currentActiveGem; // Currently active gem
    private float nextSpawnTime;
    private float currentFallSpeed;
    private float currentSpawnInterval;

    // Timer for catcher placement
    private float placementTimer = 3.0f;
    private bool isPlacementPhase = false;

    // Public events for gameplay state changes. Subscribers should unsubscribe in OnDestroy.
    public event Action GemSpawned;
    public event Action<float> PlacementPhaseStarted; // payload = duration
    public event Action<float> PlacementTimerUpdated; // payload = remaining time
    public event Action PlacementPhaseEnded;

    // Public accessor so other scripts (e.g. CatcherManager) don't need FindObjectsOfType.
    public GameObject CurrentActiveGem => currentActiveGem;

    void Start()
    {
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
        // rather than letting it tick down on a non-existent gem.
        if (gemInactive && isPlacementPhase)
        {
            EndPlacementPhase(applySpeedup: false);
        }

        // Check if the current gem is inactive and it's time to spawn a new one
        if (gemInactive && Time.time >= nextSpawnTime)
        {
            // Clean up any inactive obstacles
            for (int i = activeObstacles.Count - 1; i >= 0; i--)
            {
                if (!activeObstacles[i].activeInHierarchy)
                {
                    activeObstacles.RemoveAt(i);
                }
            }

            // Spawn a new gem
            SpawnGem();

            // Maybe spawn a new obstacle
            if (Random.value < obstacleSpawnChance && activeObstacles.Count < maxObstacles)
            {
                SpawnObstacle();
            }

            // Set the next spawn time
            nextSpawnTime = Time.time + currentSpawnInterval;

            // Start the placement phase
            isPlacementPhase = true;
            placementTimer = 3.0f;

            GemSpawned?.Invoke();
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
        // Get a random inactive object from the pool
        GameObject obj = GetRandomPooledObject(objectPool);
        if (obj != null)
        {
            // Spawn at the top of the safe play area (below any phone notch / camera lens)
            // and within the safe horizontal bounds. We clamp spawnXRange so a too-large
            // value in the Inspector doesn't push gems behind a notch on narrow phones.
            float maxX = Mathf.Max(0.1f, Mathf.Min(spawnXRange, ScreenPadding.WorldRight - 0.3f));
            float minX = Mathf.Min(-0.1f, Mathf.Max(-spawnXRange, ScreenPadding.WorldLeft + 0.3f));
            float randomX = Random.Range(minX, maxX);
            obj.transform.position = new Vector3(randomX, ScreenPadding.WorldTop, 0f);

            // Update the falling object component - ALWAYS set to slow speed initially
            FallingObject fallingObj = obj.GetComponent<FallingObject>();
            if (fallingObj != null)
            {
                // Reset the object to ensure it starts fresh
                fallingObj.ResetObject();

                // Start with a slower fall speed during placement phase
                fallingObj.fallSpeed = placementPhaseFallSpeed;

                // Set higher probability for diagonal movement
                float horizontalBias = Random.Range(-0.8f, 0.8f); // Bias towards non-zero values
                fallingObj.horizontalSpeed = Mathf.Sign(horizontalBias) * Random.Range(0.5f, 1.0f);

                // Make sure the object initializes with the slow speed
                fallingObj.InitializeMovement(placementPhaseFallSpeed);
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
            float randomX = Random.Range(minX, maxX);
            float playHeight = ScreenPadding.WorldTop - ScreenPadding.WorldBottom;
            float yMin = ScreenPadding.WorldBottom + playHeight * 0.25f;
            float yMax = ScreenPadding.WorldBottom + playHeight * 0.85f;
            float randomY = Random.Range(yMin, yMax);
            obj.transform.position = new Vector3(randomX, randomY, 0f);

            // Random rotation
            obj.transform.rotation = Quaternion.Euler(0, 0, Random.Range(0, 360));

            obj.SetActive(true);
            activeObstacles.Add(obj);
        }
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
            int randomIndex = Random.Range(0, inactiveObjects.Count);
            return inactiveObjects[randomIndex];
        }

        return null; // If all objects are active, return null
    }

    void CheckDifficultyProgression(int newScore)
    {
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
