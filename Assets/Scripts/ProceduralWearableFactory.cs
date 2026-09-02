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
            case "cowboyhat": return BuildCowboyHat();
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

    // ─── Cowboy Hat (procedural fallback) ─────────────────────────────────

    static GameObject BuildCowboyHat()
    {
        GameObject root = new GameObject("CowboyHat_Procedural");
        Color brown = new Color(0.45f, 0.28f, 0.15f);
        Color darkBrown = new Color(0.3f, 0.18f, 0.08f);

        // Brim — wide flat cylinder
        GameObject brim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        brim.name = "Brim";
        brim.transform.SetParent(root.transform, false);
        brim.transform.localPosition = Vector3.zero;
        brim.transform.localScale = new Vector3(1.8f, 0.04f, 1.8f);
        var bc = brim.GetComponent<Collider>(); if (bc) Object.Destroy(bc);
        SetColor(brim, brown);

        // Crown — taller narrower cylinder
        GameObject crown = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        crown.name = "Crown";
        crown.transform.SetParent(root.transform, false);
        crown.transform.localPosition = new Vector3(0f, 0.2f, 0f);
        crown.transform.localScale = new Vector3(0.7f, 0.25f, 0.7f);
        var cc = crown.GetComponent<Collider>(); if (cc) Object.Destroy(cc);
        SetColor(crown, darkBrown);

        // Band around crown
        GameObject band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        band.name = "Band";
        band.transform.SetParent(root.transform, false);
        band.transform.localPosition = new Vector3(0f, 0.06f, 0f);
        band.transform.localScale = new Vector3(0.75f, 0.04f, 0.75f);
        var bdc = band.GetComponent<Collider>(); if (bdc) Object.Destroy(bdc);
        SetColor(band, new Color(0.2f, 0.12f, 0.05f));

        return root;
    }

    static void SetColor(GameObject go, Color c)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend == null) return;
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", c);
        rend.material = mat;
    }
}
