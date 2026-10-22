namespace LongLeap.UI
{
    /// <summary>The UI's own sounds. The game decides which clip each one is.</summary>
    public enum UiSound
    {
        /// <summary>Any button press.</summary>
        Click,

        /// <summary>Pointer entered a menu button.</summary>
        Hover,

        /// <summary>A daily reward was claimed.</summary>
        RewardClaimed,

        /// <summary>A quest reward flies out of the quest row.</summary>
        RewardFlyout,

        /// <summary>The player poked something that is not available yet (a locked reward day).</summary>
        Denied,
    }

    /// <summary>
    /// How the UI makes sound. The package has no audio of its own: the game implements this
    /// over its audio service and hands it to the windows during loading.
    /// </summary>
    public interface IUiSounds
    {
        void Play(UiSound sound);
    }

    /// <summary>
    /// A button that plays its own sounds: <see cref="MenuClickAudioBinder"/> leaves it silent
    /// (no click on top of the button's own claim / denied sound).
    /// </summary>
    public interface IOwnClickSound
    {
    }
}
