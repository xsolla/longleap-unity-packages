using System;
using System.Collections.Generic;

namespace LongLeap.DailyRewards
{
    public sealed class DailyRewardsConfigValidationResult
    {
        public bool IsValid => Errors.Length == 0;
        public string[] Errors { get; }
        public string[] Warnings { get; }

        public DailyRewardsConfigValidationResult(string[] errors, string[] warnings)
        {
            Errors = errors ?? Array.Empty<string>();
            Warnings = warnings ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Calendar config validation. The same rules for every source: the service checks
    /// the config in InitializeAsync, the ScriptableObject in the inspector (OnValidate).
    /// An error breaks the mechanic; a warning is most likely an oversight.
    /// </summary>
    public static class DailyRewardsConfigValidator
    {
        public static DailyRewardsConfigValidationResult Validate(IDailyRewardsConfig config)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            if (config == null)
            {
                errors.Add("config is not set.");
                return new DailyRewardsConfigValidationResult(errors.ToArray(), warnings.ToArray());
            }

            if (!Enum.IsDefined(typeof(DailyRewardCyclePolicy), config.CyclePolicy))
                errors.Add($"unknown CyclePolicy = {(int)config.CyclePolicy}.");

            if (config.GracePeriod <= TimeSpan.Zero)
                errors.Add("GracePeriod ≤ 0 — the streak would reset on every claim.");
            else if (config.GracePeriod < TimeSpan.FromDays(1))
                warnings.Add("GracePeriod is less than a day — the streak may reset even when the player comes back daily.");
            else if (config.GracePeriod.Ticks % TimeSpan.TicksPerDay != 0)
                warnings.Add("GracePeriod is not a multiple of a day — whether the streak survives depends on the claim time.");

            if (config.ResetHourUtc < 0 || config.ResetHourUtc > 23)
                warnings.Add($"ResetHourUtc = {config.ResetHourUtc} is outside 0..23 — it is taken modulo 24.");

            var days = config.Days;
            if (days == null)
                return new DailyRewardsConfigValidationResult(errors.ToArray(), warnings.ToArray());

            for (int i = 0; i < days.Count; i++)
            {
                var day = days[i];
                var label = $"day {i + 1}";

                if (day == null)
                {
                    warnings.Add($"{label} is empty — it will have no rewards.");
                    continue;
                }

                if (day.DayIndex != i)
                    warnings.Add($"{label}: DayIndex = {day.DayIndex}, but the day index is the position in the list ({i}).");

                if (day.Rewards == null || day.Rewards.Count == 0)
                {
                    warnings.Add($"{label} has no rewards.");
                    continue;
                }

                for (int r = 0; r < day.Rewards.Count; r++)
                {
                    var reward = day.Rewards[r];
                    if (reward == null)
                        warnings.Add($"{label}, reward {r + 1}: empty entry.");
                    else if (reward.Key.IsNone && reward.RewardId.IsNone)
                        warnings.Add($"{label}, reward {r + 1}: neither key nor RewardId is set — nothing for the game to grant.");
                    else if (reward.Amount <= 0)
                        warnings.Add($"{label}, reward {r + 1}: amount {reward.Amount} — nothing to grant.");
                }
            }

            return new DailyRewardsConfigValidationResult(errors.ToArray(), warnings.ToArray());
        }
    }
}
