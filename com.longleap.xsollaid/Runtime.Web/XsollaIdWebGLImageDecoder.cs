using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace LongLeap.XsollaId.Web
{
    // Декодирование картинки профиля силами браузера.
    //
    // ЗАЧЕМ. Xsolla ID отдаёт аватар в WEBP
    // (cdn.xsolla.net/xsolla-id-v2-assets-prod/iam-assets/avatars/…/*.webp), а
    // DownloadHandlerTexture умеет только PNG и JPEG. Байты доезжают целиком —
    // CORS у CDN настроен, — но декодер спотыкается, и UnityWebRequest отдаёт
    // DataProcessingError. Браузер webp декодирует сам, поэтому под WebGL мы
    // просим его, а в Unity приходит уже сырой RGBA.
    //
    // Заодно это снимает вопрос про будущие форматы: avif и всё, что Xsolla
    // включит завтра, поедет тем же путём без правок здесь.
    //
    // Опрос, а не обратный вызов: ровно так же весь остальной пакет ждёт сеть
    // (`while (!op.isDone) await Task.Yield()`) — единственный безопасный
    // способ под WebGL, где потоков нет. Так не нужен ни SendMessage, ни
    // объект-приёмник на сцене.
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
        // Вне браузера декодировать нечем. Заглушки нужны только чтобы файл
        // компилировался в редакторе и в тестовых сборках; звать их нельзя, и
        // IsSupported это гарантирует.
        private static int XsollaIdBeginImageDecode(string url) => 0;
        private static int XsollaIdPollImageDecode(int handle) => StatusNetworkFailed;
        private static int XsollaIdImageWidth(int handle) => 0;
        private static int XsollaIdImageHeight(int handle) => 0;
        private static int XsollaIdCopyImagePixels(int handle, IntPtr destination, int byteLength) => 0;
        private static void XsollaIdReleaseImage(int handle) { }

        public static bool IsSupported => false;
#endif

        // Ничего не бросает: любой сбой приезжает как TextureReply, чтобы
        // загрузчик выше классифицировал его так же, как обычный ответ сети.
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
                    // Сюда же попадает срез по CORS: ответ до нас не доходит, и
                    // отличить его от «нет сети» нельзя — ровно как в
                    // XsollaIdHttp.
                    return TextureReply.Transport($"GET {XsollaIdHttp.Sanitize(url)} — the request did not complete.");
                }

                if (status != StatusReady)
                {
                    // Сервер ответил, но картинкой это не оказалось: 403/404 у
                    // CDN либо html-страница ошибки. Не транспортная ошибка —
                    // отдаём кодом, чтобы загрузчик назвал это
                    // PictureUnavailable, а не сетью.
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

                    // RGBA32 без мипмапов: формат обязан совпасть байт в байт с
                    // тем, что отдал getImageData.
                    var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    texture.LoadRawTextureData(pixels);

                    // makeNoLongerReadable: копия на стороне CPU больше не нужна,
                    // спрайту достаточно текстуры на GPU.
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
                // Иначе аватар остаётся лежать в куче JS до перезагрузки
                // страницы.
                XsollaIdReleaseImage(handle);
            }
        }
    }
}
