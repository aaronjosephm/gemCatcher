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

        // Create an unlit opaque material for the face
        Renderer rend = faceGo.GetComponent<Renderer>();
        Material faceMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        faceMat.SetTexture("_BaseMap", faceTexture);
        faceMat.SetColor("_BaseColor", Color.white);
        rend.material = faceMat;
    }
}
