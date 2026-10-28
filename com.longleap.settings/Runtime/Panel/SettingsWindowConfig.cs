using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace LongLeap.Settings.UI
{
    /// <summary>
    /// Поле-строка с ключом настройки (<c>Setting.Key</c>). В инспекторе рисуется
    /// выпадающим списком всех ключей, которые есть в игре, — опечатку не набрать.
    /// </summary>
    public sealed class SettingKeyAttribute : PropertyAttribute { }

    /// <summary>
    /// Что показывает окно настроек: секции, строки в них и подписи. Назначается
    /// в префабе окна настроек (<c>LongLeap.UI.SettingsWindow</c>) — другому окну достаточно другого конфига.
    ///
    /// Конфиг решает только «что показать и как подписать». Сами значения, границы
    /// и умолчания остаются в группах настроек (звук, клавиши, камера): строка
    /// ссылается на настройку по ключу, а вид строки выбирается по типу настройки —
    /// число даёт ползунок, bool — переключатель, клавиша — кнопку переназначения.
    /// </summary>
    [CreateAssetMenu(menuName = "LongLeap/Settings Window Config", fileName = "SettingsWindowConfig")]
    public sealed class SettingsWindowConfig : ScriptableObject
    {
        [Header("Вид")]

        [Tooltip("Шрифт окна. Пусто — окно возьмёт шрифт любого другого текста UI.")]
        public TMP_FontAsset Font;

        [Tooltip("Размер панели в пикселях опорного разрешения 1920x1080.")]
        public Vector2 PanelSize = new Vector2(900f, 980f);

        [Header("Подписи окна")]

        public string Title = "SETTINGS";

        public string ListeningText = "PRESS A KEY";

        public string ResetText = "DEFAULTS";

        public string BackText = "BACK";

        [Tooltip("Что показать, если настройки не поднялись при загрузке.")]
        public string UnavailableText = "Settings are unavailable in this session.";

        [Header("Секции")]

        public List<Section> Sections = new List<Section>();

        [Serializable]
        public sealed class Section
        {
            public string Title;

            [Tooltip("Подсказка под секцией. Пусто — без подсказки.")]
            public string Hint;

            [Tooltip("Не показывать на тач-устройствах: клавиш там нет.")]
            public bool HideOnTouch;

            public List<Row> Rows = new List<Row>();
        }

        [Serializable]
        public sealed class Row
        {
            [SettingKey]
            [Tooltip("Ключ настройки. Неизвестный ключ — ошибка в консоли, строка пропускается.")]
            public string SettingKey;

            [Tooltip("Подпись, пока локализация не поднята.")]
            public string Label;
        }
    }
}
