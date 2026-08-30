using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Attaches equipped wearable prefabs to catchy. Add this component to
/// the catcher instance. It rebuilds whenever wearables change.
/// </summary>
public class WearableAttachment : MonoBehaviour
{
    private readonly Dictionary<string, GameObject> activeWearables = new Dictionary<string, GameObject>();

    void OnEnable()
    {
        WearableManager.OnWearableChanged += Rebuild;
        Rebuild();
    }

    void OnDisable()
    {
        WearableManager.OnWearableChanged -= Rebuild;
    }

    /// <summary>Rebuild all attached wearables to match current equipped state.</summary>
    public void Rebuild()
    {
        // Remove all current attachments.
        foreach (var kvp in activeWearables)
        {
            if (kvp.Value != null) Destroy(kvp.Value);
        }
        activeWearables.Clear();

        // Attach each equipped wearable.
        foreach (var def in WearableManager.Catalog)
        {
            if (!WearableManager.IsEquipped(def.id)) continue;

            GameObject prefab = Resources.Load<GameObject>(def.prefabPath);
            GameObject instance;
            if (prefab != null)
            {
                instance = Instantiate(prefab, transform);
            }
            else
            {
                instance = ProceduralWearableFactory.Create(def.id);
                if (instance == null)
                {
                    Debug.LogWarning($"[WearableAttachment] No prefab or procedural wearable for: {def.id}");
                    continue;
                }
                instance.transform.SetParent(transform, false);
            }
            instance.name = $"Wearable_{def.id}";
            instance.transform.localPosition = def.localOffset;
            instance.transform.localRotation = Quaternion.Euler(def.localRotation);
            instance.transform.localScale = Vector3.one * def.scale;

            // Ensure it doesn't interfere with gameplay collisions.
            foreach (Collider c in instance.GetComponentsInChildren<Collider>())
                Destroy(c);

            activeWearables[def.id] = instance;
        }
    }
}
