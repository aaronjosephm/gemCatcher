using UnityEngine;

public class GemRotator : MonoBehaviour
{
    public float fallSpeed = 5f;        // Speed at which the gem falls
    public Vector3 rotationSpeed = new Vector3(0f, 100f, 0f); // Speed of rotation along each axis

    // Update is called once per frame
    void Update()
    {
        // Rotate the gem
        transform.Rotate(rotationSpeed * Time.deltaTime);

        // Move the gem downwards
        transform.Translate(Vector3.down * fallSpeed * Time.deltaTime, Space.World);
    }
}