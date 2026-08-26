using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates waves using a decision-first pipeline:
///   1. Pick a <see cref="DecisionPlan.DecisionType"/>
///   2. Generate safe/risky routes as column sequences
///   3. Build hazards around routes
///   4. Place gems contextually (guide, reward, detour)
///   5. Validate survivability
///   6. Score interestingness
///   7. Spawn best candidate
/// </summary>
public static class WaveGeneratorV2
{
    const int CandidateCount = 8;
    const int MaxAttemptsPerCandidate = 3;

    /// <summary>Result of wave generation, including debug info.</summary>
    public class GenerateResult
    {
        public WaveDefinition wave;
        public DecisionPlan plan;
        public float interestScore;
        public WaveInterestScore.Breakdown scoreBreakdown;
        public int candidatesGenerated;
        public int candidatesRejected;
    }

    /// <summary>Generate the best wave from multiple candidates.</summary>
    public static GenerateResult Generate(
        RushConfig config,
        RushConfig.DifficultyTier tier,
        float areaLeft,
        float areaRight,
        WaveMemory memory,
        WaveDefinition.Row previousLastRow,
        int runSeed,
        int waveIndex)
    {
        var result = new GenerateResult();
        var validCandidates = new List<(WaveDefinition wave, DecisionPlan plan, float score, WaveInterestScore.Breakdown bd)>();

        float targetDemand = MovementBudget.TargetDemand(tier.complexPatternWeight);

        for (int c = 0; c < CandidateCount; c++)
        {
            int waveSeed = HashSeed(runSeed, waveIndex, c);

            for (int attempt = 0; attempt < MaxAttemptsPerCandidate; attempt++)
            {
                result.candidatesGenerated++;

                // Use seeded random for reproducibility.
                var oldState = Random.state;
                Random.InitState(HashSeed(waveSeed, attempt));

                var plan = GenerateDecisionPlan(config, tier, targetDemand);
                plan.seed = waveSeed;

                var wave = BuildWaveFromPlan(plan, config, tier);

                Random.state = oldState;

                // Validate.
                if (!SafePathValidator.Validate(wave, config, areaLeft, areaRight))
                {
                    result.candidatesRejected++;
                    continue;
                }

                // Cross-wave validation.
                if (previousLastRow != null && wave.rows.Count > 0)
                {
                    var firstRow = wave.rows[0];
                    float dist = ClosestDistance(
                        previousLastRow.safeMinX, previousLastRow.safeMaxX,
                        firstRow.safeMinX, firstRow.safeMaxX);
                    float travelBudget = config.playerMoveSpeed *
                        (config.wavePause / Mathf.Max(tier.fallSpeed, 0.1f) + config.reactionTimeBuffer);
                    if (dist > travelBudget)
                    {
                        result.candidatesRejected++;
                        continue;
                    }
                }

                // Score.
                WaveInterestScore.Breakdown bd;
                float score = WaveInterestScore.Score(wave, plan, config, tier, memory?.Records, out bd);

                validCandidates.Add((wave, plan, score, bd));
                break; // Got a valid candidate for this slot.
            }
        }

        // Pick from top candidates (slight randomness among top 3).
        if (validCandidates.Count > 0)
        {
            validCandidates.Sort((a, b) => b.score.CompareTo(a.score));

            int pickRange = Mathf.Min(3, validCandidates.Count);
            int pick = Random.Range(0, pickRange);

            var chosen = validCandidates[pick];
            result.wave = chosen.wave;
            result.plan = chosen.plan;
            result.interestScore = chosen.score;
            result.scoreBreakdown = chosen.bd;

            // Record in memory.
            if (memory != null)
            {
                memory.Record(WaveMemory.BuildRecord(chosen.wave, chosen.plan, chosen.score));
            }
        }
        else
        {
            // Fallback: Recovery wave.
            var plan = new DecisionPlan { type = DecisionPlan.DecisionType.Recovery, rowCount = 2 };
            result.wave = BuildRecoveryWave(config, tier);
            result.plan = plan;

            if (config.logValidation)
                Debug.LogWarning("[WaveGenV2] All candidates rejected, using Recovery fallback");
        }

        return result;
    }

