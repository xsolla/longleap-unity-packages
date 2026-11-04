namespace LongLeap.Core.Ads
{
    // Locale stub: there is no portal, so the regular language setting from
    // the saves applies.
    public sealed class NoPortalLocale : IPortalLocale
    {
        public bool TryGetLocale(out string twoLetterCode)
        {
            twoLetterCode = null;

            return false;
        }
    }
}
