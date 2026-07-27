using UnityEngine;

/// <summary>
/// Builds a procedurally-modeled gold-bar GameObject from scratch — a
/// rectangular brick with a slight trapezoidal taper, polished gold metallic
/// material, gold-colored trail, and the standard FallingObject + GemCatcher
/// scripts so it integrates with the existing spawn / catch pipeline.
///
/// We don't try to retint a gem prefab into a bar — gem meshes are spheres /
/// crystals and stretching them flat just looks like a squashed gem. A real
/// gold bar is a brick shape, so we build a brick. The "base" cube is the
/// chunky body; a slightly smaller "top" cube stacked on it gives the
/// trapezoidal silhouette real LBMA Good Delivery bars have (wider at the
/// bottom, narrower at the top, with sloped sides reading at a glance).
///
/// The returned GameObject is in the inactive state, so the pool can hold
/// it without it falling on round start. The pooler activates / deactivates
/// it the same way it does normal gem prefabs.
/// </summary>
public static class GoldBarFactory
{
    // Bar proportions — the parent transform is unit-scale so FallingObject's
    // CaptureOriginalScaleIfNeeded baseline is (1,1,1). All shaping happens on
    // child meshes so the score-driven gem-shrink (which multiplies the parent
    // localScale) applies cleanly without any per-axis fighting.
    //
    // Values chosen by eyeballing photos of standard cast bars: width ~2.4x
    // height, depth ~1.2x height, top face about 70% of the base width on the
    // long axis and 80% on the short axis. Final on-screen size is set by the
    // base (1,1,1) parent scale times whatever score-driven factor is active
    // at spawn time.
    private const float BarWidth   = 1.20f;
    private const float BarHeight  = 0.50f;
    private const float BarDepth   = 0.60f;
    private const float TopWidth   = 0.85f;
    private const float TopHeight  = 0.18f;
    private const float TopDepth   = 0.48f;

    // Catch radius. SphereCollider so the existing GemCatcher hit-test (which
    // assumes spheres) keeps working. Sized to match the bar's half-width so
    // the player can catch it the moment any part of the bar enters the
    // catcher slot, mirroring the feel of catching a wide-ish gem.
    private const float CatchRadius = 0.65f;

    // Material colors. Albedo is a warm yellow gold — slightly desaturated so
    // the metallic + smoothness PBR response does most of the work. Emission
    // is a soft gold glow keyed below 1.0 so the bar reads as "lit gold," not
    // a glowing arcade icon.
    private static readonly Color BarAlbedo   = new Color(1.00f, 0.78f, 0.20f);
    private static readonly Color BarEmission = new Color(1.00f, 0.65f, 0.10f) * 0.45f;

    // Trail colors for the gold streak trailing behind the bar as it falls.
    private static readonly Color TrailStart = new Color(1.00f, 0.95f, 0.55f, 0.90f);
    private static readonly Color TrailEnd   = new Color(1.00f, 0.70f, 0.05f, 0.0f);