    // ------------------------------------------------------------------
    // Phase 1: Decision Plan Generation
    // ------------------------------------------------------------------

    static DecisionPlan GenerateDecisionPlan(RushConfig config, RushConfig.DifficultyTier tier, float targetDemand)
    {
        var plan = new DecisionPlan();
        plan.movementDemand = targetDemand;

        plan.type = PickDecisionType(tier);
        plan.rowCount = Mathf.Clamp(Random.Range(3, tier.maxRows + 1), 3, tier.maxRows);

        // Generate routes based on decision type.
        plan.routes = GenerateRoutes(plan, config, tier);

        return plan;
    }

    static DecisionPlan.DecisionType PickDecisionType(RushConfig.DifficultyTier tier)
    {
        float cw = tier.complexPatternWeight;

        var options = new List<(DecisionPlan.DecisionType dt, float w)>
        {
            (DecisionPlan.DecisionType.MovingCorridor,  1.0f),
            (DecisionPlan.DecisionType.Chicane,         0.8f),
            (DecisionPlan.DecisionType.Funnel,          0.6f + cw * 0.4f),
            (DecisionPlan.DecisionType.FalseComfort,    0.5f),
            (DecisionPlan.DecisionType.ForkAndMerge,    cw * 1.5f),
            (DecisionPlan.DecisionType.RiskLane,        cw * 1.2f),
            (DecisionPlan.DecisionType.GemDetour,       cw * 1.0f),
            (DecisionPlan.DecisionType.CommitmentGate,  cw * 0.8f),
            (DecisionPlan.DecisionType.Recovery,        0.25f),
        };

        return WeightedPick(options);
    }

    // ------------------------------------------------------------------
    // Phase 2: Route Generation (paths before hazards)
    // ------------------------------------------------------------------

    static RoutePlan GenerateRoutes(DecisionPlan plan, RushConfig config, RushConfig.DifficultyTier tier)
    {
        switch (plan.type)
        {
            case DecisionPlan.DecisionType.ForkAndMerge:    return GenerateForkAndMerge(plan, tier);
            case DecisionPlan.DecisionType.RiskLane:        return GenerateRiskLane(plan, tier);
            case DecisionPlan.DecisionType.Chicane:         return GenerateChicane(plan, tier);
            case DecisionPlan.DecisionType.Funnel:          return GenerateFunnel(plan, tier);
            case DecisionPlan.DecisionType.GemDetour:       return GenerateGemDetour(plan, tier);
            case DecisionPlan.DecisionType.CommitmentGate:  return GenerateCommitmentGate(plan, tier);
            case DecisionPlan.DecisionType.FalseComfort:    return GenerateFalseComfort(plan, tier);
            case DecisionPlan.DecisionType.MovingCorridor:  return GenerateMovingCorridor(plan, tier);
            case DecisionPlan.DecisionType.Recovery:        return GenerateRecovery(plan);
            default:                                        return GenerateRecovery(plan);
        }
    }

