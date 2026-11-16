using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace LongLeap.XsollaId.Web
{
    // Decodes the profile picture using the browser.
    //
    // WHY. Xsolla ID serves avatars as WEBP
    // (cdn.xsolla.net/xsolla-id-v2-assets-prod/iam-assets/avatars/…/*.webp),
    // while DownloadHandlerTexture only handles PNG and JPEG. The bytes arrive
    // in full (the CDN has CORS configured), but the decoder fails and
    // UnityWebRequest reports DataProcessingError. The browser decodes webp
    // natively, so under WebGL we ask it to, and Unity receives raw RGBA.
    //
    // This also covers future formats: avif and anything else Xsolla enables
    // later goes through the same path with no changes here.
    //
    // Polling rather than a callback: the rest of the package waits for the
    // network the same way (`while (!op.isDone) await Task.Yield()`), the only
    // safe option under WebGL, which has no threads. No SendMessage and no
    // receiver object in the scene are needed.
    internal static class XsollaIdWebGLImageDecoder
    {
        private const int StatusPending = 0;
        private const int StatusReady = 1;
        private const int StatusNetworkFailed = 2;
        private const int StatusDecodeFailed = 3;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int XsollaIdBeginImageDecode(string url);

        [DllImport("__Internal")]
        private static extern int XsollaIdPollImageDecode(int handle);

        [DllImport("__Internal")]
        private static extern int XsollaIdImageWidth(int handle);

        [DllImport("__Internal")]
        private static extern int XsollaIdImageHeight(int handle);

        [DllImport("__Internal")]
        private static extern int XsollaIdCopyImagePixels(int handle, IntPtr destination, int byteLength);

        [DllImport("__Internal")]
        private static extern void XsollaIdReleaseImage(int handle);

        public static bool IsSupported => true;
#else
        // Outside the browser there is nothing to decode with. These stubs only
        // let the file compile in the editor and in test assemblies; they must
        // never be called, and IsSupported guarantees that.
        private static int XsollaIdBeginImageDecode(string url) => 0;
        private static int XsollaIdPollImageDecode(int handle) => StatusNetworkFailed;
        private static int XsollaIdImageWidth(int handle) => 0;
        private static int XsollaIdImageHeight(int handle) => 0;
        private static int XsollaIdCopyImagePixels(int handle, IntPtr destination, int byteLength) => 0;
        private static void XsollaIdReleaseImage(int handle) { }

        public static bool IsSupported => false;
#endif

        // Never throws: every failure comes back as a TextureReply so the loader
        // above classifies it the same way as a regular network reply.
        public static async Task<TextureReply> DecodeAsync(string url, int timeoutSeconds)
        {
            if (!IsSupported)
                return TextureReply.Transport("browser image decoding is only available in a WebGL build.");

            int handle = XsollaIdBeginImageDecode(url);

            if (handle == 0)
                return TextureReply.Transport($"GET {XsollaIdHttp.Sanitize(url)} — the decoder refused the request.");

            try
            {
                float deadline = Time.realtimeSinceStartup + timeoutSeconds;
                int status = XsollaIdPollImageDecode(handle);

                while (status == StatusPending)
                {
                    if (Time.realtimeSinceStartup >= deadline)
                    {
                        return TextureReply.Transport(
                            $"GET {XsollaIdHttp.Sanitize(url)} — timed out after {timeoutSeconds} s.");
                    }

                    await Task.Yield();

                    status = XsollaIdPollImageDecode(handle);
                }

                if (status == StatusNetworkFailed)
                {
                    // A CORS rejection also lands here: the response never
                    // reaches us and cannot be told apart from "no network",
                    // same as in XsollaIdHttp.
                    return TextureReply.Transport($"GET {XsollaIdHttp.Sanitize(url)} — the request did not complete.");
                }

                if (status != StatusReady)
                {
                    // The server answered, but not with an image: a 403/404 from
                    // the CDN or an HTML error page. Not a transport error — we
                    // return a status code so the loader reports
                    // PictureUnavailable rather than a network failure.
                    return TextureReply.Status(415);
                }

                int width = XsollaIdImageWidth(handle);
                int height = XsollaIdImageHeight(handle);

                if (width <= 0 || height <= 0)
                    return TextureReply.Status(415);

                int byteLength = width * height * 4;
                IntPtr buffer = Marshal.AllocHGlobal(byteLength);

                try
                {
                    if (XsollaIdCopyImagePixels(handle, buffer, byteLength) != 1)
                        return TextureReply.Status(415);

                    var pixels = new byte[byteLength];
                    Marshal.Copy(buffer, pixels, 0, byteLength);

                    // RGBA32 without mipmaps: the format must match what
                    // getImageData returned byte for byte.
                    var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    texture.LoadRawTextureData(pixels);

                    // makeNoLongerReadable: the CPU-side copy is no longer
                    // needed, the sprite only needs the GPU texture.
                    texture.Apply(false, true);

                    return TextureReply.Ok(texture);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (Exception ex)
            {
                return TextureReply.Transport($"GET {XsollaIdHttp.Sanitize(url)} — {ex.Message}");
            }
            finally
            {
                // Otherwise the avatar stays in the JS heap until the page
                // reloads.
                XsollaIdReleaseImage(handle);
            }
        }
    }
}
