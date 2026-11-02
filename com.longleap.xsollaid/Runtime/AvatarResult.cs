using UnityEngine;

namespace LongLeap.XsollaId
{
    // Result of loading the profile picture. Same shape as XsollaIdResult:
    // private constructor and nested sealed subclasses, matched with
    // `result is AvatarResult.Success ok`.
    //
    // IXsollaIdAvatarLoader NEVER lets exceptions out. The UI polls the load,
    // and an exception stuck there means an empty spot on screen with nothing
    // in the log.
    public abstract class AvatarResult
    {
        private AvatarResult() { }

        public sealed class Success : AvatarResult
        {
            // The sprite is SHARED: one url, one instance for the lifetime of
            // the page. Do not Destroy or modify it, see
            // IXsollaIdAvatarLoader.
            public Sprite Sprite { get; }

            public Success(Sprite sprite) => Sprite = sprite;
        }

        public sealed class Failure : AvatarResult
        {
            public XsollaIdErrorCode Code { get; }

            // For the log, not for the player. Never contains the picture url:
            // it may carry a signed CDN token.
            public string Message { get; }

            public Failure(XsollaIdErrorCode code, string message)
            {
                Code = code;
                Message = message;
            }
        }

        // The account has no picture — this is NOT a failure and not worth a log
        // line. The picture claim is optional, and whether a given Xsolla ID
        // instance returns it is not known in advance.
        public static AvatarResult NoPicture() =>
            new Failure(XsollaIdErrorCode.PictureUnavailable, "The account has no usable picture url.");

        public static AvatarResult Unavailable() =>
            new Failure(XsollaIdErrorCode.Unavailable, "Avatar loading is not available in this build.");
    }
}
