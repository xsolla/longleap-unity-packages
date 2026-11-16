using LongLeap.Core.Ads;

namespace LongLeap.Null
{
    /// <summary>
    /// Portal locale for builds without a portal: there is no portal language,
    /// so the regular language setting from the saves applies.
    /// </summary>
    public sealed class NullPortalLocale : IPortalLocale
    {
        public bool TryGetLocale(out string twoLetterCode)
        {
            twoLetterCode = null;

            return false;
        }
    }
}