    static RoutePlan GenerateForkAndMerge(DecisionPlan plan, RushConfig.DifficultyTier tier)
    {
        var rp = new RoutePlan();
        int rows = plan.rowCount;

        // Pick two distinct sides for the fork.
        int leftCol = 0;
        int rightCol = RushColumns.Count - 1;
        int mergeCol = RushColumns.Count / 2; // Center-ish

        // Safe route: wider, stays on one side.
        var safeRoute = new RoutePlan.Route
        {
            label = "safe",
            isSafe = true,
            rewardLevel = 0.3f,
            width = SafeWidth(tier),
            columns = new int[rows],
        };

        // Risky route: narrower, more gems.
        var riskyRoute = new RoutePlan.Route
        {
            label = "risky",
            isSafe = false,
            rewardLevel = 2.0f,
            width = 1,
            columns = new int[rows],
        };

        // Row 0: both start from center area.
        int splitRow = 1;
        int mergeRow = Mathf.Max(rows - 1, splitRow + 1);
        rp.splitRow = splitRow;
        rp.mergeRow = mergeRow;

        bool safeOnLeft = Random.value < 0.5f;

        for (int r = 0; r < rows; r++)
        {
            if (r < splitRow)
            {
                // Before split: both at center.
                safeRoute.columns[r] = mergeCol;
                riskyRoute.columns[r] = mergeCol;
            }
            else if (r >= mergeRow)
            {
                // Merge back to center.
                safeRoute.columns[r] = mergeCol;
                riskyRoute.columns[r] = mergeCol;
            }
            else
            {
                // Split: safe on one side, risky on other.
                safeRoute.columns[r] = safeOnLeft ? leftCol : rightCol;
                riskyRoute.columns[r] = safeOnLeft ? rightCol : leftCol;
            }
        }

        rp.routes.Add(safeRoute);
        rp.routes.Add(riskyRoute);
        return rp;
    }

    static RoutePlan GenerateRiskLane(DecisionPlan plan, RushConfig.DifficultyTier tier)
    {
        var rp = new RoutePlan();
        int rows = plan.rowCount;

        // Safe route: wide, few gems.
        int safeSide = Random.value < 0.5f ? 0 : RushColumns.Count - 1;
        int riskySide = RushColumns.Count - 1 - safeSide;

        var safeRoute = new RoutePlan.Route
        {
            label = "safe",
            isSafe = true,
            rewardLevel = 0.2f,
            width = SafeWidth(tier),
            columns = new int[rows],
        };

        var riskyRoute = new RoutePlan.Route
        {
            label = "risky",
            isSafe = false,
            rewardLevel = 2.5f,
            width = 1,
            columns = new int[rows],
        };

        for (int r = 0; r < rows; r++)
        {
            safeRoute.columns[r] = safeSide;
            riskyRoute.columns[r] = riskySide;
        }

        rp.routes.Add(safeRoute);
        rp.routes.Add(riskyRoute);
        return rp;
    }

    static RoutePlan GenerateChicane(DecisionPlan plan, RushConfig.DifficultyTier tier)
    {
        var rp = new RoutePlan();
        int rows = plan.rowCount;

        var route = new RoutePlan.Route
        {
            label = "safe",
            isSafe = true,
            rewardLevel = 1.0f,
            width = SafeWidth(tier),
            columns = new int[rows],
        };

        // Gradual movement: e.g. 0 → 1 → 2 → 3 → 4, or reverse.
        bool goRight = Random.value < 0.5f;
        int col = goRight ? 0 : RushColumns.Count - 1;
        int step = goRight ? 1 : -1;

        for (int r = 0; r < rows; r++)
        {
            route.columns[r] = col;
            col += step;
            col = Mathf.Clamp(col, 0, RushColumns.Count - 1);
        }

        rp.routes.Add(route);
        return rp;
    }

    static RoutePlan GenerateFunnel(DecisionPlan plan, RushConfig.DifficultyTier tier)
    {
        var rp = new RoutePlan();
        int rows = plan.rowCount;

        var route = new RoutePlan.Route
        {
            label = "safe",
            isSafe = true,
            rewardLevel = 1.0f,
            columns = new int[rows],
        };

        // Start wide center, narrow to 1, widen back.
        int midRow = rows / 2;
        for (int r = 0; r < rows; r++)
        {
            route.columns[r] = RushColumns.Count / 2; // Center column.
            // Width changes: wide → narrow → wide.
            float progress = Mathf.Abs(r - midRow) / (float)Mathf.Max(midRow, 1);
            route.width = Mathf.RoundToInt(Mathf.Lerp(1, SafeWidth(tier), progress));
            route.width = Mathf.Clamp(route.width, 1, 3);
        }

        rp.routes.Add(route);
        return rp;
    }

