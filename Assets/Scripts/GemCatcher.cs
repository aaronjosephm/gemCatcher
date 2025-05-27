using UnityEngine;

public class GemCatcher : MonoBehaviour
{
    private Transform catcher; // Reference to the catcher (Box)
    private BoxCollider catcherCollider; // Catcher's BoxCollider
    private SphereCollider gemCollider; // Gem's SphereCollider

    private Vector3 catcherSize;
    private Vector3 catcherCenter;

    // Static score tracking
    public static int Score { get; private set; }

    // Event for score changes
    public delegate void ScoreChangedDelegate(int newScore);
    public static event ScoreChangedDelegate OnScoreChanged;

    // Public method to reset the score
    public static void ResetScore()
    {
        Score = 0;
        OnScoreChanged?.Invoke(Score);
    }

    void Start()
    {
        // Cache the gem's collider if it exists
        gemCollider = GetComponent<SphereCollider>();

        // Find the catcher at start
        FindCatcher();
    }

    void Update()
    {
        // Check if the gem crosses the catcher's boundaries
        if (IsGemWithinCatcherBounds())
        {
            // Increase score
            Score++;

            // Notify listeners of score change
            OnScoreChanged?.Invoke(Score);

            // Play catch effect
            PlayCatchEffect();

            // Deactivate the gem
            gameObject.SetActive(false);
        }
    }

    void PlayCatchEffect()
    {
        // Create a simple particle effect at the gem's position
        // This is a placeholder - you might want to use a proper particle system
        Debug.Log($"Gem caught! Score: {Score}");
    }

    void FindCatcher()
    {
        GameObject catcherRef = GameObject.FindWithTag("Catcher");
        if (catcherRef)
        {
            catcher = catcherRef.transform;
            catcherCollider = catcher.GetComponent<BoxCollider>();
            UpdateCatcherBounds();
        }
    }

    void UpdateCatcherBounds()
    {
        if (catcher != null && catcherCollider != null)
        {
            catcherSize = Vector3.Scale(catcherCollider.size, catcher.localScale); // Adjust size by the catcher's scale
            catcherCenter = catcherCollider.center + catcher.position;
        }
    }

    bool IsGemWithinCatcherBounds()
    {
        // If we don't have a catcher reference, try to find it
        if (catcher == null)
        {
            FindCatcher();
            if (catcher == null) return false;
        }

        // Update the catcher bounds in case it moved
        UpdateCatcherBounds();

        // Get the gem's current position
        Vector3 gemPosition = transform.position;

        // Get the gem's radius if it has a SphereCollider
        float gemRadius = gemCollider != null ? gemCollider.radius * transform.localScale.x : 0.1f;

        // Check if the gem's position is within the catcher's bounds
        bool isWithinHorizontalBounds = Mathf.Abs(gemPosition.x - catcherCenter.x) <= (catcherSize.x / 2 + gemRadius);
        bool isWithinVerticalBounds = Mathf.Abs(gemPosition.y - catcherCenter.y) <= (catcherSize.y / 2 + gemRadius);

        // Optionally check for the z-axis if you're working in 3D
        bool isWithinDepthBounds = Mathf.Abs(gemPosition.z - catcherCenter.z) <= (catcherSize.z / 2 + gemRadius);

        // Return true if the gem is within all bounds
        return isWithinHorizontalBounds && isWithinVerticalBounds && isWithinDepthBounds;
    }
}
