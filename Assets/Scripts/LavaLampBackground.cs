using UnityEngine;

/// <summary>
/// Procedural lava lamp background rendered as a fullscreen quad behind everything.
/// Uses the Custom/LavaLamp shader with layered sine-wave blobs that smoothly shift
/// between vibrant colors over time, creating an organic, hypnotic effect.
/// </summary>
public class LavaLampBackground : MonoBehaviour
{
    private Material material;
    private GameObject quad;

    void Start()
    {
        Shader shader = Shader.Find("Custom/LavaLamp");
        if (shader == null)
        {
            Debug.LogWarning("[LavaLampBackground] Custom/LavaLamp shader not found, falling back.");
            shader = Shader.Find("Unlit/Color");
        }

        material = new Material(shader);
        material.SetFloat("_Speed", 0.3f);

        quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "LavaLampQuad";
        Destroy(quad.GetComponent<Collider>());
        quad.GetComponent<Renderer>().material = material;
        quad.GetComponent<Renderer>().sortingOrder = -1000;

        Camera cam = Camera.main;
        if (cam != null)
        {
            float height, width;
            if (cam.orthographic)
            {
                height = cam.orthographicSize * 2f;
                width = height * cam.aspect;
                quad.transform.position = new Vector3(
                    cam.transform.position.x,
                    cam.transform.position.y,
                    cam.transform.position.z + 50f);
            }
            else
            {
                float distance = cam.farClipPlane * 0.9f;
                height = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                width = height * cam.aspect;
                quad.transform.position = new Vector3(
                    cam.transform.position.x,
                    cam.transform.position.y,
                    cam.transform.position.z + distance);
            }
            quad.transform.localScale = new Vector3(width * 1.2f, height * 1.2f, 1f);
        }
    }

    void OnDestroy()
    {
        if (quad != null) Destroy(quad);
        if (material != null) Destroy(material);
    }
}
