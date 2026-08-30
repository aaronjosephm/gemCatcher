using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages the wearable catalog, purchases, and equipment state.
/// Wearables are cosmetic items attached to catchy. Purchased with
/// lifetime points (TotalPoints in PlayerPrefs).
/// </summary>
public static class WearableManager
{
    // ─── Wearable definition ────────────────────────────────────────────

    public enum AttachPoint { Head, Face, Body }

    [System.Serializable]
    public struct WearableDef
    {
        public string id;           // unique key, e.g. "eyepatch"
        public string displayName;  // shown in shop, e.g. "Eye Patch"
        public long price;          // cost in lifetime points
        public string prefabPath;   // Resources/ path to the 3D prefab
        public AttachPoint attach;  // where on catchy it goes
        public Vector3 localOffset; // position offset from attach point
        public Vector3 localRotation; // euler rotation offset
        public float scale;         // uniform scale (1 = default)
    }

    // ─── Catalog ────────────────────────────────────────────────────────

    // Add new wearables here. Prefabs should be in Resources/Wearables/.
    private static readonly WearableDef[] catalog = new WearableDef[]
    {
        new WearableDef
        {
            id = "eyepatch",
            displayName = "Eye Patch",
            price = 1000000,
            prefabPath = "Wearables/EyePatch",
            attach = AttachPoint.Face,
            localOffset = new Vector3(0.15f, 0.1f, 0.3f),
            localRotation = Vector3.zero,
            scale = 0.5f,
        },
        new WearableDef
        {
            id = "tophat",
            displayName = "Top Hat",
            price = 2000000,
            prefabPath = "Wearables/TopHat",
            attach = AttachPoint.Head,
            localOffset = new Vector3(0f, 0.5f, 0f),
            localRotation = Vector3.zero,
            scale = 0.5f,
        },
        new WearableDef
        {
            id = "cape",
            displayName = "Cape",
            price = 5000000,
            prefabPath = "Wearables/Cape",
            attach = AttachPoint.Body,
            localOffset = new Vector3(0f, 0f, -0.3f),
            localRotation = Vector3.zero,
            scale = 0.5f,
        },
    };

    public static WearableDef[] Catalog => catalog;

    // ─── Currency ───────────────────────────────────────────────────────

    /// <summary>Current spendable point balance (lifetime earned minus spent).</summary>
    public static long Balance
    {
        get
        {
            long total = long.Parse(PlayerPrefs.GetString("TotalPoints", "0"));
            long spent = long.Parse(PlayerPrefs.GetString("TotalPointsSpent", "0"));
            return total - spent;
        }
    }

    /// <summary>Spend points. Returns false if insufficient balance.</summary>
    public static bool SpendPoints(long amount)
    {
        if (Balance < amount) return false;
        long spent = long.Parse(PlayerPrefs.GetString("TotalPointsSpent", "0"));
        spent += amount;
        PlayerPrefs.SetString("TotalPointsSpent", spent.ToString());
        PlayerPrefs.Save();
        return true;
    }

    // ─── Purchase state ─────────────────────────────────────────────────

    public static bool IsOwned(string wearableId)
    {
        return PlayerPrefs.GetInt("Wearable_Owned_" + wearableId, 0) == 1;
    }

    /// <summary>Purchase a wearable. Returns true on success.</summary>
    public static bool Purchase(string wearableId)
    {
        if (IsOwned(wearableId)) return false;
        var def = GetDef(wearableId);
        if (def == null) return false;
        if (!SpendPoints(def.Value.price)) return false;

        PlayerPrefs.SetInt("Wearable_Owned_" + wearableId, 1);
        PlayerPrefs.Save();
        OnWearableChanged?.Invoke();
        return true;
    }

    // ─── Equipment state ────────────────────────────────────────────────

    /// <summary>Get the currently equipped wearable ID for a slot, or null.</summary>
    public static string GetEquipped(AttachPoint slot)
    {
        string id = PlayerPrefs.GetString("Wearable_Equipped_" + slot, "");
        return string.IsNullOrEmpty(id) ? null : id;
    }

    /// <summary>Equip a wearable (must be owned). Replaces anything in that slot.</summary>
    public static bool Equip(string wearableId)
    {
        if (!IsOwned(wearableId)) return false;
        var def = GetDef(wearableId);
        if (def == null) return false;

        PlayerPrefs.SetString("Wearable_Equipped_" + def.Value.attach, wearableId);
        PlayerPrefs.Save();
        OnWearableChanged?.Invoke();
        return true;
    }

    /// <summary>Unequip whatever is in the given slot.</summary>
    public static void Unequip(AttachPoint slot)
    {
        PlayerPrefs.SetString("Wearable_Equipped_" + slot, "");
        PlayerPrefs.Save();
        OnWearableChanged?.Invoke();
    }

    /// <summary>Check if a specific wearable is currently equipped.</summary>
    public static bool IsEquipped(string wearableId)
    {
        var def = GetDef(wearableId);
        if (def == null) return false;
        return GetEquipped(def.Value.attach) == wearableId;
    }

    // ─── Events ─────────────────────────────────────────────────────────

    /// <summary>Fired when any wearable is purchased, equipped, or unequipped.</summary>
    public static event System.Action OnWearableChanged;

    // ─── Helpers ────────────────────────────────────────────────────────

    public static WearableDef? GetDef(string id)
    {
        foreach (var def in catalog)
        {
            if (def.id == id) return def;
        }
        return null;
    }
}
