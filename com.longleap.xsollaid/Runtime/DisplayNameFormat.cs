namespace LongLeap.XsollaId
{
    // Как показывать DisplayName игроку.
    //
    // Отдельный чистый метод, а не логика в виджете: userinfo отдаёт имя
    // четырьмя разными клеймами (XsollaIdTokenClient.PickDisplayName), и
    // последний из них — email. То есть у части игроков «имя» это адрес почты,
    // и показывать его целиком нельзя: главное меню попадает в скриншоты и
    // стримы.
    public static class DisplayNameFormat
    {
        // anton@mail.com → anton. Имя без '@' отдаём как есть.
        //
        // Режем по ПЕРВОМУ '@', хотя RFC 5321 для разбора адреса предписывает
        // последний. Задача здесь не разобрать адрес, а гарантированно не
        // вывести домен на экран — по первому '@' это выполняется всегда.
        //
        // Пустая локальная часть (@mail.com, просто @) — это не адрес, а мусор:
        // отдаём как есть, потому что пустое место на экране хуже странного
        // имени.
        public static string LocalPart(string displayName)
        {
            // null отдаём null'ом, а не пустой строкой: вызывающие уже умеют его
            // обрабатывать (`?? "-"` в XsollaIdDebugPanel), а подмена на ""
            // эту проверку тихо обошла бы.
            if (displayName == null)
                return null;

            string trimmed = displayName.Trim();

            if (trimmed.Length == 0)
                return string.Empty;

            int at = trimmed.IndexOf('@');

            // -1 — '@' нет вовсе, 0 — пустая локальная часть. Оба случая
            // означают «резать нечего».
            if (at <= 0)
                return trimmed;

            return trimmed.Substring(0, at);
        }
    }
}