    /// <summary>
    /// Builds a fully-formed Gold Bar GameObject and returns it in the
    /// inactive state. Caller is responsible for parenting / pooling /
    /// activating it. Components attached: MeshRenderer, SphereCollider,
    /// TrailRenderer, FallingObject, GemCatcher.
    /// </summary>
    public static GameObject Create()
    {
        // Parent transform — keeps the score-driven scale path simple. Made
        // inactive immediately so component Awake/Start methods (FallingObject,
        // GemCatcher) defer until the pool activates the bar at spawn time,
        // matching how Instantiate(prefab); SetActive(false); behaves.
        GameObject bar = new GameObject("GoldBar");
        bar.SetActive(false);

        // ---- Body mesh ------------------------------------------------------
        // Base brick. Cube primitive comes with a MeshFilter, MeshRenderer, and
        // BoxCollider; we ditch the BoxCollider on the child so the parent's
        // SphereCollider is the single catch hitbox.
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(bar.transform, worldPositionStays: false);
        body.transform.localPosition = Vector3.zero;
        body.transform.localScale = new Vector3(BarWidth, BarHeight, BarDepth);
        Object.Destroy(body.GetComponent<Collider>());

        // ---- Top cap mesh ---------------------------------------------------
        // Sits flush on top of the body and is slightly smaller in width and
        // depth, giving the eye the trapezoidal slope cast bars have without
        // needing a custom mesh. Y position = half body height + half cap
        // height so the bottom face of the cap aligns with the top face of
        // the body.
        GameObject top = GameObject.CreatePrimitive(PrimitiveType.Cube);
        top.name = "TopCap";
        top.transform.SetParent(bar.transform, worldPositionStays: false);
        top.transform.localPosition = new Vector3(0f, (BarHeight + TopHeight) * 0.5f, 0f);
        top.transform.localScale = new Vector3(TopWidth, TopHeight, TopDepth);
        Object.Destroy(top.GetComponent<Collider>());

        // ---- Shared gold material -------------------------------------------
        // One material instance shared between body and cap so the two faces
        // always lit identically — even if some downstream tweak modifies the
        // material at runtime, both pieces stay in sync.
        Material gold = BuildGoldMaterial();
        body.GetComponent<Renderer>().sharedMaterial = gold;
        top.GetComponent<Renderer>().sharedMaterial = gold;

        // ---- Catch hitbox ---------------------------------------------------
        // Sphere on the parent. GemCatcher already uses GetComponent<SphereCollider>
        // on the gem to compute catch radius, so this Just Works™ with the
        // existing catch dispatch.
        SphereCollider hit = bar.AddComponent<SphereCollider>();
        hit.radius = CatchRadius;
        hit.isTrigger = false;

        // ---- Trail ----------------------------------------------------------
        TrailRenderer trail = bar.AddComponent<TrailRenderer>();
        trail.time = 0.45f;
        trail.startWidth = 0.55f;
        trail.endWidth = 0.0f;
        trail.startColor = TrailStart;
        trail.endColor = TrailEnd;
        trail.minVertexDistance = 0.05f;
        trail.material = BuildTrailMaterial();

        // ---- Falling / catch behavior ---------------------------------------
        // FallingObject expects the Renderer to live on its own GameObject so
        // it can read .bounds for boundary clamping. We don't have a renderer
        // on the parent, but FallingObject only checks it inside an `if (r !=
        // null)` guard — boundary calc falls back to the camera frustum, which
        // is the right behavior here since the bar is a multi-mesh object.
        FallingObject falling = bar.AddComponent<FallingObject>();
        falling.bounceFactor = 0.8f;

        // GemCatcher handles per-frame catch detection; needs a SphereCollider
        // on the same GO (added above).
        bar.AddComponent<GemCatcher>();

        return bar;
    }

    /// <summary>
    /// Builds the polished-gold PBR material used on both the body and the
    /// top cap. Standard shader, fully metallic, high smoothness, with a soft
    /// emissive bias so the bar still reads as gold under poor scene lighting.
    /// </summary>
    private static Material BuildGoldMaterial()
    {
        // Standard shader is bundled with every Unity install (Built-in RP).
        // If a project ever migrates to URP/HDRP this falls back gracefully —
        // the magenta-error case would still draw the cube silhouette so the
        // gameplay shape stays visible while the artist swaps shaders.
        Shader standard = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Standard");
        Material m = new Material(standard != null ? standard : Shader.Find("Sprites/Default"));
        m.name = "GoldBarMat";

        if (m.HasProperty("_BaseColor"))  m.SetColor("_BaseColor", BarAlbedo);
        else if (m.HasProperty("_Color"))  m.color = BarAlbedo;
        if (m.HasProperty("_Metallic"))  m.SetFloat("_Metallic", 1.0f);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.85f);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.85f);
        if (m.HasProperty("_EmissionColor"))
        {
            m.SetColor("_EmissionColor", BarEmission);
            m.EnableKeyword("_EMISSION");
        }
        return m;
    }

    /// <summary>
    /// Builds an additive-style trail material. Uses Sprites/Default which is
    /// the canonical pick for TrailRenderers — supports the per-vertex color
    /// gradient the trail uses, no extra shader work needed.
    /// </summary>
    private static Material BuildTrailMaterial()
    {
        Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit")
              ?? Shader.Find("Sprites/Default");
        Material m = new Material(s != null ? s : Shader.Find("Universal Render Pipeline/Lit"));
        m.name = "GoldBarTrailMat";
        return m;
    }
}
