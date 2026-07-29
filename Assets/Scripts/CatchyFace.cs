using UnityEngine;

/// <summary>
/// Attaches Catchy's face texture to the front of the catcher cube.
/// Spawns a quad child slightly in front of the -Z face so the face
/// is visible to the camera without z-fighting.
/// </summary>
public class CatchyFace : MonoBehaviour
{
    private static Texture2D faceTexture;

    void Start()
    {
        if (faceTexture == null)
            faceTexture = Resources.Load<Texture2D>("Textures/CatchyFace");

        if (faceTexture == null) return;

        // Create a quad child on the front face (-Z direction faces the camera)
        GameObject faceGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        faceGo.name = "CatchyFace";
        faceGo.transform.SetParent(transform, false);

        // Position slightly in front of the cube face (cube is 1 unit, so face is at -0.5 local Z)
        faceGo.transform.localPosition = new Vector3(0f, 0f, -0.501f);
        faceGo.transform.localRotation = Quaternion.identity;
        faceGo.transform.localScale = new Vector3(0.9f, 0.9f, 1f);

        // Remove the collider (we don't want it interfering with gem catching)
        Destroy(faceGo.GetComponent<Collider>());

        // Create an unlit transparent material so only the face features show
        Renderer rend = faceGo.GetComponent<Renderer>();
        Material faceMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        faceMat.SetTexture("_BaseMap", faceTexture);
        faceMat.SetColor("_BaseColor", Color.white);

        // Enable alpha transparency
        faceMat.SetFloat("_Surface", 1f); // Transparent
        faceMat.SetFloat("_Blend", 0f);   // Alpha
        faceMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        faceMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        faceMat.SetFloat("_ZWrite", 0f);
        faceMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        faceMat.renderQueue = 3000;

        rend.material = faceMat;
    }
}
