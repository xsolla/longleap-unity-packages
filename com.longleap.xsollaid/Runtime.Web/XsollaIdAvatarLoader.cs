using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace LongLeap.XsollaId.Web
{
    // Что вернула загрузка картинки.
    //
    // Отдельно от HttpReply намеренно: там тело — строка, а картинку в WebGL
    // отдаёт нативный декодер браузера уже готовой текстурой. Гонять её через
    // байты значило бы декодировать второй раз, уже в wasm, и тащить в сборку
    // ImageConversionModule ради Texture2D.LoadImage.
    //
    // Различение «транспорт не дошёл» / «сервер ответил кодом» такое же и по той
    // же причине, что у HttpReply: в браузере отсутствие CORS-заголовка выглядит
    // как ConnectionError с нулевым StatusCode, а не как ответ сервера.
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

    // Загрузка картинки профиля Xsolla ID.
    //
    // Повадки транспорта скопированы с XsollaIdHttp, чтобы в проекте был один
    // способ ходить по сети: UnityWebRequest, ожидание через
    // `while (!op.isDone) await Task.Yield()` (единственный безопасный вариант
    // под WebGL — потоков там нет), request.timeout = 10 и вычищенные от
    // параметров адреса в сообщениях.
    public sealed class XsollaIdAvatarLoader : IXsollaIdAvatarLoader
    {
        private const int TimeoutSeconds = 10;

        // Больше картинок за одну страницу быть не должно: аккаунт один,
        // максимум сменится на другой. Рост выше порога — признак утечки, и
        // увидеть его в консоли лучше, чем в профайлере через час.
        private const int SaneCacheSize = 8;

        // Кэш СТАТИЧЕСКИЙ, а не по экземпляру: сцена перезагружается на смерти
        // игрока, GameBootstrapper вместе с ней пересоздаётся, и кэш в поле
        // экземпляра умирал бы каждый забег. Статическая ссылка ещё и держит
        // текстуру живой — Resources.UnloadUnusedAssets, который Unity зовёт при
        // загрузке сцены, считает статические поля корнями.
        //
        // Кладём саму задачу, а не спрайт. Так одним механизмом закрываются
        // сразу три: одна и та же картинка не качается дважды, если её
        // попросили два виджета в одном кадре (тот же приём, что
        // _signInInFlight в XsollaIdAuth); успех кэшируется; и отказ тоже —
        // иначе мёртвый адрес переспрашивался бы на каждой перезагрузке сцены.
        private static readonly Dictionary<string, Task<AvatarResult>> Cached =
            new Dictionary<string, Task<AvatarResult>>();

        private readonly Func<string, Task<TextureReply>> _download;

        public XsollaIdAvatarLoader(Func<string, Task<TextureReply>> download = null)
        {
            _download = download ?? SendAsync;
        }

        // UnityWebRequestTexture работает и в редакторе, и на любой платформе:
        // всплывающего окна здесь нет, поэтому в отличие от
        // XsollaIdWebBrowser.IsSupported врать про UNITY_WEBGL незачем. Это
        // единственная часть входа, которую можно проверить, не собирая WebGL.
        public bool IsAvailable => true;

        public Task<AvatarResult> LoadAsync(AvatarUrl url)
        {
            // Ни одного запроса: адреса нет или он не прошёл проверку.
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

            // Задача может быть ещё в полёте — тогда готового спрайта нет, и
            // ждать здесь нельзя: метод синхронный именно для того, чтобы не
            // ждать.
            if (task.Status != TaskStatus.RanToCompletion || !(task.Result is AvatarResult.Success ok))
                return false;

            sprite = ok.Sprite;

            return sprite != null;
        }

        // Единственное место, где текстуры уничтожаются. В рантайме НЕ зовём —
        // см. комментарий о времени жизни в IXsollaIdAvatarLoader.
        //
        // Нужен тестам: кэш статический, а NUnit гоняет все фикстуры в одном
        // домене, и без сброса тесты видели бы кэш друг друга и оставляли за
        // собой текстуры в редакторе.
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
                // Наружу не бросаем никогда: этот Task опрашивает UI.
                return new AvatarResult.Failure(
                    XsollaIdErrorCode.Network, $"avatar download threw — {ex.Message}");
            }

            if (reply.IsTransportError)
            {
                // Здесь же оказывается и отсутствие CORS-заголовка на хосте
                // картинки: ответ до нас не доходит вовсе, StatusCode остаётся
                // нулём. Отличить это от «нет сети» невозможно — ровно как с
                // корневым /oauth2/revoke, см. XsollaIdTokenClient.RevokeAsync.
                return new AvatarResult.Failure(XsollaIdErrorCode.Network, reply.TransportError);
            }

            if (!reply.IsSuccess)
            {
                // Сервер ответил, но не картинкой: 403/404 от CDN либо 200 с
                // html-страницей ошибки, на которой декодер спотыкается.
                return new AvatarResult.Failure(
                    XsollaIdErrorCode.PictureUnavailable,
                    $"avatar request answered {reply.StatusCode} without a usable image.");
            }

            return new AvatarResult.Success(ToSprite(reply.Texture));
        }

        private static Task<TextureReply> SendAsync(string url)
        {
            // В браузере декодирует браузер. Причина конкретная: Xsolla ID
            // отдаёт аватар в WEBP, а DownloadHandlerTexture умеет только PNG и
            // JPEG — байты доезжают, а декодер отдаёт DataProcessingError.
            // Подробности в XsollaIdWebGLImageDecoder.
            if (XsollaIdWebGLImageDecoder.IsSupported)
                return XsollaIdWebGLImageDecoder.DecodeAsync(url, TimeoutSeconds);

            // В редакторе браузера нет. Оставляем UnityWebRequestTexture: PNG и
            // JPEG он берёт, и этого хватает, чтобы проверять загрузку, не
            // собирая WebGL.
            return SendViaUnityWebRequestAsync(url);
        }

        private static async Task<TextureReply> SendViaUnityWebRequestAsync(string url)
        {
            // nonReadable: копия в памяти CPU не нужна — спрайту достаточно
            // текстуры на GPU.
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

            // DataProcessingError — это НЕ сеть: байты доехали, но декодер их не
            // взял (обычно формат, который UnityWebRequestTexture не умеет —
            // ровно так проявлялся webp от Xsolla ID). Отдаём кодом, чтобы выше
            // это стало PictureUnavailable, а не Network: сеть предлагают
            // повторить, а тут повторять нечего.
            if (request.result == UnityWebRequest.Result.DataProcessingError)
                return TextureReply.Status(415);

            return TextureReply.Status(
                request.responseCode, (request.downloadHandler as DownloadHandlerTexture)?.texture);
        }

        private static Sprite ToSprite(Texture2D texture)
        {
            // Имя — чтобы в профайлере было видно, чья это текстура.
            texture.name = "XsollaIdAvatar";

            return Sprite.Create(
                texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
