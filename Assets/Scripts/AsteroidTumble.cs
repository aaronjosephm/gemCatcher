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
        // Random speed between 5-15°/s on each axis, random direction
        rotationSpeed = new Vector3(
            Random.Range(-15f, 15f),
            Random.Range(-15f, 15f),
            Random.Range(-15f, 15f)
        );
    }

    void Update()
    {
        transform.Rotate(rotationSpeed * Time.deltaTime, Space.Self);
    }
}
