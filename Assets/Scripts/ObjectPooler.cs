using System.Collections;
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

        // Start the spawning process
        nextSpawnTime = Time.time + currentSpawnInterval;
    }

    void Update()
    {
        // Check if the current gem is inactive and it's time to spawn a new one
        if ((currentActiveGem == null || !currentActiveGem.activeInHierarchy) && Time.time >= nextSpawnTime)
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

            // Notify the CatcherManager that a new gem has spawned
            BroadcastMessage("OnGemSpawned", SendMessageOptions.DontRequireReceiver);

            // Notify the UIManager about the placement phase
            BroadcastMessage("OnPlacementPhaseStarted", placementTimer, SendMessageOptions.DontRequireReceiver);
        }

        // Update the placement timer
        if (isPlacementPhase)
        {
            placementTimer -= Time.deltaTime;

            // Update the UI with the current timer value
            BroadcastMessage("OnPlacementTimerUpdated", placementTimer, SendMessageOptions.DontRequireReceiver);

            // When the placement phase ends, increase the gem's speed
            if (placementTimer <= 0)
            {
                isPlacementPhase = false;

                // Increase the gem's speed to normal
                if (currentActiveGem != null && currentActiveGem.activeInHierarchy)
                {
                    FallingObject fallingObj = currentActiveGem.GetComponent<FallingObject>();
                    if (fallingObj != null)
                    {
                        fallingObj.UpdateFallSpeed(currentFallSpeed);
                    }
                }

                // Notify the CatcherManager that the placement phase has ended
                BroadcastMessage("OnPlacementPhaseEnded", SendMessageOptions.DontRequireReceiver);
            }
        }
    }

    void SpawnGem()
    {
        // Get a random inactive object from the pool
        GameObject obj = GetRandomPooledObject(objectPool);
        if (obj != null)
        {
            // Set the spawn position at the top of the screen with a random X position
            float randomX = Random.Range(-spawnXRange, spawnXRange);
            obj.transform.position = new Vector3(randomX, Camera.main.orthographicSize, 0f);

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
            // Set a random position for the obstacle (not too close to the top or bottom)
            float randomX = Random.Range(-spawnXRange, spawnXRange);
            float randomY = Random.Range(-Camera.main.orthographicSize * 0.5f, Camera.main.orthographicSize * 0.7f);
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
                Debug.Log($"Difficulty increased to level {currentDifficultyLevel + 1}! Speed: {currentFallSpeed}, Interval: {currentSpawnInterval}");
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
    }
}
