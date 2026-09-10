using System;
using System.Collections.Generic;

namespace LongLeap.XsollaId
{
    // Проверка, что игра открыта на origin'е, с которого вход вообще может
    // сработать.
    //
    // Зачем: сервер сверяет redirect_uri посимвольно и при несовпадении
    // отказывает — но увидим мы это уже ПОСЛЕ открытия окна и в невнятной
    // формулировке. Дешевле проверить до и сказать прямо.
    //
    // Почему список, а не сравнение с redirect uri: вход теперь есть и в
    // портальной сборке, где игру отдаёт GamePix со своего origin'а, а
    // страница обратного вызова может лежать на нашем домене. То есть origin
    // игры и origin redirect uri СОВПАДАТЬ НЕ ОБЯЗАНЫ, и требовать этого
    // нельзя. Проверяем принадлежность к списку разрешённых.
    //
    // Ловит два реальных случая:
    //   1) serve.py печатает 127.0.0.1, а разработчик по привычке набирает
    //      localhost — это РАЗНЫЕ origin'ы, и вход упадёт;
    //   2) в конфиге прода остался чужой домен — узнаем на первом же клике,
    //      а не из отчёта игрока.
    //
    // Чистая функция без UnityEngine, чтобы проверялась тестами.
    public static class OriginGuard
    {
        // Разрешён ли origin страницы.
        //
        // pageUrl — полный адрес страницы (в игре Application.absoluteURL).
        // Пустой pageUrl (редактор, standalone) проходит: origin'а там нет.
        //
        // Пустой allowedOrigins тоже проходит: список не задан — значит
        // ограничивать нечего, пусть решает сервер. Молча блокировать вход
        // из-за незаполненного конфига хуже, чем пропустить.
        public static bool IsAllowed(
            string pageUrl, IReadOnlyList<string> allowedOrigins, out string problem)
        {
            problem = null;

            if (string.IsNullOrEmpty(pageUrl))
                return true;

            if (allowedOrigins == null || allowedOrigins.Count == 0)
                return true;

            string pageOrigin = TryGetOrigin(pageUrl);

            if (pageOrigin == null)
            {
                // Разобрать не смогли — не мешаем.
                return true;
            }

            var known = new List<string>(allowedOrigins.Count);

            foreach (string allowed in allowedOrigins)
            {
                string origin = TryGetOrigin(allowed);

                if (origin == null)
                    continue;

                known.Add(origin);

                if (string.Equals(pageOrigin, origin, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Ни один не подошёл — но если разобрать не удалось ни одного,
            // считаем, что списка нет.
            if (known.Count == 0)
                return true;

            problem =
                $"the game is served from {pageOrigin}, but sign-in is allowed only from {string.Join(", ", known)}. " +
                $"The server compares the address character by character, so the sign-in window would open for nothing.";

            return false;
        }

        // scheme://host:port — то, что браузер считает origin'ом. Порт по
        // умолчанию не дописываем: обе стороны приводим одинаково, так что
        // сокращённая форма совпадёт сама с собой.
        public static string TryGetOrigin(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                return null;

            return uri.IsDefaultPort
                ? $"{uri.Scheme}://{uri.Host}"
                : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        }
    }
}
