namespace LongLeap.Tools
{
    /// <summary>
    /// Device profile: where the game runs and how it is controlled. Both axes are folded
    /// into a single value so a config can be picked from it (<c>switch</c>, dictionary,
    /// asset field) without juggling a pair of flags.
    /// </summary>
    public enum DeviceProfile
    {
        /// <summary>Native desktop build (or the editor): keyboard and mouse.</summary>
        Desktop = 0,

        /// <summary>Native mobile build or tablet: touch controls.</summary>
        Mobile = 1,

        /// <summary>Browser on a desktop (WebGL): keyboard and mouse.</summary>
        WebDesktop = 2,

        /// <summary>Browser on a phone or tablet (WebGL): touch controls.</summary>
        WebMobile = 3,
    }
}