    static RoutePlan GenerateGemDetour(DecisionPlan plan, RushConfig.DifficultyTier tier)
    {
        var rp = new RoutePlan();
        int rows = plan.rowCount;

        int straightCol = Random.Range(1, RushColumns.Count - 1); // Center-ish.

        var safeRoute = new RoutePlan.Route
        {
            label = "safe",
            isSafe = true,
            rewardLevel = 0.2f,
            width = SafeWidth(tier),
            columns = new int[rows],
        };

        var detourRoute = new RoutePlan.Route
        {
            label = "detour",
            isSafe = false,
            rewardLevel = 3.0f,
            width = 1,
            columns = new int[rows],
        };

        // Safe route is straight.
        // Detour veers away for 2 rows then returns.
        int detourCol = straightCol <= 2 ? RushColumns.Count - 1 : 0;
        int detourStart = Mathf.Max(1, rows / 3);
        int detourEnd = Mathf.Min(rows - 1, detourStart + 2);

        for (int r = 0; r < rows; r++)
        {
            safeRoute.columns[r] = straightCol;
            detourRoute.columns[r] = (r >= detourStart && r <= detourEnd) ? detourCol : straightCol;
        }

        rp.routes.Add(safeRoute);
        rp.routes.Add(detourRoute);
        return rp;
    }

    static RoutePlan GenerateCommitmentGate(DecisionPlan plan, RushConfig.DifficultyTier tier)
    {
        var rp = new RoutePlan();
        int rows = plan.rowCount;

        int leftCol = 0;
        int rightCol = RushColumns.Count - 1;

        // Both start accessible, gate closes mid-wave.
        int gateRow = Mathf.Max(2, rows / 2);
        rp.splitRow = gateRow;

        var leftRoute = new RoutePlan.Route
        {
            label = "left",
            isSafe = true,
            rewardLevel = 1.0f,
            width = SafeWidth(tier),
            columns = new int[rows],
        };

        var rightRoute = new RoutePlan.Route
        {
            label = "right",
            isSafe = true,
            rewardLevel = 1.0f,
            width = SafeWidth(tier),
            columns = new int[rows],
        };

        for (int r = 0; r < rows; r++)
        {
            leftRoute.columns[r] = leftCol;
            rightRoute.columns[r] = rightCol;
        }

        rp.routes.Add(leftRoute);
        rp.routes.Add(rightRoute);
        return rp;
    }

    static RoutePlan GenerateFalseComfort(DecisionPlan plan, RushConfig.DifficultyTier tier)
    {
        var rp = new RoutePlan();
        int rows = Mathf.Max(plan.rowCount, 4);
        plan.rowCount = rows;

        var route = new RoutePlan.Route
        {
            label = "safe",
            isSafe = true,
            rewardLevel = 1.0f,
            width = SafeWidth(tier),
            columns = new int[rows],
        };

        // Stay on one side for comfort rows, then force move.
        int comfortCol = Random.Range(0, RushColumns.Count);
        int forceCol = (comfortCol <= 1) ? Random.Range(RushColumns.Count - 2, RushColumns.Count) : Random.Range(0, 2);
        int switchRow = rows - 2; // Last 2 rows force movement.

        for (int r = 0; r < rows; r++)
        {
            route.columns[r] = r < switchRow ? comfortCol : forceCol;
        }

        rp.routes.Add(route);
        return rp;
    }

    static RoutePlan GenerateMovingCorridor(DecisionPlan plan, RushConfig.DifficultyTier tier)
    {
        var rp = new RoutePlan();
        int rows = plan.rowCount;

        var route = new RoutePlan.Route
        {
            label = "safe",
            isSafe = true,
            rewardLevel = 1.0f,
            width = SafeWidth(tier),
            columns = new int[rows],
        };

        int col = Random.Range(0, RushColumns.Count);
        int dir = Random.value < 0.5f ? 1 : -1;

        for (int r = 0; r < rows; r++)
        {
            route.columns[r] = col;
            col += dir;
            if (col <= 0 || col >= RushColumns.Count - 1) dir = -dir;
            col = Mathf.Clamp(col, 0, RushColumns.Count - 1);
        }

        rp.routes.Add(route);
        return rp;
    }

