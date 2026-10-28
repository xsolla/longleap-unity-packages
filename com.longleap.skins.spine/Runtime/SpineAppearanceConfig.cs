using System;
using Spine.Unity;
using UnityEngine;

namespace LongLeap.Skins
{
    /// <summary>
    /// Одно описание внешнего вида спайнового персонажа — скины, зоны перекраса и
    /// правила перекрытия. Ассет, а не поля компонента, потому что один и тот же набор
    /// нужен и геймплейному персонажу, и его превью в меню; продублированный в обоих
    /// префабах, он рисковал бы разъехаться при правке только одного из них.
    ///
    /// Компонент внешнего вида (он живёт в игре) ссылается на ассет и только читает
    /// его. Цвета, выставленные игроком, в ассет не пишутся и живут в
    /// рантайм-состоянии компонента — иначе перекраска одного
    /// персонажа в сессии перекрашивала бы всех, кто ссылается на тот же ассет
    /// (ScriptableObject один на всех, кто на него смотрит).
    ///
    /// Ссылка на <see cref="SkeletonDataAsset"/> — не для рантайма, а только чтобы
    /// выпадающие списки <c>[SpineSkin]</c> и <c>[SpineSlot]</c> в инспекторе САМОГО
    /// ассета знали, к какому скелету предлагать варианты (см. <c>IHasSkeletonDataAsset</c>
    /// в SpineAttributeDrawers).
    /// </summary>
    [CreateAssetMenu(menuName = "LongLeap/Skins/Spine Appearance Config", fileName = "SpineAppearanceConfig")]
    public sealed class SpineAppearanceConfig : ScriptableObject, IHasSkeletonDataAsset
    {
        /// <summary>Зона перекраса: набор слотов, которые игра красит одним цветом.</summary>
        [Serializable]
        public sealed class ColorZone
        {
            [Tooltip("Имя зоны — им её просят из кода (skin, wheels, shirt). " +
                     "Регистр не важен.")]
            public string Name = "";

            [Tooltip("Цвет зоны по умолчанию. Белый — «как нарисовано»: умножение на белый ничего не меняет.")]
            public Color Color = Color.white;

            [Tooltip("Слоты скелета, которые красит зона. Выбираются из списка самого " +
                     "скелета, поэтому опечататься нельзя.")]
            [SpineSlot]
            public string[] Slots = new string[0];
        }

        /// <summary>Правило перекрытия: какие слоты гаснут, пока надет скин.</summary>
        [Serializable]
        public sealed class HideRule
        {
            [Tooltip("Скин, который перекрывает собой другие части. Пока он надет, " +
                     "слоты ниже не показываются.")]
            [SpineSkin]
            public string Skin = "";

            [Tooltip("Слоты, которые гаснут, пока скин надет.")]
            [SpineSlot]
            public string[] Slots = new string[0];
        }

        [Tooltip("Скелет, чьи скины и слоты предлагаются в выпадающих списках ниже.")]
        [SerializeField] private SkeletonDataAsset _skeletonData;

        [Header("Skins")]
        [Tooltip("Комбинация скинов: по одному на категорию (Heads/..., Top/..., " +
                 "Skateboards/... и так далее). Имена выбираются из списка самого скелета, " +
                 "поэтому опечататься нельзя. Порядок значения не имеет.")]
        [SpineSkin]
        [SerializeField] private string[] _skins = new string[0];

        [Header("Color Zones")]
        [Tooltip("Зоны перекраса: какая часть персонажа каким цветом красится по умолчанию.")]
        [SerializeField] private ColorZone[] _colorZones = new ColorZone[0];

        [Header("Hide Rules")]
        [Tooltip("Перекрытия: какой скин какие слоты прячет, пока он надет.")]
        [SerializeField] private HideRule[] _hideRules = new HideRule[0];

        public SkeletonDataAsset SkeletonDataAsset => _skeletonData;
        public string[] Skins => _skins;
        public ColorZone[] ColorZones => _colorZones;
        public HideRule[] HideRules => _hideRules;

        /// <summary>Правило перекрытия для скина, если оно уже заведено — иначе null.</summary>
        public HideRule FindHideRule(string skin)
        {
            for (int i = 0; i < _hideRules.Length; i++)
            {
                if (_hideRules[i] != null && _hideRules[i].Skin == skin)
                    return _hideRules[i];
            }

            return null;
        }

        /// <summary>
        /// Правило перекрытия для скина — существующее либо новое, без слотов.
        /// Только для авторского инструмента (Skin Layout Editor): в рантайме
        /// набор правил не меняется, поэтому массив-бэкинг растёт напрямую, а не
        /// через список.
        /// </summary>
        public HideRule GetOrAddHideRule(string skin)
        {
            var existing = FindHideRule(skin);
            if (existing != null) return existing;

            var rule = new HideRule { Skin = skin };

            var expanded = new HideRule[_hideRules.Length + 1];
            Array.Copy(_hideRules, expanded, _hideRules.Length);
            expanded[_hideRules.Length] = rule;
            _hideRules = expanded;

            return rule;
        }

        /// <summary>Убрать правило — например, когда в нём не осталось ни одного слота.</summary>
        public void RemoveHideRule(HideRule rule)
        {
            var index = Array.IndexOf(_hideRules, rule);
            if (index < 0) return;

            var shrunk = new HideRule[_hideRules.Length - 1];
            Array.Copy(_hideRules, 0, shrunk, 0, index);
            Array.Copy(_hideRules, index + 1, shrunk, index, _hideRules.Length - index - 1);
            _hideRules = shrunk;
        }
    }
}
