using System;
using System.Collections.Generic;
using UnityEngine;

namespace LongLeap.Core.Ads
{
    // Кольцевой журнал событий рекламы для экранного оверлея.
    //
    // Нужен потому, что в портальной сборке игра живёт в чужом iframe: консоль
    // браузера там достать неудобно, а результат показа знать надо — особенно
    // отказ и таймаут, которые иначе выглядят как «кнопка просто не работает».
    //
    // Пишут сюда сами провайдеры, а не места вызова: только провайдер знает про
    // таймаут и про то, что площадка проглотила повторный запрос.
    public static class AdsDebug
    {
        private const int Capacity = 12;

        private static readonly List<string> _lines = new List<string>();

        // Оверлей перерисовывается по этому событию, а не опросом каждый кадр.
        public static event Action Changed;

        public static IReadOnlyList<string> Lines => _lines;

        public static void Record(string message)
        {
            // Время берём системное (DateTime, не Time.realtimeSinceStartup):
            // строка нужна человеку, а не игровой логике, и часы на ней должны
            // совпадать с тем, что покажет консоль браузера.
            string formattedMessage = DateTime.Now.ToString("HH:mm:ss") + "  " + message;

            _lines.Add(formattedMessage);

            // Дублируем в консоль, а не только в оверлей: на опубликованной
            // странице лог можно выгрузить и разобрать потом, а оверлей живёт
            // только пока открыта вкладка — и в сборке на ревью он выключен.
            // Метку времени оставляем: консоль браузера своих часов не рисует.
            Debug.Log(formattedMessage);

            if (_lines.Count > Capacity)
                _lines.RemoveAt(0);

            Changed?.Invoke();
        }

        public static void Clear()
        {
            _lines.Clear();

            Changed?.Invoke();
        }
    }
}
