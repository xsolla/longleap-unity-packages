namespace LongLeap.Core
{
    public readonly struct AbExperimentInfo
    {
        public string ExperimentId { get; }
        public int VariantIndex { get; }

        public AbExperimentInfo(string experimentId, int variantIndex)
        {
            ExperimentId = experimentId;
            VariantIndex = variantIndex;
        }
    }
}
