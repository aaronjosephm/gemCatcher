using UnityEngine;

/// <summary>
/// Builds wearable GameObjects procedurally when no prefab asset exists.
/// Uses pixel-art style textures on quads.
/// </summary>
public static class ProceduralWearableFactory
{
    public static GameObject Create(string wearableId)
    {
        switch (wearableId)
        {
            case "eyepatch": return BuildEyePatch();
            default: return null;
        }
    }

    // ─── Eye Patch (pixel art on a quad) ────────────────────────────────

    static GameObject BuildEyePatch()
    {
        GameObject root = new GameObject("EyePatch_Procedural");

        // Patch quad
        GameObject patch = GameObject.CreatePrimitive(PrimitiveType.Quad);
        patch.name = "Patch";
        patch.transform.SetParent(root.transform, false);
        patch.transform.localPosition = Vector3.zero;
        patch.transform.localScale = Vector3.one;

        // Remove the collider — not needed for a cosmetic.
        var col = patch.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);

        // Build the pixel-art texture.
        Texture2D tex = BuildEyePatchTexture();
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.mainTexture = tex;

        // Enable transparency.
        mat.SetFloat("_Surface", 1); // Transparent
        mat.SetFloat("_Blend", 0);   // Alpha
        mat.SetFloat("_AlphaClip", 0);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 3000;
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

        patch.GetComponent<Renderer>().material = mat;

        // Strap quad (horizontal band)
        GameObject strap = GameObject.CreatePrimitive(PrimitiveType.Quad);
        strap.name = "Strap";
        strap.transform.SetParent(root.transform, false);
        strap.transform.localPosition = Vector3.zero;
        strap.transform.localScale = Vector3.one;

        var strapCol = strap.GetComponent<Collider>();
        if (strapCol != null) Object.Destroy(strapCol);

        Texture2D strapTex = BuildStrapTexture();
        Material strapMat = new Material(mat); // clone transparent setup
        strapMat.mainTexture = strapTex;
        strap.GetComponent<Renderer>().material = strapMat;

        return root;
    }

    /// <summary>16×16 pixel art eye patch — dark oval with highlight.</summary>
    static Texture2D BuildEyePatchTexture()
    {
        int w = 16, h = 16;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point; // crisp pixel art
        tex.wrapMode = TextureWrapMode.Clamp;

        Color clear = new Color(0, 0, 0, 0);
        Color black = new Color(0.06f, 0.05f, 0.05f, 1f);
        Color dark  = new Color(0.15f, 0.12f, 0.10f, 1f);
        Color hi    = new Color(0.25f, 0.20f, 0.18f, 1f);

        // Fill transparent
        Color[] pixels = new Color[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

        // Draw an oval patch shape (rows 3-12, centered)
        // Row pattern: width at each row to form an oval
        int[] rowWidths = { 0,0,0, 4, 8, 10, 12, 12, 12, 10, 8, 4, 0,0,0,0 };

        for (int y = 0; y < h; y++)
        {
            int rw = y < rowWidths.Length ? rowWidths[y] : 0;
            if (rw == 0) continue;
            int startX = (w - rw) / 2;
            for (int x = startX; x < startX + rw; x++)
            {
                // Border pixels
                if (y == 3 || y == 11 || x == startX || x == startX + rw - 1)
                    pixels[y * w + x] = dark;
                // Inner highlight on top-left
                else if (y >= 4 && y <= 6 && x >= startX + 1 && x <= startX + 3)
                    pixels[y * w + x] = hi;
                else
                    pixels[y * w + x] = black;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>16×16 pixel art strap — thin horizontal band across the width.</summary>
    static Texture2D BuildStrapTexture()
    {
        int w = 16, h = 16;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color clear = new Color(0, 0, 0, 0);
        Color strap = new Color(0.14f, 0.10f, 0.08f, 1f);
        Color edge  = new Color(0.08f, 0.06f, 0.05f, 1f);

        Color[] pixels = new Color[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

        // Draw a 2-pixel-tall band across the full width at rows 7-8
        for (int x = 0; x < w; x++)
        {
            pixels[7 * w + x] = edge;
            pixels[8 * w + x] = strap;
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}
