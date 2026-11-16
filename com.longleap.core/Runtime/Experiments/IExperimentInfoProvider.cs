using System.Collections.Generic;

namespace LongLeap.Core
{
    public interface IExperimentInfoProvider
    {
        IReadOnlyList<AbExperimentInfo> ActiveExperiments { get; }
    }
}
