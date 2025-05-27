using UnityEngine;

public class Obstacle : MonoBehaviour
{
  public enum ObstacleType
  {
    Static,     // Doesn't move
    Rotating,   // Rotates in place
    Moving      // Moves back and forth
  }

  public ObstacleType type = ObstacleType.Static;

  // Rotation settings
  public float rotationSpeed = 50f;
  public Vector3 rotationAxis = Vector3.forward;

  // Movement settings
  public float movementSpeed = 2f;
  public float movementDistance = 2f;
  private Vector3 startPosition;
  private Vector3 movementDirection;
  private float movementProgress = 0f;

  // Visual settings
  public Color obstacleColor = Color.white;
  private Renderer obstacleRenderer;

  // Physics settings
  public float bounceFactor = 1.0f; // How much to affect the gem's bounce

  void Start()
  {
    // Cache the start position for moving obstacles
    startPosition = transform.position;

    // Set a random movement direction for moving obstacles
    movementDirection = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0).normalized;

    // Get the renderer component
    obstacleRenderer = GetComponent<Renderer>();
    if (obstacleRenderer != null)
    {
      // Set the obstacle color
      obstacleRenderer.material.color = obstacleColor;
    }

    // Make sure the obstacle has a collider
    Collider collider = GetComponent<Collider>();
    if (collider == null)
    {
      // Add a box collider if none exists
      BoxCollider boxCollider = gameObject.AddComponent<BoxCollider>();
      boxCollider.isTrigger = false;
    }

    // Set the layer to the obstacle layer (assuming layer 8 is for obstacles)
    gameObject.layer = 8; // Make sure this matches the layer mask in FallingObject
  }

  void Update()
  {
    // Handle behavior based on obstacle type
    switch (type)
    {
      case ObstacleType.Rotating:
        transform.Rotate(rotationAxis, rotationSpeed * Time.deltaTime);
        break;

      case ObstacleType.Moving:
        UpdateMovement();
        break;

      case ObstacleType.Static:
      default:
        // Static obstacles don't need updates
        break;
    }
  }

  void UpdateMovement()
  {
    // Calculate movement using a sine wave for smooth back-and-forth
    movementProgress += Time.deltaTime * movementSpeed;
    float offset = Mathf.Sin(movementProgress) * movementDistance;

    // Apply the movement
    transform.position = startPosition + movementDirection * offset;
  }

  void OnCollisionEnter(Collision collision)
  {
    // Check if we collided with a gem
    FallingObject fallingObject = collision.gameObject.GetComponent<FallingObject>();
    if (fallingObject != null)
    {
      // Play a bounce sound if available
      if (SoundManager.Instance != null)
      {
        SoundManager.Instance.PlayWithRandomPitch("ObstacleBounce", 0.8f, 1.2f);
      }

      // Visual feedback for collision
      if (obstacleRenderer != null)
      {
        // Flash the obstacle briefly
        StartCoroutine(FlashObstacle());
      }
    }
  }

  System.Collections.IEnumerator FlashObstacle()
  {
    // Store the original color
    Color originalColor = obstacleRenderer.material.color;

    // Flash to a bright color
    obstacleRenderer.material.color = Color.white;

    // Wait a short time
    yield return new WaitForSeconds(0.1f);

    // Return to the original color
    obstacleRenderer.material.color = originalColor;
  }
}
