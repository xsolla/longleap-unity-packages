using System;

namespace LongLeap.UI
{
    /// <summary>What is playing right now — for screens that show the current track.</summary>
    public interface IMusicInfo
    {
        /// <summary>Some music is playing (muted music still counts).</summary>
        bool HasMusic { get; }

        /// <summary>Display name of the current track; empty when there is no music.</summary>
        string CurrentTrackName { get; }

        /// <summary>The track changed, stopped or started.</summary>
        event Action Changed;
    }
}
