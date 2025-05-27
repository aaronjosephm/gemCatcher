using UnityEngine;

public class BoundaryManager : MonoBehaviour
{
    public GameObject leftBoundary;
    public GameObject rightBoundary;
    public GameObject bottomBoundary;

    public float zPosition = 0f; // Ensure boundaries are at the same Z position as the falling objects
    public bool visualizeBoundaries = true; // Whether to make boundaries visible

    void Start()
    {
        PositionBoundaryColliders();
    }

    void PositionBoundaryColliders()
    {
        Camera camera = Camera.main;

        // Calculate the vertical and horizontal boundaries
        float verticalSize = camera.orthographicSize;
        float aspectRatio = camera.aspect;
        float horizontalSize = verticalSize * aspectRatio;

        // Calculate positions for the boundary colliders
        Vector3 leftPos = new Vector3(-horizontalSize, 0f, zPosition);
        Vector3 rightPos = new Vector3(horizontalSize, 0f, zPosition);
        Vector3 bottomPos = new Vector3(0f, -verticalSize, zPosition);

        // Set positions and sizes for the boundary GameObjects
        SetBoundaryPositionAndSize(leftBoundary, leftPos, new Vector3(0.1f, 2 * verticalSize, 1f));
        SetBoundaryPositionAndSize(rightBoundary, rightPos, new Vector3(0.1f, 2 * verticalSize, 1f));
        SetBoundaryPositionAndSize(bottomBoundary, bottomPos, new Vector3(2 * horizontalSize, 0.1f, 1f));
    }

    void SetBoundaryPositionAndSize(GameObject boundary, Vector3 position, Vector3 size)
    {
        if (boundary != null)
        {
            boundary.transform.position = position;
            BoxCollider boxCollider = boundary.GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                boxCollider = boundary.AddComponent<BoxCollider>();
            }
            boxCollider.size = size;
            boxCollider.isTrigger = false; // Set as needed (false if you want physical collisions)

            // Make the boundary visible or invisible based on the setting
            Renderer renderer = boundary.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = visualizeBoundaries;
            }
        }
        else
        {
            // Instead of a warning log, create the boundary if it doesn't exist
            boundary = CreateBoundary(position, size);
        }
    }

    GameObject CreateBoundary(Vector3 position, Vector3 size)
    {
        GameObject boundary = GameObject.CreatePrimitive(PrimitiveType.Cube);
        boundary.transform.position = position;
        boundary.transform.localScale = size;
        boundary.transform.parent = transform; // Parent to this object for organization

        // Make the boundary visible or invisible based on the setting
        Renderer renderer = boundary.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.enabled = visualizeBoundaries;

            // Set a semi-transparent material if visible
            if (visualizeBoundaries)
            {
                Material material = new Material(Shader.Find("Standard"));
                material.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
                renderer.material = material;
            }
        }

        return boundary;
    }
}
