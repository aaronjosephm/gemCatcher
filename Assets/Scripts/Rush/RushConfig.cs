using UnityEngine;

/// <summary>
/// Data-driven configuration for Rush Mode. Exposes all tuning variables
/// in the Inspector via a ScriptableObject so designers can tweak rock
/// sizes, spawn weights, difficulty curves, and safety margins without
/// touching code.
///
/// Create via Assets → Create → Gem Catch → Rush Config.
/// </summary>
[CreateAssetMenu(fileName = "RushConfig", menuName = "Gem Catch/Rush Config")]
public class RushConfig : ScriptableObject
{
    // ------------------------------------------------------------------
    // Hazard (Rock) Definitions
    // ------------------------------------------------------------------

    [System.Serializable]
    public class HazardSize
    {
        public string label = "Medium";

        [Tooltip("Uniform scale applied to the rock prefab.")]
        public float scale = 0.4f;

        [Tooltip("SphereCollider radius on the spawned rock.")]
        public float colliderRadius = 0.5f;

        [Tooltip("Width this rock occupies in world units. Used by the " +
                 "wave generator to calculate column coverage.")]
        public float worldWidth = 0.8f;

        [Tooltip("Relative spawn weight. Higher = more frequent.")]
        [Range(0f, 10f)]
        public float spawnWeight = 1f;
    }

    [Header("Rock Size")]
    public HazardSize rockSize = new HazardSize
    {
        label = "Standard", scale = 0.25f, colliderRadius = 0.3f,
        worldWidth = 0.5f, spawnWeight = 1f
    };

    // ------------------------------------------------------------------
    // Wave / Spawn Timing
    // ------------------------------------------------------------------

    [Header("Wave Timing")]
    [Tooltip("Vertical gap (world units) between consecutive rows in a wave.")]
    public float rowSpacing = 3.0f;

    [Tooltip("Vertical gap (world units) between waves. Breathing room.")]
    public float wavePause = 3.0f;

    // ------------------------------------------------------------------
    // Safe-Path Constraints
    // ------------------------------------------------------------------

    [Header("Safety")]
    [Tooltip("Minimum safe corridor width (world units). Must be >= player width + margin.")]
    public float minSafeCorridorWidth = 0f;

    [Tooltip("Player horizontal speed (world units/sec) for reachability checks.")]
    public float playerMoveSpeed = 30f;

    [Tooltip("Extra reaction-time buffer (seconds) for reachability validation.")]
    public float reactionTimeBuffer = 0.2f;

    // ------------------------------------------------------------------
    // Gem Placement
    // ------------------------------------------------------------------

    [Header("Gems")]
    [Range(0f, 1f)]
    [Tooltip("Chance a safe corridor row spawns a gem guiding the player.")]
    public float gemGuideChance = 0.5f;

    [Range(0f, 1f)]
    [Tooltip("Chance a risky corridor places a gem as reward.")]
    public float gemRiskRewardChance = 0.3f;

    [Range(0f, 1f)]
    [Tooltip("Chance a poison gem spawns in a hazard formation (higher tiers only).")]
    public float poisonGemChance = 0f;

    // ------------------------------------------------------------------
    // Difficulty Curve
    // ------------------------------------------------------------------

    [System.Serializable]
    public class DifficultyTier
    {
        public float startTime;
        public float fallSpeed = 3f;

        [Range(1, 6)]
        public int maxRows = 2;

        [Range(0.2f, 1f)]
        [Tooltip("Fraction of screen width for the safe corridor.")]
        public float safeCorridorFraction = 0.6f;

        [Range(0f, 2f)]
        [Tooltip("Weight for complex archetypes (zig-zag, funnel, fork).")]
        public float complexPatternWeight = 0f;

        [Range(0f, 1f)]
        [Tooltip("Chance a poison gem appears in hazard rows at this tier.")]
        public float poisonGemChance = 0f;

        [Tooltip("Wave pause override (world units). 0 = use global wavePause.")]
        public float wavePauseOverride = 0f;
    }

    [Header("Difficulty Progression")]
    public DifficultyTier[] difficultyTiers = new DifficultyTier[]
    {
        new DifficultyTier { startTime = 0f,   fallSpeed = 2.4f, maxRows = 3, safeCorridorFraction = 0.4f,  complexPatternWeight = 0.2f, poisonGemChance = 0f, wavePauseOverride = 3.0f },
        new DifficultyTier { startTime = 30f,  fallSpeed = 2.8f, maxRows = 4, safeCorridorFraction = 0.35f, complexPatternWeight = 0.4f, poisonGemChance = 0f, wavePauseOverride = 2.5f },
        new DifficultyTier { startTime = 55f,  fallSpeed = 3.2f, maxRows = 4, safeCorridorFraction = 0.3f,  complexPatternWeight = 0.6f, poisonGemChance = 0f, wavePauseOverride = 2.0f },
        new DifficultyTier { startTime = 90f,  fallSpeed = 3.6f, maxRows = 5, safeCorridorFraction = 0.25f, complexPatternWeight = 0.8f, poisonGemChance = 0f, wavePauseOverride = 1.5f },
        new DifficultyTier { startTime = 135f, fallSpeed = 4.0f, maxRows = 5, safeCorridorFraction = 0.2f,  complexPatternWeight = 1.0f, poisonGemChance = 0f, wavePauseOverride = 1.2f },
    };

    // ------------------------------------------------------------------
    // Debug
    // ------------------------------------------------------------------

    [Header("Debug")]
    public bool debugVisualization = false;
    public bool logValidation = false;

    [Tooltip("When enabled, logs each candidate's score breakdown.")]
    public bool logCandidateScores = false;

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>Get the difficulty tier for the given elapsed time.</summary>
    public DifficultyTier GetTier(float elapsedTime)
    {
        if (difficultyTiers.Length == 0) return new DifficultyTier();
        if (difficultyTiers.Length == 1) return difficultyTiers[0];

        // Find the two bounding tiers and lerp between them.
        for (int i = difficultyTiers.Length - 1; i >= 0; i--)
        {
            if (elapsedTime >= difficultyTiers[i].startTime)
            {
                if (i == difficultyTiers.Length - 1)
                    return difficultyTiers[i]; // Past final tier — use it as-is.

                DifficultyTier a = difficultyTiers[i];
                DifficultyTier b = difficultyTiers[i + 1];
                float range = b.startTime - a.startTime;
                float t = Mathf.Clamp01((elapsedTime - a.startTime) / range);

                return LerpTier(a, b, t);
            }
        }
        return difficultyTiers[0];
    }

    /// <summary>Linearly interpolate all numeric fields between two tiers.</summary>
    static DifficultyTier LerpTier(DifficultyTier a, DifficultyTier b, float t)
    {
        return new DifficultyTier
        {
            startTime              = Mathf.Lerp(a.startTime, b.startTime, t),
            fallSpeed              = Mathf.Lerp(a.fallSpeed, b.fallSpeed, t),
            maxRows                = Mathf.RoundToInt(Mathf.Lerp(a.maxRows, b.maxRows, t)),
            safeCorridorFraction   = Mathf.Lerp(a.safeCorridorFraction, b.safeCorridorFraction, t),
            complexPatternWeight   = Mathf.Lerp(a.complexPatternWeight, b.complexPatternWeight, t),
            poisonGemChance        = Mathf.Lerp(a.poisonGemChance, b.poisonGemChance, t),
            wavePauseOverride      = Mathf.Lerp(a.wavePauseOverride, b.wavePauseOverride, t),
        };
    }
}
