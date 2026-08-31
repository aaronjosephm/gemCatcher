using UnityEngine;

/// <summary>
/// Manages catchy skin catalog, purchases, and equipment.
/// Skins change catchy's base color or material pattern.
/// </summary>
public static class SkinManager
{
    public enum SkinType { SolidColor, Camo, PrefabMaterial }

    [System.Serializable]
    public struct SkinDef
    {
        public string id;
        public string displayName;
        public long price;
        public SkinType type;
        public Color primaryColor;
        public Color secondaryColor; // for patterns like camo
        public string materialPrefabPath; // for PrefabMaterial type
    }

    private static readonly SkinDef[] catalog = new SkinDef[]
    {
        new SkinDef
        {
            id = "default",
            displayName = "Default",
            price = 0,
            type = SkinType.SolidColor,
            primaryColor = Color.white, // handled by ApplyGlassAppearance
        },
        new SkinDef
        {
            id = "purple",
            displayName = "Purple",
            price = 10,
            type = SkinType.SolidColor,
            primaryColor = new Color(0.55f, 0.20f, 0.80f),
        },
        new SkinDef
        {
            id = "red",
            displayName = "Red",
            price = 10,
            type = SkinType.SolidColor,
            primaryColor = new Color(0.85f, 0.15f, 0.15f),
        },
        new SkinDef
        {
            id = "camo",
            displayName = "Green",
            price = 10,
            type = SkinType.Camo,
            primaryColor = new Color(0.30f, 0.40f, 0.20f),
            secondaryColor = new Color(0.20f, 0.28f, 0.12f),
        },
        new SkinDef
        {
            id = "diamond",
            displayName = "Diamond",
            price = 50,
            type = SkinType.PrefabMaterial,
            primaryColor = new Color(0.7f, 0.85f, 1f), // swatch color for card
            materialPrefabPath = "Gems/DiamondGem",
        },
    };

    public static SkinDef[] Catalog => catalog;

    public static event System.Action OnSkinChanged;

    // ─── Balance (shared with WearableManager) ──────────────────────────

    public static long Balance => WearableManager.Balance;

    static void Spend(long amount)
    {
        long spent = long.Parse(PlayerPrefs.GetString("TotalPointsSpent", "0"));
        spent += amount;
        PlayerPrefs.SetString("TotalPointsSpent", spent.ToString());
        PlayerPrefs.Save();
    }

    // ─── Ownership ──────────────────────────────────────────────────────

    public static bool IsOwned(string skinId)
    {
        if (skinId == "default") return true;
        return PlayerPrefs.GetInt("Skin_Owned_" + skinId, 0) == 1;
    }

    public static bool Purchase(string skinId)
    {
        var def = GetDef(skinId);
        if (def == null || IsOwned(skinId)) return false;
        if (Balance < def.Value.price) return false;
        Spend(def.Value.price);
        PlayerPrefs.SetInt("Skin_Owned_" + skinId, 1);
        PlayerPrefs.Save();
        Equip(skinId);
        return true;
    }

    // ─── Equipment ──────────────────────────────────────────────────────

    public static string EquippedId
    {
        get => PlayerPrefs.GetString("Skin_Equipped", "default");
    }

    public static bool IsEquipped(string skinId)
    {
        return EquippedId == skinId;
    }

    public static void Equip(string skinId)
    {
        if (!IsOwned(skinId)) return;
        PlayerPrefs.SetString("Skin_Equipped", skinId);
        PlayerPrefs.Save();
        OnSkinChanged?.Invoke();
    }

    // ─── Lookup ─────────────────────────────────────────────────────────

    public static SkinDef? GetDef(string id)
    {
        foreach (var def in catalog)
            if (def.id == id) return def;
        return null;
    }

    public static SkinDef? GetEquippedDef()
    {
        return GetDef(EquippedId);
    }

    // ─── Apply to a GameObject ──────────────────────────────────────────

    /// <summary>Applies the currently equipped skin to catchy's renderers.</summary>
    public static void ApplyEquippedSkin(GameObject catchy)
    {
        var def = GetDef(EquippedId);
        if (def == null || def.Value.id == "default") return;
        ApplySkin(catchy, def.Value);
    }

    /// <summary>Applies a specific skin to catchy's renderers.</summary>
    public static void ApplySkin(GameObject catchy, SkinDef skin)
    {
        if (skin.id == "default") return;

        Material prefabMat = null;
        if (skin.type == SkinType.PrefabMaterial && !string.IsNullOrEmpty(skin.materialPrefabPath))
        {
            var prefab = Resources.Load<GameObject>(skin.materialPrefabPath);
            if (prefab != null)
            {
                var rend = prefab.GetComponentInChildren<Renderer>();
                if (rend != null && rend.sharedMaterial != null)
                {
                    // Create an instance so we can zero out environment light
                    // to keep the look consistent across all levels
                    prefabMat = new Material(rend.sharedMaterial);
                    if (prefabMat.HasProperty("_EnvironmentLight"))
                    {
                        prefabMat.SetFloat("_EnvironmentLight", 0f);
                        // Boost emission to compensate for removed env light
                        float currentEmission = prefabMat.HasProperty("_Emission") ? prefabMat.GetFloat("_Emission") : 0f;
                        prefabMat.SetFloat("_Emission", Mathf.Max(currentEmission, 1.8f));
                    }
                }
            }
        }

        foreach (var rend in catchy.GetComponentsInChildren<Renderer>())
        {
            if (rend == null || rend.gameObject == null) continue;
            string partName = rend.gameObject.name;
            if (partName.Contains("Eye") || partName.Contains("Smile") || partName.Contains("Mouth")
                || partName.Contains("Happy") || partName.Contains("Tear") || partName.Contains("Sad")
                || partName.Contains("Sunglass") || partName.Contains("Eyepatch") || partName.Contains("Strap")
                || IsUnderWearable(rend.transform))
                continue;

            if (skin.type == SkinType.PrefabMaterial && prefabMat != null)
            {
                Material[] mats = new Material[rend.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = prefabMat;
                rend.sharedMaterials = mats;
            }
            else
            {
                Material[] mats = rend.materials; // single access creates instances
                foreach (var mat in mats)
                {
                    if (mat == null) continue;
                    if (skin.type == SkinType.Camo)
                    {
                        int hash = rend.GetHashCode();
                        mat.color = (hash % 2 == 0) ? skin.primaryColor : skin.secondaryColor;
                    }
                    else
                    {
                        mat.color = skin.primaryColor;
                    }
                }
                rend.materials = mats;
            }
        }
    }

    /// <summary>Returns true if the transform or any ancestor is a wearable instance (named Wearable_*).</summary>
    private static bool IsUnderWearable(Transform t)
    {
        while (t != null)
        {
            if (t.gameObject.name.StartsWith("Wearable_")) return true;
            t = t.parent;
        }
        return false;
    }

    /// <summary>Reset all skin purchases (testing only).</summary>
    public static void ResetAll()
    {
        foreach (var def in catalog)
        {
            PlayerPrefs.DeleteKey("Skin_Owned_" + def.id);
        }
        PlayerPrefs.SetString("Skin_Equipped", "default");
        PlayerPrefs.Save();
        OnSkinChanged?.Invoke();
    }
}
