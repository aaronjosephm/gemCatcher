using UnityEngine;

/// <summary>
/// Slowly tumbles the attached object on all axes.
/// Attach to each asteroid/rock in the scene.
/// Each instance picks a random rotation speed so they don't all spin identically.
/// </summary>
public class AsteroidTumble : MonoBehaviour
{
    private Vector3 rotationSpeed;

    void Start()
    {
        // Disable colliders so nothing pushes the rock around
        foreach (var col in GetComponentsInChildren<Collider>())
            col.enabled = false;

        // Random Y-axis spin only (like a turntable) so the rock
        // doesn't clip through the background plane behind it
        float speed = Random.Range(5f, 15f) * (Random.value > 0.5f ? 1f : -1f);
        rotationSpeed = new Vector3(speed, 0f, 0f);
    }

    void Update()
    {
        transform.Rotate(rotationSpeed * Time.deltaTime, Space.Self);
    }
}
