using UnityEngine;

/// <summary>
/// Central level/theme management. Tracks which level is selected, which
/// levels are unlocked, and provides difficulty parameters to ObjectPooler.
///
/// Each locked level is unlocked by crossing the finish line that appears at
/// its score threshold in the preceding level. The selected level and unlocks
/// persist in PlayerPrefs so the player returns to their last choice.
/// </summary>
public static class LevelManager
{
    public enum LevelId { Cave, Jungle, Space, Lava }

    public const string GameplaySceneName = "Gameplay";
    public const int FinishLineScore = 100;
    public const int JungleUnlockScore = 100;
    public const int SpaceUnlockScore = 25_000;
    public const int LavaUnlockScore = 50_000;

    [System.Serializable]
    public struct LevelConfig
    {
        public LevelId id;
        public string displayName;
        public string backgroundResource;   // Resources/ path to background texture
        public string backgroundMaterialResource; // Resources/ path to a Material (overrides texture)
        public string midgroundResource;     // Resources/ path to midground texture (null = none)
        public string musicResource;         // Resources/ path to background music
        public string[] extraGemPrefabs;     // Additional gem prefab names (from Resources/Gems/) for this level
        public int unlockScore;              // Score threshold to spawn the finish line (0 = always unlocked)
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
            backgroundResource = "Backgrounds/WaterfallBackground",
            midgroundResource = null,
            musicResource = "Audio/JungleMusic",
            extraGemPrefabs = new[] { "Gems/BlueGem" },
            unlockScore = JungleUnlockScore,
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
            backgroundResource = "Backgrounds/SpaceBackground",
            midgroundResource = null,
            musicResource = "Audio/SpaceMusic",
            extraGemPrefabs = new[] { "Gems/BlueGem" },
            unlockScore = SpaceUnlockScore,
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
            backgroundResource = "Backgrounds/BayLookoutBackground",
            backgroundMaterialResource = null,
            midgroundResource = null,
            musicResource = "Audio/BayLookoutMusic",
            extraGemPrefabs = new[] { "Gems/Magic_Gem_22" },
            unlockScore = LavaUnlockScore,
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
    private const string UnlockProgressionVersionKey = "LevelUnlockProgressionVersion";
    private const int CurrentUnlockProgressionVersion = 1;
    private static bool unlockProgressionInitialized;

    public static LevelConfig[] AllLevels => levels;

    public static LevelId SelectedLevel
    {
        get
        {
            EnsureUnlockProgressionInitialized();
            string saved = PlayerPrefs.GetString(SelectedKey, LevelId.Cave.ToString());

            if (System.Enum.TryParse<LevelId>(saved, out var id)
                && System.Enum.IsDefined(typeof(LevelId), id)
                && IsUnlocked(id))
                return id;

            PlayerPrefs.SetString(SelectedKey, LevelId.Cave.ToString());
            PlayerPrefs.Save();
            return LevelId.Cave;
        }
        set
        {
            EnsureUnlockProgressionInitialized();
            bool valid = System.Enum.IsDefined(typeof(LevelId), value);
            LevelId selected = valid && IsUnlocked(value) ? value : LevelId.Cave;
            PlayerPrefs.SetString(SelectedKey, selected.ToString());
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
    /// A level is unlocked if its unlockScore is 0, or if the player has
    /// crossed the finish line to unlock it (persisted in PlayerPrefs).
    /// </summary>
    public static bool IsUnlocked(LevelId id)
    {
        EnsureUnlockProgressionInitialized();
        var config = GetConfig(id);
        if (config.unlockScore <= 0) return true;
        return PlayerPrefs.GetInt("KeyUnlocked_" + id, 0) == 1;
    }

    /// <summary>
    /// Permanently unlock a level (called when the player crosses the finish line).
    /// </summary>
    public static void UnlockLevel(LevelId id)
    {
        EnsureUnlockProgressionInitialized();
        PlayerPrefs.SetInt("KeyUnlocked_" + id, 1);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Returns the level immediately after the selected level, regardless of
    /// whether it is already unlocked, or null for the final level.
    /// </summary>
    public static LevelId? GetNextLevel()
    {
        int idx = System.Array.FindIndex(levels, l => l.id == SelectedLevel);
        if (idx < 0 || idx >= levels.Length - 1) return null;
        return levels[idx + 1].id;
    }

    /// <summary>
    /// Returns the score at which the finish line appears in every level.
    /// </summary>
    public static int GetFinishLineScore()
    {
        return FinishLineScore;
    }

    /// <summary>
    /// Returns the LevelId of a newly unlocked level that hasn't been notified
    /// yet, or null if nothing new to announce.
    /// </summary>
    public static LevelId? CheckNewUnlock()
    {
        EnsureUnlockProgressionInitialized();
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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        unlockProgressionInitialized = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeUnlockProgression()
    {
        EnsureUnlockProgressionInitialized();
    }

    private static void EnsureUnlockProgressionInitialized()
    {
        if (unlockProgressionInitialized) return;
        unlockProgressionInitialized = true;

        int savedVersion = PlayerPrefs.GetInt(UnlockProgressionVersionKey, 0);
        if (savedVersion >= CurrentUnlockProgressionVersion) return;

        // Early pre-release builds used the same 100-point threshold for every
        // level. Reset only that obsolete unlock state so installed test builds
        // start the current progression with Crystal Cave while preserving
        // scores, points, cosmetics, settings, and purchase entitlements.
        foreach (var level in levels)
        {
            if (level.unlockScore <= 0) continue;
            PlayerPrefs.DeleteKey("KeyUnlocked_" + level.id);
            PlayerPrefs.DeleteKey(UnlockNotifiedKey + level.id);
        }

        PlayerPrefs.SetString(SelectedKey, LevelId.Cave.ToString());
        PlayerPrefs.SetInt(UnlockProgressionVersionKey, CurrentUnlockProgressionVersion);
        PlayerPrefs.Save();
    }
}
