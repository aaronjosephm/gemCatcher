using UnityEngine;

/// <summary>
/// Rotates the attached object at a constant speed (degrees/second per axis).
/// </summary>
public class SimpleSpinner : MonoBehaviour
{
    public Vector3 speed = new Vector3(0f, 120f, 30f);

    void Update()
    {
        transform.Rotate(speed * Time.unscaledDeltaTime);
    }
}
