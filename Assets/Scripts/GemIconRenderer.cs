using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders gem prefabs to sprite icons at runtime so they can be displayed in the UI
/// (e.g. on the game-over screen) without requiring the developer to author 2D icon
/// artwork for each prefab.
///
/// Strategy: lazily spawn a hidden orthographic camera on a dedicated layer, instantiate
/// a copy of the prefab in front of it, render a single frame to a RenderTexture, copy
/// the result into a Texture2D, and wrap that in a Sprite. Results are cached by prefab
/// name so each gem is only rendered once per session.
/// </summary>
public static class GemIconRenderer
{
  // Layer used to isolate preview gems from the main camera. Most projects leave 31
  // unused; if it's reserved in your project, change this to any unused user layer.
  public const int PreviewLayer = 31;
  public const int IconSize = 256;

  private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
  private static Camera previewCamera;

  // Reset on entering Play Mode so stale references from a previous session (e.g.
  // sprites pointing at destroyed Texture2Ds when Domain Reload is disabled) don't
  // leak into the new run.
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void ResetStaticState()
  {
    cache.Clear();
    previewCamera = null;
  }

  /// <summary>
  /// Returns a cached sprite for the prefab, rendering it on first request. Returns
  /// null if the prefab is null or rendering fails.
  /// </summary>
  public static Sprite GetOrCapture(GameObject prefab)
  {
    if (prefab == null) return null;

    string key = prefab.name;
    if (cache.TryGetValue(key, out Sprite cached) && cached != null && cached.texture != null)
    {
      return cached;
    }

    Sprite sprite = Capture(prefab);
    cache[key] = sprite;
    return sprite;
  }

  private static Sprite Capture(GameObject prefab)
  {
    EnsureCamera();
    if (previewCamera == null) return null;

    // Stage the gem far from the gameplay area so even if something goes wrong with
    // layer culling, the preview won't appear on the main camera.
    Vector3 stagingOrigin = new Vector3(2000f, 2000f, 0f);

    GameObject instance = Object.Instantiate(prefab);
    SetLayerRecursive(instance, PreviewLayer);
    instance.SetActive(true);
    instance.transform.position = stagingOrigin;
    // A 3/4 view reads better than a flat front-on view for most gem shapes.
    instance.transform.rotation = Quaternion.Euler(20f, 35f, 0f);

    // Disable any scripts that might move/rotate the gem in the single frame between
    // Instantiate and Render (the gems all have a FallingObject that would otherwise
    // start drifting downward).
    foreach (MonoBehaviour mb in instance.GetComponentsInChildren<MonoBehaviour>(true))
    {
      mb.enabled = false;
    }
    foreach (ParticleSystem ps in instance.GetComponentsInChildren<ParticleSystem>(true))
    {
      ps.gameObject.SetActive(false);
    }
    foreach (TrailRenderer tr in instance.GetComponentsInChildren<TrailRenderer>(true))
    {
      tr.enabled = false;
    }

    // Frame the camera around the prefab's combined renderer bounds so gems of any
    // size fill a similar fraction of the icon.
    Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
    Vector3 boundsCenter = stagingOrigin;
    float halfSize = 0.6f;
    if (renderers.Length > 0)
    {
      Bounds bounds = renderers[0].bounds;
      for (int i = 1; i < renderers.Length; i++)
      {
        bounds.Encapsulate(renderers[i].bounds);
      }
      boundsCenter = bounds.center;
      halfSize = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
      if (halfSize <= 0.0001f) halfSize = 0.6f;
    }

    previewCamera.transform.position = boundsCenter + new Vector3(0f, 0f, -10f);
    previewCamera.transform.rotation = Quaternion.identity;
    // 1.25x padding so the gem doesn't kiss the edges of the icon.
    previewCamera.orthographicSize = halfSize * 1.25f;

    RenderTexture rt = RenderTexture.GetTemporary(IconSize, IconSize, 16, RenderTextureFormat.ARGB32);
    previewCamera.targetTexture = rt;
    previewCamera.Render();

    RenderTexture prevActive = RenderTexture.active;
    RenderTexture.active = rt;
    Texture2D tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
    tex.ReadPixels(new Rect(0, 0, IconSize, IconSize), 0, 0);
    tex.Apply();
    RenderTexture.active = prevActive;

    previewCamera.targetTexture = null;
    RenderTexture.ReleaseTemporary(rt);

    Sprite sprite = Sprite.Create(tex, new Rect(0, 0, IconSize, IconSize), new Vector2(0.5f, 0.5f));
    sprite.name = prefab.name + "_icon";

    Object.Destroy(instance);
    return sprite;
  }

  private static void EnsureCamera()
  {
    if (previewCamera != null) return;

    GameObject camGo = new GameObject("GemPreviewCamera (auto)");
    camGo.hideFlags = HideFlags.HideAndDontSave;
    previewCamera = camGo.AddComponent<Camera>();
    previewCamera.clearFlags = CameraClearFlags.SolidColor;
    // Transparent background so the icon composites cleanly over the game-over panel.
    previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
    previewCamera.orthographic = true;
    previewCamera.orthographicSize = 0.6f;
    previewCamera.cullingMask = 1 << PreviewLayer;
    previewCamera.nearClipPlane = 0.1f;
    previewCamera.farClipPlane = 50f;
    // Manually invoked via Render(); we don't want it ticking every frame.
    previewCamera.enabled = false;

    // Add a directional light on the same layer so lit materials don't render black.
    GameObject lightGo = new GameObject("PreviewLight");
    lightGo.transform.SetParent(camGo.transform, false);
    lightGo.layer = PreviewLayer;
    lightGo.transform.localRotation = Quaternion.Euler(35f, -25f, 0f);
    Light l = lightGo.AddComponent<Light>();
    l.type = LightType.Directional;
    l.intensity = 1.3f;
    l.cullingMask = 1 << PreviewLayer;
    l.shadows = LightShadows.None;
  }

  private static void SetLayerRecursive(GameObject go, int layer)
  {
    go.layer = layer;
    foreach (Transform child in go.transform)
    {
      SetLayerRecursive(child.gameObject, layer);
    }
  }
}
