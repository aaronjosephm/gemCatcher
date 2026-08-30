using UnityEngine;

/// <summary>
/// Builds wearable GameObjects procedurally when no prefab asset exists.
/// </summary>
public static class ProceduralWearableFactory
{
    /// <summary>
    /// Returns a procedurally built wearable, or null if the id has no
    /// procedural implementation.
    /// </summary>
    public static GameObject Create(string wearableId)
    {
        switch (wearableId)
        {
            case "eye_patch": return BuildEyePatch();
            default: return null;
        }
    }

    // ─── Eye Patch ──────────────────────────────────────────────────────

    static GameObject BuildEyePatch()
    {
        GameObject root = new GameObject("EyePatch_Procedural");

        // ── Patch (dark oval disc over one eye) ──
        GameObject patch = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        patch.name = "Patch";
        patch.transform.SetParent(root.transform, false);
        // Flatten into a thin disc, stretched into an oval.
        patch.transform.localScale = new Vector3(0.38f, 0.015f, 0.30f);
        patch.transform.localPosition = new Vector3(0.12f, 0.05f, 0.42f);
        // Tilt slightly to conform to the face curve.
        patch.transform.localRotation = Quaternion.Euler(80f, 0f, 0f);

        Renderer patchRend = patch.GetComponent<Renderer>();
        Material patchMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        patchMat.color = new Color(0.08f, 0.06f, 0.06f); // near-black leather
        patchMat.SetFloat("_Smoothness", 0.3f);
        patchRend.material = patchMat;

        // ── Strap (thin band going diagonally around the head) ──
        // Left strap segment (patch → left side of head)
        CreateStrapSegment(root.transform, "StrapLeft",
            new Vector3(0.12f, 0.05f, 0.42f),
            new Vector3(-0.35f, 0.15f, 0.10f));

        // Right strap segment (patch → right/back of head)
        CreateStrapSegment(root.transform, "StrapRight",
            new Vector3(0.12f, 0.05f, 0.42f),
            new Vector3(-0.35f, 0.15f, -0.10f));

        return root;
    }

    static void CreateStrapSegment(Transform parent, string name, Vector3 from, Vector3 to)
    {
        GameObject strap = GameObject.CreatePrimitive(PrimitiveType.Cube);
        strap.name = name;
        strap.transform.SetParent(parent, false);

        Vector3 mid = (from + to) * 0.5f;
        float length = Vector3.Distance(from, to);
        strap.transform.localPosition = mid;
        strap.transform.localScale = new Vector3(0.04f, 0.02f, length);
        strap.transform.LookAt(parent.TransformPoint(to), Vector3.up);

        Renderer rend = strap.GetComponent<Renderer>();
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = new Color(0.12f, 0.08f, 0.06f); // dark brown leather
        mat.SetFloat("_Smoothness", 0.2f);
        rend.material = mat;
    }
}
