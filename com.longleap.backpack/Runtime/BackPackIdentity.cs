using UnityEngine;

namespace LongLeap.Backpack
{
    /// <summary>
    /// От чьего имени уходят события на BackPack-сервер.
    ///
    /// Отдельный держатель, а не поле сервиса: xsollaID появляется не при старте,
    /// а когда игрок войдёт (или когда тихо восстановится сессия), и меняется при
    /// выходе-входе. <see cref="BackPackService"/> спрашивает его на каждой
    /// отправке через делегат, поэтому достаточно записать сюда — переподнимать
    /// сервис не нужно.
    ///
    /// В Darkslide входа пока нет вовсе, поэтому здесь null: события не уходят,
    /// а ачивки сохраняются локально (см. BackPackAchievementStateStore). Когда
    /// вход появится, ему остаётся присвоить сюда id после успешной авторизации
    /// и обнулить при выходе.
    /// </summary>
    public static class BackPackIdentity
    {
        /// <summary>Идентификатор вошедшего игрока или null, пока входа не было.</summary>
        public static string XsollaId { get; private set; }

        /// <summary>Есть ли, от чьего имени отправлять.</summary>
        public static bool HasXsollaId => !string.IsNullOrEmpty(XsollaId);

        /// <summary>Игрок вошёл.</summary>
        public static void SetXsollaId(string xsollaId)
        {
            XsollaId = string.IsNullOrEmpty(xsollaId) ? null : xsollaId;
        }

        /// <summary>Игрок вышел: дальше события снова никуда не уходят.</summary>
        public static void Clear()
        {
            XsollaId = null;
        }

        /// <summary>Сбрасывает статику при отключённом Domain Reload.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            XsollaId = null;
        }
    }
}
