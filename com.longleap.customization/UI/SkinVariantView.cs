using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.Customization
{
    /// <summary>
    /// Необязательное превью варианта. Реализация может жить в отдельной сборке
    /// (например, Spine), а UI передаёт ей только имя скина.
    /// </summary>
    public interface ISkinVariantPreview
    {
        void ShowSkin(string skinName);
    }

    /// <summary>
    /// Один вариант в списке кастомизации: кнопка, иконка скина, отметка «выбрано»
    /// и замок «ещё не открыт». Иконка своя у каждого скина (Skin Layout Editor) и
    /// показывается как нарисована — иконки полноцветные, поэтому цветом скина по
    /// умолчанию их не тонируем: умножение исказило бы рисунок. У скина без иконки
    /// объект иконки остаётся таким, каким лежит в шаблоне. Дополнительно превью может рисовать
    /// <see cref="ISkinVariantPreview"/> (например, спайновый рендер).
    ///
    /// Живёт в префабе как ШАБЛОН — панель кастомизации размножает его по
    /// каталогу скинов. Поэтому в префабе меню лежит ровно один такой объект на
    /// категорию (или один общий), а не по объекту на каждый скин: добавить скин
    /// в игру должно означать правку одного ассета каталога, а не правку и
    /// каталога, и префаба.
    ///
    /// Ссылки необязательны: нет замка — закрытые варианты будут выглядеть как
    /// обычные, но нажать их всё равно не выйдет (кнопка гасится).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkinVariantView : MonoBehaviour
    {
        [Tooltip("Кнопка варианта. Пусто — ищется на этом же объекте.")]
        [SerializeField] private Button _button;

        [Tooltip("Отметка «выбрано»: рамка, галка, подсветка. Необязательна.")]
        [SerializeField] private GameObject _selectedMark;

        [Tooltip("Замок «скин ещё не открыт». Необязателен.")]
        [SerializeField] private GameObject _lockMark;

        [Tooltip("Иконка скина: спрайт из Skin Layout Editor, у каждого скина свой. Объект " +
                 "включается, когда у скина есть иконка; без неё остаётся таким, каким лежит в " +
                 "шаблоне. Не тонируется — цвет Image задаёт префаб. Необязательна.")]
        [SerializeField] private Image _icon;

        private Button _resolved;
        private bool _resolvedOnce;

        private ISkinVariantPreview[] _previews;

        /// <summary>
        /// Кнопка варианта или null, если её нет вовсе (вариант только показывает
        /// себя, а листают стрелками категории).
        /// </summary>
        public Button Button
        {
            get
            {
                if (_resolvedOnce) return _resolved;

                _resolvedOnce = true;
                _resolved = _button != null ? _button : GetComponent<Button>();

                return _resolved;
            }
        }

        /// <summary>
        /// Превью внутри варианта — те, кто умеет показать скин собой (например
        /// спайновый SpineSkinPreview). Ищутся один раз: вариант показывают заново
        /// на каждое переодевание, а дети у него с рождения одни и те же.
        /// </summary>
        private ISkinVariantPreview[] Previews
        {
            get
            {
                if (_previews != null) return _previews;

                var behaviours = GetComponentsInChildren<MonoBehaviour>(true);
                var found = new List<ISkinVariantPreview>();

                for (var i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i] is ISkinVariantPreview preview)
                        found.Add(preview);
                }

                _previews = found.ToArray();

                return _previews;
            }
        }

        /// <summary>
        /// Показать вариант: открыт ли он, выбран ли сейчас и его иконку.
        ///
        /// Закрытый вариант виден, но не нажимается: игрок должен понимать, что
        /// в игре есть ещё скины, — это и есть вся витрина.
        /// </summary>
        /// <param name="icon">
        /// Иконка скина. null — шаблон не трогаем: что показать вместо иконки
        /// (ничего или плейсхолдер), решает префаб, а не код.
        /// </param>
        public void Show(string skinName, bool owned, bool selected, Sprite icon)
        {
            var previews = Previews;
            for (var i = 0; i < previews.Length; i++)
                previews[i].ShowSkin(skinName);

            if (_selectedMark != null)
                _selectedMark.SetActive(selected && owned);

            if (_lockMark != null)
                _lockMark.SetActive(!owned);

            if (_icon != null && icon != null)
            {
                _icon.sprite = icon;
                _icon.gameObject.SetActive(true);
            }

            if (Button != null)
                Button.interactable = owned;
        }
    }
}
