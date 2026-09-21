using System;
using System.Collections.Generic;

namespace LongLeap.Core.Editor
{
    // Один способ собрать игру.
    //
    // Пакетов, умеющих собирать, может быть несколько, а кнопка «Build» одна:
    // она выбирает способ по текущей конфигурации. Включён GAMEPIX — собирает
    // вендорный билдер площадки, иначе обычный BuildPipeline. Благодаря этому
    // окно НЕ знает ничего про GamePix: знание остаётся в пакете GamePix,
    // который регистрирует свой способ сам.
    public sealed class ProjectBuildTarget
    {
        private readonly Func<IProjectOptionState, bool> _isApplicable;
        private readonly Func<ProjectBuildTargetAvailability> _availability;

        public string Id { get; }

        public string Label { get; }

        public string Description { get; }

        // Побеждает наибольший среди подходящих.
        public int Priority { get; }

        public Action Build { get; }

        public ProjectBuildTarget(
            string id,
            string label,
            int priority,
            Action build,
            Func<IProjectOptionState, bool> isApplicable = null,
            Func<ProjectBuildTargetAvailability> availability = null,
            string description = null)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Build target id must not be empty.", nameof(id));

            Id = id;
            Label = label ?? id;
            Priority = priority;
            Build = build ?? throw new ArgumentNullException(nameof(build));
            _isApplicable = isApplicable;
            _availability = availability;
            Description = description;
        }

        public bool IsApplicable(IProjectOptionState state) => _isApplicable?.Invoke(state) ?? true;

        public ProjectBuildTargetAvailability GetAvailability() =>
            _availability?.Invoke() ?? ProjectBuildTargetAvailability.Available;

        public override string ToString() => $"{Id} ({Label})";
    }

    public readonly struct ProjectBuildTargetAvailability
    {
        public bool IsAvailable { get; }

        public string Reason { get; }

        private ProjectBuildTargetAvailability(bool isAvailable, string reason)
        {
            IsAvailable = isAvailable;
            Reason = reason;
        }

        public static ProjectBuildTargetAvailability Available =>
            new ProjectBuildTargetAvailability(true, null);

        public static ProjectBuildTargetAvailability Blocked(string reason)
        {
            if (string.IsNullOrEmpty(reason))
                throw new ArgumentException("A blocked build target must explain why.", nameof(reason));

            return new ProjectBuildTargetAvailability(false, reason);
        }
    }

    public interface IProjectBuildTargetProvider
    {
        IEnumerable<ProjectBuildTarget> GetBuildTargets();
    }
}
