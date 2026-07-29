using UnityEngine;

/// <summary>
/// Creates Catchy's face on the front of the catcher cube using small
/// procedural quads — two square eyes and a smile. No texture needed.
/// </summary>
public class CatchyFace : MonoBehaviour
{
    void Start()
    {
        float z = -0.502f; // Slightly in front of the -Z face
        Color dark = new Color(0.1f, 0.1f, 0.15f, 1f);

        // Left eye
        CreateFacePart("LeftEye", new Vector3(-0.22f, 0.12f, z), new Vector3(0.14f, 0.14f, 0.01f), dark);

        // Right eye
        CreateFacePart("RightEye", new Vector3(0.22f, 0.12f, z), new Vector3(0.14f, 0.14f, 0.01f), dark);

        // Smile - built from a horizontal bar and two corner pieces
        CreateFacePart("SmileMid", new Vector3(0f, -0.18f, z), new Vector3(0.32f, 0.06f, 0.01f), dark);
        CreateFacePart("SmileLeft", new Vector3(-0.16f, -0.14f, z), new Vector3(0.06f, 0.06f, 0.01f), dark);
        CreateFacePart("SmileRight", new Vector3(0.16f, -0.14f, z), new Vector3(0.06f, 0.06f, 0.01f), dark);
    }

    void CreateFacePart(string name, Vector3 localPos, Vector3 localScale, Color color)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Quad);
        part.name = name;
        part.transform.SetParent(transform, false);
        part.transform.localPosition = localPos;
        part.transform.localRotation = Quaternion.identity;
        part.transform.localScale = localScale;

        Destroy(part.GetComponent<Collider>());

        Renderer rend = part.GetComponent<Renderer>();
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetColor("_BaseColor", color);
        rend.material = mat;
    }
}
