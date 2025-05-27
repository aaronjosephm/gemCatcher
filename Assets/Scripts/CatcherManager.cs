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
    private bool userPlacedCatcher = false;
    private ObjectPooler objectPooler;

    // Trajectory prediction
    public LineRenderer trajectoryLine;
    public int trajectoryPoints = 10;
    public float trajectoryTimeStep = 0.1f;

    void Start()
    {
        // Find the object pooler
        objectPooler = FindObjectOfType<ObjectPooler>();

        // Calculate the width of each slot based on the screen size
        float screenWidth = Camera.main.orthographicSize * 2.0f * Camera.main.aspect;
        slotWidth = screenWidth / numberOfSlots;

        // Initialize slot positions
        slotPositions = new Vector3[numberOfSlots];
        slotHighlights = new GameObject[numberOfSlots];

        float startX = -screenWidth / 2.0f + slotWidth / 2.0f; // Starting x position for the slots

        // Calculate the position for each slot at the bottom of the screen
        for (int i = 0; i < numberOfSlots; i++)
        {
            slotPositions[i] = new Vector3(startX + i * slotWidth, -Camera.main.orthographicSize + slotHeight / 2.0f, 0f);

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

        // Detect mouse clicks or touches during placement phase
        if (isPlacementPhase && Input.GetMouseButtonDown(0)) // Detects left mouse click or first touch
        {
            // Get the world position of the click
            Vector3 clickPosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            clickPosition.z = 0f; // Set z to 0 because we're in 2D

            // Determine which section (slot) was clicked
            int slotIndex = GetSlotFromClick(clickPosition);

            // Move the catcher to the corresponding slot
            if (slotIndex != -1) // Check if the click is in a valid slot
            {
                PlaceCatcherInSlot(slotIndex);
                userPlacedCatcher = true;
            }
        }
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
        // Find the active gem
        GameObject activeGem = null;
        FallingObject[] fallingObjects = FindObjectsOfType<FallingObject>();
        foreach (FallingObject obj in fallingObjects)
        {
            if (obj.gameObject.activeInHierarchy)
            {
                activeGem = obj.gameObject;
                break;
            }
        }

        if (activeGem != null)
        {
            // Get the FallingObject component
            FallingObject fallingObj = activeGem.GetComponent<FallingObject>();
            if (fallingObj != null)
            {
                // Enable the trajectory line
                trajectoryLine.enabled = true;

                // Calculate and display the predicted trajectory
                Vector3 position = activeGem.transform.position;
                Vector3 velocity = new Vector3(
                  fallingObj.horizontalSpeed * (fallingObj.horizontalSpeed > 0 ? 1 : -1),
                  -fallingObj.fallSpeed,
                  0
                );

                // Set the first point to the current position
                trajectoryLine.SetPosition(0, position);

                // Calculate the rest of the points
                for (int i = 1; i < trajectoryPoints; i++)
                {
                    // Simple physics prediction (doesn't account for bounces)
                    position += velocity * trajectoryTimeStep;

                    // Check for screen boundaries
                    float screenWidth = Camera.main.orthographicSize * Camera.main.aspect;
                    if (position.x < -screenWidth)
                    {
                        position.x = -screenWidth;
                        velocity.x = -velocity.x;
                    }
                    else if (position.x > screenWidth)
                    {
                        position.x = screenWidth;
                        velocity.x = -velocity.x;
                    }

                    trajectoryLine.SetPosition(i, position);
                }
            }
        }
        else
        {
            trajectoryLine.enabled = false;
        }
    }

    int GetSlotFromClick(Vector3 clickPosition)
    {
        // Check if the click is within the vertical range of the slots
        float bottomY = -Camera.main.orthographicSize;
        float topY = bottomY + slotHeight;

        if (clickPosition.y >= bottomY && clickPosition.y <= topY)
        {
            // Calculate which slot the click was in based on the x position
            float startX = -Camera.main.orthographicSize * Camera.main.aspect;
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

        // Reset the user placed flag
        userPlacedCatcher = false;
    }

    // Called by ObjectPooler when the placement phase ends
    void OnPlacementPhaseEnded()
    {
        // If the user didn't place the catcher, place it randomly
        if (!userPlacedCatcher)
        {
            // Choose a random slot
            int randomSlot = Random.Range(0, numberOfSlots);
            PlaceCatcherInSlot(randomSlot);

            // Play a sound to indicate random placement
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.Play("RandomPlacement");
            }
        }
    }

    void OnDestroy()
    {
        // Unsubscribe from events when this object is destroyed
        GemCatcher.OnScoreChanged -= UpdateScoreDisplay;
    }
}
