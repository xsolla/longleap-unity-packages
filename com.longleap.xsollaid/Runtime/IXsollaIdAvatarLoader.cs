using System.Threading.Tasks;
using UnityEngine;

namespace LongLeap.XsollaId
{
    // Xsolla ID profile picture as a Sprite.
    //
    // Exists for the same reason as UnavailableXsollaIdAuth: to keep the UI
    // free of #if. With XSOLLA_ID off, or on a portal, game code gets a stub
    // that honestly answers Unavailable, and the widget shows its placeholder.
    //
    // LIFETIME. The sprite is SHARED and lives as long as the page: one url,
    // one instance, surviving scene reloads. Therefore the UI:
    //
    //   does NOT call Destroy on the sprite or its texture;
    //   does NOT modify them;
    //   does NOT hold a reference longer than the session itself lives.
    //
    // Nothing needs to be destroyed: one texture per account for the lifetime
    // of the page is a deliberately accepted bound. Destroying on sign-out was
    // considered and rejected: a SignedOut subscriber could run BEFORE the
    // widget removes the sprite from its Image, and a destroyed sprite renders
    // as nothing without a word in the log.
    public interface IXsollaIdAvatarLoader
    {
        // Whether loading is possible at all.
        bool IsAvailable { get; }

        // Never throws: any failure arrives as AvatarResult.Failure.
        //
        // A repeated call with the same url does not download again and returns
        // the same result — including the same failure. A dead url must not be
        // re-requested on every scene reload.
        Task<AvatarResult> LoadAsync(AvatarUrl url);

        // An already loaded sprite, without waiting.
        //
        // Needed because of scene reloads: the widget is recreated each time,
        // and awaiting a Task for a picture that is already in memory would
        // mean an extra frame showing the placeholder.
        bool TryGetCached(AvatarUrl url, out Sprite sprite);
    }
}