    static RoutePlan GenerateRecovery(DecisionPlan plan)
    {
        var rp = new RoutePlan();
        plan.rowCount = 2;

        var route = new RoutePlan.Route
        {
            label = "safe",
            isSafe = true,
            rewardLevel = 1.5f,
            width = RushColumns.Count,
            columns = new int[] { RushColumns.Count / 2, RushColumns.Count / 2 },
        };
        rp.routes.Add(route);
        return rp;
    }

    // ------------------------------------------------------------------
    // Phase 3: Build Wave from Plan (hazards around routes, contextual gems)
    // ------------------------------------------------------------------

    static WaveDefinition BuildWaveFromPlan(DecisionPlan plan, RushConfig config, RushConfig.DifficultyTier tier)
    {
        var wave = new WaveDefinition { archetypeName = plan.type.ToString() };
        int rows = plan.rowCount;
        float rw = config.rockSize.worldWidth;

        var safeRoute = plan.routes.SafeRoute;

        for (int r = 0; r < rows; r++)
        {
            var row = new WaveDefinition.Row { yOffset = r * config.rowSpacing };

            // Determine safe columns for this row from all routes.
            bool[] safe = new bool[RushColumns.Count];
            foreach (var route in plan.routes.routes)
            {
                if (r < route.columns.Length)
                {
                    int center = route.columns[r];
                    int halfW = (route.width - 1) / 2;
                    for (int c = center - halfW; c <= center + halfW; c++)
                    {
                        if (c >= 0 && c < RushColumns.Count)
                            safe[c] = true;
                    }
                }
            }

            // CommitmentGate: after gate row, block crossing between routes.
            if (plan.type == DecisionPlan.DecisionType.CommitmentGate &&
                plan.routes.splitRow >= 0 && r >= plan.routes.splitRow)
            {
                // Center column becomes rock (the gate).
                int gateCol = RushColumns.Count / 2;
                safe[gateCol] = false;
            }

            // Funnel: dynamic width per row.
            if (plan.type == DecisionPlan.DecisionType.Funnel && safeRoute != null)
            {
                int midRow = rows / 2;
                float progress = Mathf.Abs(r - midRow) / (float)Mathf.Max(midRow, 1);
                int width = Mathf.RoundToInt(Mathf.Lerp(1, SafeWidth(tier), progress));
                width = Mathf.Clamp(width, 1, RushColumns.Count);

                // Clear and reset safe columns to match funnel width.
                for (int c = 0; c < RushColumns.Count; c++) safe[c] = false;
                int center = safeRoute.columns[r];
                int halfW = (width - 1) / 2;
                for (int c = center - halfW; c <= center + halfW; c++)
                    if (c >= 0 && c < RushColumns.Count) safe[c] = true;
            }

            // Ensure at least one column is safe.
            bool anySafe = false;
            for (int c = 0; c < RushColumns.Count; c++)
                if (safe[c]) { anySafe = true; break; }
            if (!anySafe) safe[RushColumns.Count / 2] = true; // Fallback center.

            // Place rocks in unsafe columns.
            float minSafe = float.MaxValue;
            float maxSafe = float.MinValue;

            for (int c = 0; c < RushColumns.Count; c++)
            {
                float x = RushColumns.GetColumnX(c);
                if (safe[c])
                {
                    if (x < minSafe) minSafe = x;
                    if (x > maxSafe) maxSafe = x;
                }
                else
                {
                    row.slots.Add(WaveDefinition.Slot.Rock(x, rw, 0));
                }
            }

            row.safeMinX = minSafe;
            row.safeMaxX = maxSafe;
            wave.rows.Add(row);
        }

        // --- Phase 3: Place gems contextually ---
        PlaceGemsContextually(wave, plan, config, tier);

        return wave;
    }

