using System;
using System.Collections.Generic;
using Spine.Unity;
using UnityEngine;

namespace LongLeap.Skins
{
    /// <summary>
    /// Источник каталога скинов для рантайма: скелет + раскладка JSON рядом с ним
    /// (см. <see cref="SkinLayout"/>, правится окном
    /// <c>LongLeap/Skin Layout Editor</c>). Заменяет собой ручное перечисление
    /// категорий и скинов в инспекторе — единственное место, где раскладку
    /// действительно составляет геймдизайнер/художник, это сам JSON.
    ///
    /// Иконки скинов лежат здесь, а не в JSON: ссылку на спрайт JsonUtility в текст
    /// не сохранит (в редакторе запишет instanceID, который в билде ничего не
    /// значит). Ассет лежит в Resources, поэтому всё, на что он ссылается, само
    /// попадает в билд. Правятся иконки тем же Skin Layout Editor — он находит
    /// этот ассет по ссылке на открытый JSON.
    ///
    /// Где ассет лежит и как загружается — решает игра: пакету это неизвестно.
    /// </summary>
    [CreateAssetMenu(menuName = "LongLeap/Skins/Skin Layout Source", fileName = "SkinLayoutSource")]
    public sealed class SkinLayoutSourceSO : ScriptableObject, IHasSkeletonDataAsset
    {
        /// <summary>Иконка одного скина в панели кастомизации.</summary>
        [Serializable]
        public sealed class SkinIcon
        {
            [Tooltip("Полное имя скина, как в Spine Editor: «Heads/Alien».")]
            [SpineSkin]
            public string Skin = "";

            [Tooltip("Иконка варианта в панели кастомизации.")]
            public Sprite Icon;
        }

        [Tooltip("Скелет, чьи папки скинов перечислены в раскладке.")]
        [SerializeField] private SkeletonDataAsset _skeleton;

        [Tooltip("Раскладка скинов по категориям — JSON рядом со скелетом, тот же " +
                 "файл, что открыт в Skin Layout Editor.")]
        [SerializeField] private TextAsset _layout;

        [Tooltip("Иконки скинов — по одной на скин. Удобнее править в Skin Layout Editor, " +
                 "там поле иконки стоит в строке каждого скина.")]
        [SerializeField] private List<SkinIcon> _icons = new List<SkinIcon>();

        /// <summary>Нужен выпадающему списку <c>[SpineSkin]</c> у иконок в инспекторе.</summary>
        public SkeletonDataAsset SkeletonDataAsset => _skeleton;

        /// <summary>JSON раскладки — по нему Skin Layout Editor находит, куда писать иконки.</summary>
        public TextAsset Layout => _layout;

        /// <summary>
        /// Собрать каталог для рантайма: реальные скины берутся из скелета, а
        /// категории, подписи и доступность — из раскладки, иконки — из этого ассета.
        /// </summary>
        public SkinCatalog BuildCatalog()
        {
            if (_skeleton == null)
            {
                Debug.LogError($"[Player] {name}: не задан Skeleton — каталог скинов будет пустым.", this);
                return new SkinCatalog();
            }

            if (_layout == null)
            {
                Debug.LogError($"[Player] {name}: не задан Layout JSON — каталог скинов будет пустым.", this);
                return new SkinCatalog();
            }

            var data = _skeleton.GetSkeletonData(true);
            var layout = SkinLayout.FromJson(_layout.text);

            var catalog = layout.BuildCatalog(data, name);
            ApplyIcons(catalog);

            return catalog;
        }

        /// <summary>Иконка скина, если её задали, — иначе null.</summary>
        public Sprite GetIcon(string skin)
        {
            var entry = FindIcon(skin);
            return entry != null ? entry.Icon : null;
        }

        /// <summary>
        /// Задать иконку скина; null — убрать запись совсем, а не хранить пустую.
        /// Только для авторского инструмента (Skin Layout Editor): Undo и SetDirty —
        /// на стороне вызывающего.
        /// </summary>
        public void SetIcon(string skin, Sprite icon)
        {
            var entry = FindIcon(skin);

            if (icon == null)
            {
                if (entry != null) _icons.Remove(entry);
                return;
            }

            if (entry == null)
                _icons.Add(entry = new SkinIcon { Skin = skin });

            entry.Icon = icon;
        }

        private SkinIcon FindIcon(string skin)
        {
            foreach (var entry in _icons)
            {
                if (entry != null && entry.Skin == skin) return entry;
            }

            return null;
        }

        /// <summary>
        /// Доложить иконки в собранный каталог. Идём по скинам каталога, а не по
        /// записям: SkinId из произвольной строки бросает на мусоре, а скины каталога
        /// уже проверены. Записи, не нашедшие скина (переименовали в Spine, папку
        /// убрали из раскладки), загрузку не роняют — только предупреждение.
        /// </summary>
        private void ApplyIcons(SkinCatalog catalog)
        {
            var icons = new Dictionary<string, Sprite>(StringComparer.Ordinal);

            foreach (var entry in _icons)
            {
                if (entry != null && entry.Icon != null && !string.IsNullOrEmpty(entry.Skin))
                    icons[entry.Skin] = entry.Icon;
            }

            if (icons.Count == 0) return;

            foreach (var category in catalog.Categories)
            foreach (var skin in catalog.GetSkins(category))
            {
                if (!icons.TryGetValue(skin.Value, out var icon)) continue;

                catalog.SetIcon(skin, icon);
                icons.Remove(skin.Value);
            }

            // Отключённые скины в каталог не входят, но иконка у них не «потерялась» —
            // включат скин обратно, и она понадобится снова.
            foreach (var skin in catalog.DisabledSkins)
                icons.Remove(skin.Value);

            foreach (var stale in icons.Keys)
                Debug.LogWarning(
                    $"[Player] {name}: иконка задана для скина '{stale}', которого нет в каталоге — " +
                    "пропущена. Скин переименовали в Spine или его папку убрали из раскладки.", this);
        }
    }
}
