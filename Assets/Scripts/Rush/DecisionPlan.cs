using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Describes the *intent* behind a wave — what movement decision the player
/// must make — before any rocks or gems are placed.
///
/// The generation pipeline is:
///   DecisionType → RoutePlan → Hazards around routes → Gems on/near routes → Validate → Score
/// </summary>
public class DecisionPlan
{
    // ------------------------------------------------------------------
    // Decision types (composable building blocks)
    // ------------------------------------------------------------------

    public enum DecisionType
    {
        /// <summary>Two survivable routes split and later reconnect.</summary>
        ForkAndMerge,

        /// <summary>Wide safe route vs narrow gem-rich route.</summary>
        RiskLane,

        /// <summary>Player gradually moves: e.g. Left → Center → Right.</summary>
        Chicane,

        /// <summary>Safe area narrows then widens again.</summary>
        Funnel,

        /// <summary>Safest route is straight, but gems require briefly leaving it.</summary>
        GemDetour,

        /// <summary>Two routes initially possible, but after a row switching is blocked.</summary>
        CommitmentGate,

        /// <summary>Comfortable for several rows, then forces movement.</summary>
        FalseComfort,

        /// <summary>Single corridor that shifts across the grid.</summary>
        MovingCorridor,

        /// <summary>Easy breathing room — minimal hazards.</summary>
        Recovery,
    }

    /// <summary>The high-level decision the player faces.</summary>
    public DecisionType type;

    /// <summary>Generated routes (safe, risky, detour, etc.).</summary>
    public RoutePlan routes;

    /// <summary>How many rows this decision spans.</summary>
    public int rowCount;

    /// <summary>Difficulty budget: fraction (0–1) of player movement capability required.</summary>
    public float movementDemand;

    /// <summary>Seed used to generate this plan (for reproducibility).</summary>
    public int seed;

    /// <summary>Wave interest score (set after scoring).</summary>
    public float interestScore;

    /// <summary>Reason this candidate was rejected, if any.</summary>
    public string rejectionReason;
}

/// <summary>
/// Defines one or more column-paths through a wave. Routes are sequences of
/// column indices, one per row.
/// </summary>
public class RoutePlan
{
    /// <summary>A single column-path through the wave.</summary>
    public class Route
    {
        /// <summary>Descriptive label: "safe", "risky", "detour".</summary>
        public string label;

        /// <summary>Column index per row (length == wave row count).</summary>
        public int[] columns;

        /// <summary>Width in columns (1 = single column, 2 = two adjacent, etc.).</summary>
        public int width = 1;

        /// <summary>Gem reward multiplier (0 = no gems, 1 = normal, 2 = rich).</summary>
        public float rewardLevel;

        /// <summary>Whether this is the primary safe path.</summary>
        public bool isSafe;
    }

    /// <summary>All routes in this plan. At least one must be safe.</summary>
    public List<Route> routes = new List<Route>();

    /// <summary>Row where routes diverge (-1 if single path).</summary>
    public int splitRow = -1;

    /// <summary>Row where routes converge (-1 if they never merge).</summary>
    public int mergeRow = -1;

    /// <summary>Get the primary safe route.</summary>
    public Route SafeRoute
    {
        get
        {
            for (int i = 0; i < routes.Count; i++)
                if (routes[i].isSafe) return routes[i];
            return routes.Count > 0 ? routes[0] : null;
        }
    }
}