    // ------------------------------------------------------------------
    // Phase 3 continued: Contextual Gem Placement
    // ------------------------------------------------------------------

    static void PlaceGemsContextually(WaveDefinition wave, DecisionPlan plan, RushConfig config, RushConfig.DifficultyTier tier)
    {
        if (plan.routes == null || plan.routes.routes.Count == 0) return;

        foreach (var route in plan.routes.routes)
        {
            if (route.rewardLevel <= 0.01f) continue;

            // Determine gem count based on reward level.
            int baseGems = Mathf.RoundToInt(route.rewardLevel * 2f);
            int gemCount = Mathf.Clamp(baseGems + Random.Range(-1, 2), 0, route.columns.Length);

            if (gemCount == 0) continue;

            // Pick gem placement pattern.
            PlaceGemsAlongRoute(wave, route, gemCount, config);
        }
    }

    static void PlaceGemsAlongRoute(WaveDefinition wave, RoutePlan.Route route, int count, RushConfig config)
    {
        if (route.columns.Length == 0 || wave.rows.Count == 0) return;

        // Choose a contiguous or spread placement.
        int startRow = Random.Range(0, Mathf.Max(1, route.columns.Length - count + 1));

        for (int i = 0; i < count && startRow + i < wave.rows.Count; i++)
        {
            int r = startRow + i;
            if (r >= route.columns.Length) break;

            int col = route.columns[r];
            float x = RushColumns.GetColumnX(col);
            var row = wave.rows[r];

            // Don't place gem on top of a rock or existing gem.
            bool occupied = false;
            for (int s = 0; s < row.slots.Count; s++)
            {
                if (Mathf.Abs(row.slots[s].x - x) < 0.1f &&
                    (row.slots[s].type == WaveDefinition.SlotType.Hazard ||
                     row.slots[s].type == WaveDefinition.SlotType.Gem))
                {
                    occupied = true;
                    break;
                }
            }
            if (occupied) continue;

            row.slots.Add(WaveDefinition.Slot.GemAt(x));
        }
    }

    // ------------------------------------------------------------------
    // Recovery fallback
    // ------------------------------------------------------------------

    static WaveDefinition BuildRecoveryWave(RushConfig config, RushConfig.DifficultyTier tier)
    {
        var wave = new WaveDefinition { archetypeName = "Recovery" };

        // 2 rows, all columns safe, center gem cluster.
        for (int r = 0; r < 2; r++)
        {
            var row = new WaveDefinition.Row
            {
                yOffset = r * config.rowSpacing,
                safeMinX = RushColumns.GetColumnX(0),
                safeMaxX = RushColumns.GetColumnX(RushColumns.Count - 1),
            };
            row.slots.Add(WaveDefinition.Slot.GemAt(RushColumns.GetColumnX(RushColumns.Count / 2)));
            wave.rows.Add(row);
        }

        return wave;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    static int SafeWidth(RushConfig.DifficultyTier tier)
    {
        int w = Mathf.RoundToInt(tier.safeCorridorFraction * RushColumns.Count);
        return Mathf.Clamp(w, 1, RushColumns.Count - 1);
    }

    static float ClosestDistance(float aMin, float aMax, float bMin, float bMax)
    {
        if (aMax < bMin) return bMin - aMax;
        if (bMax < aMin) return aMin - bMax;
        return 0f;
    }

    static T WeightedPick<T>(List<(T item, float weight)> options)
    {
        float total = 0f;
        foreach (var o in options) total += Mathf.Max(0f, o.weight);
        if (total <= 0f) return options[0].item;

        float roll = Random.Range(0f, total);
        float running = 0f;
        foreach (var o in options)
        {
            running += Mathf.Max(0f, o.weight);
            if (roll <= running) return o.item;
        }
        return options[options.Count - 1].item;
    }

    static int HashSeed(int a, int b, int c = 0)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + a;
            hash = hash * 31 + b;
            hash = hash * 31 + c;
            return hash;
        }
    }
}
