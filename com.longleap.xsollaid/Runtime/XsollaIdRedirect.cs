using System;

namespace LongLeap.XsollaId
{
    // Откуда берётся redirect_uri.
    //
    // Зачем вычислять, а не просто брать из конфига: страница обратного вызова
    // едет В САМОЙ СБОРКЕ (её кладёт в корень XsollaIdCallbackPagePostprocessor),
    // поэтому у всякой сборки, которую отдаём мы сами, правильный адрес — рядом
    // с ней. А таких адресов минимум три: 127.0.0.1 у serve.py, домен Pages и
    // объектное хранилище. Одно поле в ассете на все три означало бы правку
    // ассета перед каждой сборкой — и забытую правку в релизе, где вход
    // открывает окно на мёртвый адрес.
    //
    // Портальная сборка — исключение, и вычислить её адрес нельзя: игру отдаёт
    // площадка со своего origin'а, положить туда свою страницу мы не можем, а
    // адрес площадки в клиенте Xsolla ID не зарегистрирован (и не может быть —
    // сервер сверяет redirect_uri посимвольно). Там адрес берётся из конфига.
    //
    // Чистая функция без UnityEngine — как OriginGuard, и по той же причине:
    // ошибка здесь видна только на живом входе, поэтому проверяется тестами.
    // Причину неудачи отдаём наружу out-параметром, тоже как OriginGuard:
    // логировать должен тот, у кого есть UnityEngine, иначе класс перестал бы
    // проверяться без сцены.
    public static class XsollaIdRedirect
    {
        // Имя обязано совпадать с тем, что кладёт в сборку постпроцесс
        // (XsollaIdCallbackPagePostprocessor.FileName). Разъехавшись, они дали
        // бы 404 в окне входа.
        public const string CallbackFileName = "xid-callback.html";

        // Адрес страницы обратного вызова рядом с игрой либо null, если
        // вычислить нельзя. null означает «возьмите значение из конфига» — сам
        // по себе он ничего не ломает.
        //
        // problem — почему не получилось, готовой фразой для лога; null при
        // успехе. Без неё «взяли адрес из ассета» выглядело бы в логе
        // неотличимо от «вычислили» — а это ровно та развилка, из-за которой
        // сервер отказывает в окне входа.
        //
        // Считаем от КАТАЛОГА страницы, а не от одного origin'а: сборку могут
        // отдавать из подпапки (https://host/game/index.html), и страница
        // обратного вызова лежит там же, а не в корне домена.
        public static string TryDerive(string pageUrl, out string problem)
        {
            problem = null;

            if (string.IsNullOrEmpty(pageUrl))
            {
                problem = "the page address is empty, which is normal in the editor and in a standalone player (there is no origin at all)";
                return null;
            }

            if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out Uri uri))
            {
                problem = $"the page address '{pageUrl}' is not an absolute URL";
                return null;
            }

            // Схемы вроде file:// отпадают: у них нет origin'а, который сервер
            // мог бы принять.
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                problem = $"the page is served over '{uri.Scheme}', which has no origin the server would accept; open the build over http(s)";
                return null;
            }

            string origin = OriginGuard.TryGetOrigin(pageUrl);

            if (origin == null)
            {
                problem = $"the origin of '{pageUrl}' could not be determined";
                return null;
            }

            return origin + DirectoryOf(uri.AbsolutePath) + CallbackFileName;
        }

        // Каталог страницы, всегда со слэшем на конце. Query и fragment сюда не
        // попадают: AbsolutePath их не содержит.
        private static string DirectoryOf(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return "/";

            int lastSlash = absolutePath.LastIndexOf('/');

            return lastSlash < 0 ? "/" : absolutePath.Substring(0, lastSlash + 1);
        }
    }
}
