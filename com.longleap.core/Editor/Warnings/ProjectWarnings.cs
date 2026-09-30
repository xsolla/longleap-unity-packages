using System.Collections.Generic;

namespace LongLeap.Core.Editor
{
    public static class ProjectWarnings
    {
        // Поставщиков спрашиваем КАЖДЫЙ раз, без кэша: замечания зависят от
        // отложенного состояния, а оно меняется на каждое нажатие галочки.
        // Кэшировать тут нечего — кэш пришлось бы сбрасывать на каждый щелчок.
        //
        // Вызывать не из перерисовки: поставщики читают ассеты и файлы.
        public static List<ProjectWarning> Collect(IProjectOptionState pendingState)
        {
            List<(ProjectWarning Warning, string Provider)> found =
                ProviderDiscovery.Collect<IProjectWarningProvider, ProjectWarning>(
                    provider => provider.GetWarnings(pendingState));

            var result = new List<ProjectWarning>();

            foreach ((ProjectWarning warning, string _) in found)
                result.Add(warning);

            // Сначала то, что мешает собрать, потом по убыванию серьёзности.
            result.Sort((left, right) =>
            {
                if (left.BlocksBuild != right.BlocksBuild)
                    return left.BlocksBuild ? -1 : 1;

                return right.Severity.CompareTo(left.Severity);
            });

            return result;
        }
    }
}
