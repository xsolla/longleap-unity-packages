using System;
using System.Collections.Generic;

namespace LongLeap.Core.Editor
{
    public enum ProjectWarningSeverity
    {
        Info,
        Warning,
        Error
    }

    // Замечание о конфигурации: «прод смотрит на 127.0.0.1», «включён GAMEPIX,
    // а папки плагина нет», «сжатие не то для этого способа загрузки».
    //
    // Поставляется пакетами, а не зашито в окно — иначе окно пришлось бы
    // научить и условиям площадки GamePix, и устройству конфига Xsolla ID.
    //
    // Считается против ОТЛОЖЕННОГО состояния: замечание должно показывать
    // последствие того, что вы собираетесь применить, а не того, что уже
    // применено.
    public sealed class ProjectWarning
    {
        public ProjectWarningSeverity Severity { get; }

        public string Message { get; }

        // Замечания не мешают Apply — только сборке. Запрещать Apply нечего и
        // незачем: настройки как раз может понадобиться применить именно для
        // того, чтобы замечание исчезло.
        public bool BlocksBuild { get; }

        public string FixLabel { get; }

        public Action Fix { get; }

        public ProjectWarning(
            ProjectWarningSeverity severity,
            string message,
            bool blocksBuild = false,
            string fixLabel = null,
            Action fix = null)
        {
            if (string.IsNullOrEmpty(message))
                throw new ArgumentException("A warning must say something.", nameof(message));

            Severity = severity;
            Message = message;
            BlocksBuild = blocksBuild;
            FixLabel = fixLabel;
            Fix = fix;
        }

        public bool HasFix => Fix != null && !string.IsNullOrEmpty(FixLabel);

        public override string ToString() => $"{Severity}: {Message}";
    }

    public interface IProjectWarningProvider
    {
        IEnumerable<ProjectWarning> GetWarnings(IProjectOptionState pendingState);
    }
}
