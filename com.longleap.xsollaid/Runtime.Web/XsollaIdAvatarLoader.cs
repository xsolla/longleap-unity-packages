using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace LongLeap.XsollaId.Web
{
    // Result of an image download.
    //
    // Separate from HttpReply on purpose: there the body is a string, while in
    // WebGL the browser's native decoder hands the image back as a ready
    // texture. Routing it through bytes would mean decoding a second time in
    // wasm and pulling ImageConversionModule into the build just for
    // Texture2D.LoadImage.
    //
    // The "transport failed" / "server answered with a status code" split is the
    // same as in HttpReply, for the same reason: in the browser a missing CORS
    // header looks like a ConnectionError with StatusCode 0, not like a server
    // response.
    public readonly struct TextureReply
    {
        public Texture2D Texture { get; }
        public long StatusCode { get; }
        public string TransportError { get; }

        public bool IsTransportError => TransportError != null;

        public bool IsSuccess =>
            !IsTransportError && StatusCode >= 200 && StatusCode < 300 && Texture != null;

        public TextureReply(Texture2D texture, long statusCode, string transportError)
        {
            Texture = texture;
            StatusCode = statusCode;
            TransportError = transportError;
        }

        public static TextureReply Transport(string error) => new TextureReply(null, 0, error);
        public static TextureReply Ok(Texture2D texture) => new TextureReply(texture, 200, null);

        public static TextureReply Status(long code, Texture2D texture = null) =>
            new TextureReply(texture, code, null);
    }

    // Loads the Xsolla ID profile picture.
    //
    // Transport behaviour mirrors XsollaIdHttp so the package has a single way
    // of doing network calls: UnityWebRequest, waiting via
    // `while (!op.isDone) await Task.Yield()` (the only safe option under
    // WebGL, which has no threads), request.timeout = 10, and URLs stripped of
    // query parameters in messages.
    public sealed class XsollaIdAvatarLoader : IXsollaIdAvatarLoader
    {
        private const int TimeoutSeconds = 10;

        // A page should never need more images than this: there is one account,
        // at most replaced by another. Growing past the threshold indicates a
        // leak, and it is better to see that in the console than in the
        // profiler an hour later.
        private const int SaneCacheSize = 8;

        // The cache is STATIC, not per instance: the scene can reload and the
        // bootstrap code that owns the loader is recreated with it, so an
        // instance cache would be lost on every reload. The static reference
        // also keeps the texture alive — Resources.UnloadUnusedAssets, which
        // Unity calls on scene load, treats static fields as roots.
        //
        // We store the task, not the sprite. One mechanism covers three cases:
        // the same image is not downloaded twice when two widgets request it in
        // the same frame (same trick as _signInInFlight in XsollaIdAuth);
        // success is cached; and so is failure — otherwise a dead URL would be
        // retried on every scene reload.
        private static readonly Dictionary<string, Task<AvatarResult>> Cached =
            new Dictionary<string, Task<AvatarResult>>();

        private readonly Func<string, Task<TextureReply>> _download;

        public XsollaIdAvatarLoader(Func<string, Task<TextureReply>> download = null)
        {
            _download = download ?? SendAsync;
        }

        // UnityWebRequestTexture works in the editor and on every platform:
        // there is no popup here, so unlike XsollaIdWebBrowser.IsSupported there
        // is no reason to restrict this to UNITY_WEBGL. This is the only part of
        // sign-in that can be tested without a WebGL build.
        public bool IsAvailable => true;

        public Task<AvatarResult> LoadAsync(AvatarUrl url)
        {
            // No request at all: there is no URL or it failed validation.
            if (!url.IsValid)
                return Task.FromResult(AvatarResult.NoPicture());

            if (Cached.TryGetValue(url.Value, out Task<AvatarResult> existing))
                return existing;

            if (Cached.Count >= SaneCacheSize)
            {
                XsollaIdHttp.LogWarning(
                    $"the avatar cache holds {Cached.Count} entries — that looks like a leak.");
            }

            Task<AvatarResult> task = DownloadAsync(url.Value);
            Cached[url.Value] = task;

            return task;
        }

        public bool TryGetCached(AvatarUrl url, out Sprite sprite)
        {
            sprite = null;

            if (!url.IsValid || !Cached.TryGetValue(url.Value, out Task<AvatarResult> task))
                return false;

            // The task may still be in flight — then there is no sprite yet, and
            // we must not wait: this method is synchronous precisely so that it
            // never waits.
            if (task.Status != TaskStatus.RanToCompletion || !(task.Result is AvatarResult.Success ok))
                return false;

            sprite = ok.Sprite;

            return sprite != null;
        }

        // The only place textures are destroyed. NOT called at runtime — see the
        // lifetime note in IXsollaIdAvatarLoader.
        //
        // Needed by tests: the cache is static and NUnit runs all fixtures in
        // one domain, so without a reset tests would see each other's cache and
        // leave textures behind in the editor.
        public static void Forget()
        {
            foreach (Task<AvatarResult> task in Cached.Values)
            {
                if (task.Status != TaskStatus.RanToCompletion ||
                    !(task.Result is AvatarResult.Success ok) || ok.Sprite == null)
                {
                    continue;
                }

                Texture2D texture = ok.Sprite.texture;

                Object.DestroyImmediate(ok.Sprite);

                if (texture != null)
                    Object.DestroyImmediate(texture);
            }

            Cached.Clear();
        }

        private async Task<AvatarResult> DownloadAsync(string url)
        {
            TextureReply reply;

            try
            {
                reply = await _download(url);
            }
            catch (Exception ex)
            {
                // Never throw: the UI polls this Task.
                return new AvatarResult.Failure(
                    XsollaIdErrorCode.Network, $"avatar download threw — {ex.Message}");
            }

            if (reply.IsTransportError)
            {
                // A missing CORS header on the image host also ends up here: the
                // response never reaches us and StatusCode stays 0. It cannot be
                // told apart from "no network" — same as with the root
                // /oauth2/revoke, see XsollaIdTokenClient.RevokeAsync.
                return new AvatarResult.Failure(XsollaIdErrorCode.Network, reply.TransportError);
            }

            if (!reply.IsSuccess)
            {
                // The server answered, but not with an image: a 403/404 from the
                // CDN, or a 200 with an HTML error page the decoder chokes on.
                return new AvatarResult.Failure(
                    XsollaIdErrorCode.PictureUnavailable,
                    $"avatar request answered {reply.StatusCode} without a usable image.");
            }

            return new AvatarResult.Success(ToSprite(reply.Texture));
        }

        private static Task<TextureReply> SendAsync(string url)
        {
            // In the browser, the browser decodes. The concrete reason: Xsolla ID
            // serves avatars as WEBP, and DownloadHandlerTexture only handles PNG
            // and JPEG — the bytes arrive, but the decoder reports
            // DataProcessingError. Details in XsollaIdWebGLImageDecoder.
            if (XsollaIdWebGLImageDecoder.IsSupported)
                return XsollaIdWebGLImageDecoder.DecodeAsync(url, TimeoutSeconds);

            // There is no browser in the editor. UnityWebRequestTexture handles
            // PNG and JPEG, which is enough to test loading without a WebGL
            // build.
            return SendViaUnityWebRequestAsync(url);
        }

        private static async Task<TextureReply> SendViaUnityWebRequestAsync(string url)
        {
            // nonReadable: no CPU-side copy needed — the sprite only needs the
            // GPU texture.
            using UnityWebRequest request = UnityWebRequestTexture.GetTexture(url, nonReadable: true);
            request.timeout = TimeoutSeconds;

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();

            while (!operation.isDone)
                await Task.Yield();

            if (request.result == UnityWebRequest.Result.ConnectionError)
            {
                return TextureReply.Transport(
                    $"GET {XsollaIdHttp.Sanitize(url)} — {request.error}");
            }

            // DataProcessingError is NOT a network issue: the bytes arrived but
            // the decoder rejected them (usually a format UnityWebRequestTexture
            // does not support — exactly how webp from Xsolla ID showed up). We
            // return a status code so it becomes PictureUnavailable rather than
            // Network upstream: a network failure invites a retry, this does not.
            if (request.result == UnityWebRequest.Result.DataProcessingError)
                return TextureReply.Status(415);

            return TextureReply.Status(
                request.responseCode, (request.downloadHandler as DownloadHandlerTexture)?.texture);
        }

        private static Sprite ToSprite(Texture2D texture)
        {
            // Named so the profiler shows whose texture this is.
            texture.name = "XsollaIdAvatar";

            return Sprite.Create(
                texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
