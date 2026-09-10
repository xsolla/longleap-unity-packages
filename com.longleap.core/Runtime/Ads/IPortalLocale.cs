namespace LongLeap.Core.Ads
{
    // Язык, который портал выбрал для игрока. Нужен, чтобы на первом запуске
    // показать игру на его языке — площадки этого требуют, а не просят.
    public interface IPortalLocale
    {
        // false — портала нет или язык не определён, тогда работает обычная
        // настройка языка из сохранений.
        bool TryGetLocale(out string twoLetterCode);
    }
}
