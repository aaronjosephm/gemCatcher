using UnityEngine;

/// <summary>
/// Central level/theme management. Tracks which level is selected, which
/// levels are unlocked, and provides difficulty parameters to ObjectPooler.
///
/// Levels unlock based on best score thresholds. The selected level persists
/// in PlayerPrefs so the player returns to their last choice.
/// </summary>
public static class LevelManager
{
    public enum LevelId { Cave, Jungle, Space, Lava }

    [System.Serializable]
    public struct LevelConfig
    {
        public LevelId id;
        public string displayName;
        public string sceneName;             // Scene to load for this level
        public string backgroundResource;   // Resources/ path to background texture
        public string backgroundMaterialResource; // Resources/ path to a Material (overrides texture)
        public string midgroundResource;     // Resources/ path to midground texture (null = none)
        public string musicResource;         // Resources/ path to background music
        public string[] extraGemPrefabs;     // Additional gem prefab names (from Resources/Gems/) for this level
        public int unlockScore;              // Best score required to unlock (0 = always unlocked)
        public Color cameraColor;            // Camera.backgroundColor for this level

        // Difficulty overrides
        public float initialFallSpeed;
        public float initialSpawnInterval;
        public float bombChance;
        public float goldenChance;
        public float dailyMaxFallSpeed;
        public float dailyMinSpawnInterval;
        public float catcherYOffset;         // Extra downward offset for catcher position (0 = default)
        public float placementDuration;
        public float backgroundWallZ;        // Override for background plane Z (0 = use default 2)      // Seconds the gem blinks before going solid (0 = use default 3s)
    }

    private static readonly LevelConfig[] levels = new[]
    {
        new LevelConfig
        {
            id = LevelId.Cave,
            displayName = "Crystal Cave",
            sceneName = "SampleScene",
            backgroundResource = "Backgrounds/CaveBackground",
            midgroundResource = "Backgrounds/MidgroundCave",
            musicResource = "Audio/BackgroundMusic",
            extraGemPrefabs = null,
            unlockScore = 0,
            cameraColor = new Color(0.05f, 0.06f, 0.12f, 1f),
            initialFallSpeed = 3.0f,
            initialSpawnInterval = 3.0f,
            bombChance = 0.07f,
            goldenChance = 0.05f,
            dailyMaxFallSpeed = 5.5f,
            dailyMinSpawnInterval = 2.0f,
            placementDuration = 4.0f,
        },
        new LevelConfig
        {
            id = LevelId.Jungle,
            displayName = "Jungle Falls",
            sceneName = "JungleFalls",
            backgroundResource = "Backgrounds/WaterfallBackground",
            midgroundResource = null,
            musicResource = "Audio/JungleMusic",
            extraGemPrefabs = new[] { "Gems/BlueGem" },
            unlockScore = 100,
            cameraColor = new Color(0.08f, 0.15f, 0.10f, 1f),
            initialFallSpeed = 4.0f,
            initialSpawnInterval = 2.4f,
            bombChance = 0.12f,
            goldenChance = 0.06f,
            dailyMaxFallSpeed = 7.0f,
            dailyMinSpawnInterval = 1.5f,
            placementDuration = 3.5f,
        },
        new LevelConfig
        {
            id = LevelId.Space,
            displayName = "Deep Space",
            sceneName = "DeepSpace",
            backgroundResource = "Backgrounds/SpaceBackground",
            midgroundResource = null,
            musicResource = "Audio/SpaceMusic",
            extraGemPrefabs = new[] { "Gems/BlueGem" },
            unlockScore = 100,
            cameraColor = new Color(0.01f, 0.02f, 0.06f, 1f),
            initialFallSpeed = 4.5f,
            initialSpawnInterval = 2.0f,
            bombChance = 0.15f,
            goldenChance = 0.07f,
            dailyMaxFallSpeed = 8.0f,
            dailyMinSpawnInterval = 1.2f,
            catcherYOffset = 0f,
            placementDuration = 3.0f,
        },
        new LevelConfig
        {
            id = LevelId.Lava,
            displayName = "Bay Lookout",
            sceneName = "LavaLamp",
            backgroundResource = "Backgrounds/BayLookoutBackground",
            backgroundMaterialResource = null,
            midgroundResource = null,
            musicResource = "Audio/BayLookoutMusic",
            extraGemPrefabs = null,
            unlockScore = 100,
            cameraColor = new Color(0.02f, 0.08f, 0.18f, 1f),
            initialFallSpeed = 5.0f,
            initialSpawnInterval = 1.8f,
            bombChance = 0.18f,
            goldenChance = 0.08f,
            dailyMaxFallSpeed = 9.0f,
            dailyMinSpawnInterval = 1.0f,
            catcherYOffset = 0f,
            placementDuration = 2.5f,
            backgroundWallZ = 500f,
        },
    };

    private const string SelectedKey = "SelectedLevel";
    private const string UnlockNotifiedKey = "LevelUnlockNotified_";

    public static LevelConfig[] AllLevels => levels;

    public static LevelId SelectedLevel
    {
        get
        {
            string saved = PlayerPrefs.GetString(SelectedKey, LevelId.Cave.ToString());
            if (System.Enum.TryParse<LevelId>(saved, out var id)) return id;
            return LevelId.Cave;
        }
        set
        {
            PlayerPrefs.SetString(SelectedKey, value.ToString());
            PlayerPrefs.Save();
        }
    }

    public static LevelConfig GetConfig(LevelId id)
    {
        foreach (var l in levels)
            if (l.id == id) return l;
        return levels[0];
    }

    public static LevelConfig CurrentConfig => GetConfig(SelectedLevel);

    // Set to true during development to bypass unlock requirements.
    private const bool AllLevelsUnlocked = false;

    /// <summary>
    /// Returns the best score achieved on a specific level.
    /// </summary>
    public static int GetLevelBestScore(LevelId id)
    {
        return PlayerPrefs.GetInt("BestScore_" + id, 0);
    }

    /// <summary>
    /// Records a score for the given level. Updates best if higher.
    /// </summary>
    public static void RecordLevelScore(LevelId id, int score)
    {
        int current = GetLevelBestScore(id);
        if (score > current)
        {
            PlayerPrefs.SetInt("BestScore_" + id, score);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// A level is unlocked if its unlockScore is 0, or if the preceding
    /// level's best score meets the threshold.
    /// </summary>
    public static bool IsUnlocked(LevelId id)
    {
        if (AllLevelsUnlocked) return true;
        var config = GetConfig(id);
        if (config.unlockScore <= 0) return true;

        // Find the preceding level's best score.
        int idx = System.Array.FindIndex(levels, l => l.id == id);
        if (idx <= 0) return true; // First level is always unlocked
        LevelId precedingLevel = levels[idx - 1].id;
        return GetLevelBestScore(precedingLevel) >= config.unlockScore;
    }

    /// <summary>
    /// Returns the LevelId of a newly unlocked level that hasn't been notified
    /// yet, or null if nothing new to announce.
    /// </summary>
    public static LevelId? CheckNewUnlock()
    {
        foreach (var l in levels)
        {
            if (l.unlockScore <= 0) continue;
            if (!IsUnlocked(l.id)) continue;
            string key = UnlockNotifiedKey + l.id;
            if (PlayerPrefs.GetInt(key, 0) == 0)
            {
                PlayerPrefs.SetInt(key, 1);
                PlayerPrefs.Save();
                return l.id;
            }
        }
        return null;
    }
}
