using UnityEngine;

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

    void Start()
    {
        // Initialize components and boundaries
        InitializeComponents();
    }

    // Method to reset the object when it's reused from the pool
    public void ResetObject()
    {
        // Re-initialize components in case anything has changed
        InitializeComponents();
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

        // Rotate the object
        transform.Rotate(rotationSpeed * Time.deltaTime);

        // Move the object
        transform.Translate(movementDirection * Time.deltaTime, Space.World);

        // Check and enforce boundaries
        EnforceBoundaries();

        // Check for collisions with obstacles
        CheckObstacleCollisions();

        // Check if the object has fallen past the catcher line — this can only happen
        // if the gem was NOT caught (a caught gem is deactivated by GemCatcher before it
        // gets here), so we treat reaching the bottom as a miss and deduct points.
        // We use the same world-bottom that the catcher uses so the gem disappears
        // right at the catcher level instead of slipping behind the gesture bar.
        if (transform.position.y < bottomBoundary - objectHalfHeight)
        {
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
        // Cast a ray in the movement direction to detect obstacles
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
