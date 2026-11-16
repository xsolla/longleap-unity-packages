namespace LongLeap.Bootstrap.Loading
{
    /// <summary>Where loading is: the current step and the overall share done.</summary>
    public readonly struct LoadingProgress
    {
        public LoadingProgress(int stepIndex, int stepCount, string stepName, float stepProgress, float value)
        {
            StepIndex = stepIndex;
            StepCount = stepCount;
            StepName = stepName;
            StepProgress = stepProgress;
            Value = value;
        }

        /// <summary>Zero-based index of the current step.</summary>
        public int StepIndex { get; }

        public int StepCount { get; }

        /// <summary>Type name of the current task.</summary>
        public string StepName { get; }

        /// <summary>Progress of the current step, 0..1.</summary>
        public float StepProgress { get; }

        /// <summary>Overall progress, 0..1, steps counted by their weight.</summary>
        public float Value { get; }
    }
}
