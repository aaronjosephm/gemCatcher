using System;
using UnityEngine;

/// <summary>
/// Activates the authored environment snapshot for the selected level.
/// Gameplay systems, camera, lighting, UI, and the fitted backdrop remain shared.
/// </summary>
[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public sealed class LevelEnvironmentController : MonoBehaviour
{
    [Serializable]
    private struct EnvironmentEntry
    {
        public LevelManager.LevelId level;
        public GameObject root;
    }

    [SerializeField] private EnvironmentEntry[] environments = Array.Empty<EnvironmentEntry>();

    private GameObject underwaterEnvironment;

    public LevelManager.LevelId ActiveLevel { get; private set; }

    void Awake()
    {
        ApplySelectedLevel();
    }

    public void ApplySelectedLevel()
    {
        LevelManager.LevelId selectedLevel = LevelManager.SelectedLevel;
        bool foundSelectedEnvironment = false;

        foreach (EnvironmentEntry environment in environments)
        {
            if (environment.root == null)
            {
                Debug.LogError(
                    $"[{nameof(LevelEnvironmentController)}] A level environment reference is missing.",
                    this);
                continue;
            }

            bool shouldBeActive = environment.level == selectedLevel;
            if (environment.root.activeSelf != shouldBeActive)
            {
                environment.root.SetActive(shouldBeActive);
            }

            foundSelectedEnvironment |= shouldBeActive;
        }

        // The reef uses the shared fitted backdrop plus lightweight procedural ambience.
        if (selectedLevel == LevelManager.LevelId.Underwater)
        {
            if (underwaterEnvironment == null)
            {
                underwaterEnvironment = new GameObject("Underwater Environment");
                underwaterEnvironment.transform.SetParent(transform, false);
                underwaterEnvironment.AddComponent<UnderwaterAmbience>();
            }
            underwaterEnvironment.SetActive(true);
            foundSelectedEnvironment = true;
        }
        else if (underwaterEnvironment != null) underwaterEnvironment.SetActive(false);

        if (!foundSelectedEnvironment)
        {
            Debug.LogError(
                $"[{nameof(LevelEnvironmentController)}] No environment is configured for {selectedLevel}.",
                this);
            return;
        }

        ActiveLevel = selectedLevel;
    }

#if UNITY_EDITOR
    public void ConfigureForEditor(
        GameObject cave,
        GameObject jungle,
        GameObject space,
        GameObject lava)
    {
        environments = new[]
        {
            new EnvironmentEntry { level = LevelManager.LevelId.Cave, root = cave },
            new EnvironmentEntry { level = LevelManager.LevelId.Jungle, root = jungle },
            new EnvironmentEntry { level = LevelManager.LevelId.Space, root = space },
            new EnvironmentEntry { level = LevelManager.LevelId.Lava, root = lava },
        };
    }
#endif
}
