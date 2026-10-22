using System.Collections.Generic;
using LongLeap.Backpack;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.UI
{
    /// <summary>Что сейчас можно сделать с предметом рюкзака — см. <see cref="BackPackItemView.SetState"/>.</summary>
    public enum BackPackItemState
    {
        /// <summary>Можно выбрать на заезд.</summary>
        Available,

        /// <summary>Выбран: активируется в начале заезда. Нажатие снимает выбор.</summary>
        Selected,

        /// <summary>
        /// Выбрать нельзя: игра не знает его награду, его товар не годится для
        /// момента, в который открыт рюкзак, такого товара уже выбрано сколько можно
        /// или идущий заезд его не примет (полное здоровье, щит уже стоит).
        /// </summary>
        Unavailable
    }

    /// <summary>
    /// Одна карточка в <see cref="UserBackPackWindow"/>: предмет из рюкзака игрока
    /// на сервере — картинка, название, описание, награды и кнопка «активировать».
    ///
    /// Награды — столбик строк (<see cref="BackPackRewardView"/>): иконка эффекта
    /// и что он даст, например монета и «+$100». Сервер присылает только ключи
    /// наград, а иконки и подписи знает игра (каталог товаров), поэтому строки
    /// собирает окно и отдаёт сюда (<see cref="SetRewards"/>).
    ///
    /// Кнопка не тратит предмет сразу, а выбирает его на заезд (повторное нажатие —
    /// снимает выбор): потратить предмет и выдать его товар окно может только
    /// когда заезд уже начат — см. <see cref="UserBackPackWindow.ActivateSelectedAsync"/>.
    /// Какое у карточки состояние, решает окно (<see cref="SetState"/>), карточка
    /// только рисует его.
    ///
    /// Картинка приходит с сервера ссылкой и скачивается уже после показа
    /// карточки (<see cref="SetIcon"/>) — до этого иконка спрятана, чтобы на её
    /// месте не висел белый квадрат.
    ///
    /// Живёт в проекте ШАБЛОНОМ (обычно отдельным префабом) — окно размножает
    /// его по содержимому рюкзака, поэтому объект держит выключенным:
    /// включённый шаблон остался бы на экране лишней карточкой.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BackPackItemView : MonoBehaviour
    {
        [Tooltip("Картинка предмета.")]
        [SerializeField] private Image _icon;

        [Tooltip("Название предмета.")]
        [SerializeField] private TMP_Text _title;

        [Tooltip("Описание предмета.")]
        [SerializeField] private TMP_Text _description;

        [Header("Награды")]

        [Tooltip("Заголовок над списком наград. Необязателен. Наград нет — прячется.")]
        [SerializeField] private TMP_Text _rewards;

        [Tooltip("Текст заголовка наград.")]
        [SerializeField] private string _rewardsPrefix = "REWARD: ";

        [Tooltip("Шаблон строки награды: иконка и подпись. Лежит внутри карточки — держи " +
                 "выключенным: включённый останется лишней пустой строкой.")]
        [SerializeField] private BackPackRewardView _rewardTemplate;

        [Tooltip("Куда класть строки наград (объект с VerticalLayoutGroup). Пусто — родитель шаблона.")]
        [SerializeField] private Transform _rewardsRoot;

        [Header("Активация")]

        [Tooltip("Кнопка «активировать»: выбирает предмет на заезд, повторное нажатие снимает выбор.")]
        [SerializeField] private Button _activateButton;

        [Tooltip("Подпись кнопки. Необязательна — без неё кнопка не меняет текст.")]
        [SerializeField] private TMP_Text _activateLabel;

        [Tooltip("Подпись кнопки, пока предмет не выбран.")]
        [SerializeField] private string _activateText = "ACTIVATE";

        [Tooltip("Подпись кнопки, когда предмет выбран на заезд.")]
        [SerializeField] private string _selectedText = "ACTIVE";

        [Tooltip("Подпись кнопки, когда выбрать предмет нельзя.")]
        [SerializeField] private string _unavailableText = "UNAVAILABLE";

        [Header("Фон")]

        [Tooltip("Фон карточки: его спрайт меняется, когда предмет выбран на заезд. Необязателен.")]
        [SerializeField] private Image _background;

        [Tooltip("Фон, пока предмет не выбран.")]
        [SerializeField] private Sprite _normalFrame;

        [Tooltip("Фон, когда предмет выбран на заезд.")]
        [SerializeField] private Sprite _selectedFrame;

        /// <summary>Созданные строки наград — переиспользуются при каждом <see cref="SetRewards"/>.</summary>
        private readonly List<BackPackRewardView> _rewardViews = new List<BackPackRewardView>();

        private bool _warnedNoRewardTemplate;

        /// <summary>Предмет, который показывает эта карточка — задаётся в <see cref="Show"/>.</summary>
        public BackPackRewardItem Item { get; private set; }

        /// <summary>Кнопка «активировать» — окно вешает на неё обработчик.</summary>
        public Button ActivateButton => _activateButton;

        public void Show(BackPackRewardItem item)
        {
            Item = item;

            // Названия у предмета может не быть — тогда хотя бы sku, иначе карточка без подписи.
            if (_title != null)
                _title.text = string.IsNullOrEmpty(item.Name) ? item.Sku : item.Name;

            if (_description != null)
                _description.text = item.Description ?? "";

            SetIcon(null);
            SetRewards(null);
            SetState(BackPackItemState.Available);
        }

        /// <summary>
        /// Награды столбиком, по строке на каждую. Пусто — прячем и заголовок,
        /// а не оставляем его над пустым местом. Лишние строки от прошлого
        /// вызова гасим, а не удаляем: окно зовёт это на каждое перечитывание.
        /// </summary>
        public void SetRewards(IReadOnlyList<BackPackRewardEntry> rewards)
        {
            var count = rewards?.Count ?? 0;

            if (_rewards != null)
            {
                _rewards.gameObject.SetActive(count > 0);
                _rewards.text = _rewardsPrefix;
            }

            var template = ResolveRewardTemplate(count > 0);
            if (template == null) return;

            var parent = _rewardsRoot != null ? _rewardsRoot : template.transform.parent;

            while (_rewardViews.Count < count)
                _rewardViews.Add(Instantiate(template, parent));

            for (var i = 0; i < _rewardViews.Count; i++)
            {
                var visible = i < count;
                _rewardViews[i].gameObject.SetActive(visible);

                if (visible)
                    _rewardViews[i].Show(rewards[i]);
            }
        }

        public void SetIcon(Sprite icon)
        {
            if (_icon == null) return;

            _icon.sprite = icon;
            _icon.enabled = icon != null;
        }

        public void SetState(BackPackItemState state)
        {
            if (_activateButton != null)
                _activateButton.interactable = state != BackPackItemState.Unavailable;

            // Пустой спрайт не ставим: забытое в инспекторе поле не должно стирать фон.
            var frame = state == BackPackItemState.Selected ? _selectedFrame : _normalFrame;

            if (_background != null && frame != null)
                _background.sprite = frame;

            if (_activateLabel == null) return;

            switch (state)
            {
                case BackPackItemState.Selected:
                    _activateLabel.text = _selectedText;
                    break;

                case BackPackItemState.Unavailable:
                    _activateLabel.text = _unavailableText;
                    break;

                default:
                    _activateLabel.text = _activateText;
                    break;
            }
        }

        /// <summary>
        /// Шаблон строки награды, погашенный: включённым он остался бы лишней
        /// пустой строкой. Нет шаблона — жалуемся один раз и только когда есть
        /// что показывать.
        /// </summary>
        private BackPackRewardView ResolveRewardTemplate(bool needed)
        {
            if (_rewardTemplate == null)
            {
                if (needed && !_warnedNoRewardTemplate)
                {
                    _warnedNoRewardTemplate = true;
                    Debug.LogError(
                        "[BackPack] карточке не задан шаблон строки награды (Reward Template) — " +
                        "награды не покажутся.", this);
                }

                return null;
            }

            if (_rewardTemplate.gameObject.activeSelf)
                _rewardTemplate.gameObject.SetActive(false);

            return _rewardTemplate;
        }
    }
}
