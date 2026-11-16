using System.Threading.Tasks;
using UnityEngine;

namespace LongLeap.XsollaId
{
    // Stub loader for when pictures are unavailable: a portal build or the
    // XSOLLA_ID define turned off.
    //
    // Counterpart of UnavailableXsollaIdAuth, for the same reason.
    public sealed class UnavailableXsollaIdAvatarLoader : IXsollaIdAvatarLoader
    {
        public bool IsAvailable => false;

        public Task<AvatarResult> LoadAsync(AvatarUrl url) => Task.FromResult(AvatarResult.Unavailable());

        public bool TryGetCached(AvatarUrl url, out Sprite sprite)
        {
            sprite = null;
            return false;
        }
    }
}
