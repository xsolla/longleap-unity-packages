namespace LongLeap.Core.Ads
{
    // The language the portal chose for the player. Used to show the game in
    // that language on the first launch — portals require this.
    public interface IPortalLocale
    {
        // false: there is no portal or the language is unknown; the regular
        // language setting from the saves applies then.
        bool TryGetLocale(out string twoLetterCode);
    }
}
