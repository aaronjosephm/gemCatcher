using UnityEngine;

/// <summary>
/// Spins the attached object around a configurable axis.
/// Set the axis and speed in the Inspector for each rock.
/// </summary>
public class AsteroidTumble : MonoBehaviour
{
    public enum RotationAxis { X, Y, Z }

    [Tooltip("Which axis to rotate around")]
    public RotationAxis axis = RotationAxis.Z;

    [Tooltip("Rotation speed in degrees per second")]
    public float speed = 10f;

    [Tooltip("Reverse the rotation direction")]
    public bool reverse = false;

    void Start()
    {
        // Disable colliders so nothing pushes the rock around
        foreach (var col in GetComponentsInChildren<Collider>())
            col.enabled = false;
    }

    void Update()
    {
        float s = speed * (reverse ? -1f : 1f) * Time.deltaTime;
        switch (axis)
        {
            case RotationAxis.X: transform.Rotate(s, 0f, 0f, Space.Self); break;
            case RotationAxis.Y: transform.Rotate(0f, s, 0f, Space.Self); break;
            case RotationAxis.Z: transform.Rotate(0f, 0f, s, Space.Self); break;
        }
    }
}
