using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LongLeap.Backpack;
using LongLeap.XsollaId;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.UI
{
    /// <summary>
    /// Рюкзак игрока: показывает, что у игрока есть на сервере, — перед заездом.
    ///
    /// Содержимое приходит с BackPack-сервера (<see cref="IBackPackService.GetRewardsAsync"/>,
    /// GET {baseUrl}/rewards?xsollaId=…&amp;gameId=…) и перечитывается на каждом
    /// открытии: рюкзак меняется и вне игры (награды за ачивки выдаёт сервер).
    /// Сервис приносит загрузка игры, как и всем остальным окнам, —
    /// окно само про контейнер сервисов не знает.
    ///
    /// Пункты — сетка карточек (<see cref="BackPackItemView"/>): картинка, название,
    /// описание, награды столбиком (иконка и что получит игрок) и кнопка «активировать».
    ///
    /// Что даёт предмет, решает игра, а не сервер: сервер прикладывает к предмету
    /// ключи наград (Rewards, из reward_items.json по SKU), и ключ — это id товара
    /// товара (<see cref="IBackPackProduct.Id"/>, каталог — <see cref="IBackPackProductCatalog"/>).
    /// Активировать предмет — значит выдать игроку эти товары.
    ///
    /// Активация в два шага, и оба несущие:
    /// <list type="number">
    /// <item>«Активировать» в окне только ВЫБИРАЕТ предмет — на сервер ничего не
    /// уходит. Применимо ли товар, можно узнать только у идущего заезда
    /// (<see cref="IBackPackProduct.CanApply"/>), а до старта его нет, и сам старт
    /// сбросил бы выданное. Если же
    /// окно открыто посреди заезда, неприменимый прямо сейчас товар
    /// выбрать не дадим.</item>
    /// <item>Когда товар есть куда выдать (сразу после старта заезда), открывший окно зовёт
    /// <see cref="ActivateSelectedAsync"/>: по каждому выбранному предмету
    /// проверяем, применим ли товар; неприменимый НЕ тратим — предмет остаётся
    /// в рюкзаке. Применимый удаляем на сервере, и товар выдаём ТОЛЬКО после
    /// подтверждённого удаления: не удалось — не выдаём, предмет остаётся.</item>
    /// </list>
    /// Выбрать нельзя предмет, чью награду игра не знает (нет товара с таким id),
    /// чей товар не годится для момента, в который открыли окно
    /// (<see cref="IBackPackProduct.IsUsableIn"/>), и сверх того, сколько товара можно держать разом
    /// (<see cref="IBackPackProduct.Max"/>).
    ///
    /// Рюкзак есть только у вошедшего игрока, поэтому у окна две панели, и на
    /// экране всегда ровно одна: нет xsollaID — панель входа (призыв войти и
    /// кнопка входа через Xsolla ID, см. <see cref="BindAuth"/>), есть — панель
    /// предметов. После входа окно само переключается на предметы и перечитывает
    /// рюкзак. Кнопка сайта BackPack (там рюкзак целиком, со всеми играми) видна
    /// в обоих случаях.
    ///
    /// Связь с сервером проверяет не окно, а тот, кто его открывает
    /// (<see cref="CheckConnectionAsync"/>): без связи рюкзак открывать незачем.
    ///
    /// Открывающий сперва отдаёт через <see cref="Prepare"/>, в какой момент будут
    /// выданы товары, что делать по «Продолжить» и по «назад». Например, главное меню
    /// по кнопке «играть»: <see cref="ProductUseContext.RunStart"/>, «Продолжить»
    /// начинает заезд, «назад» возвращает в меню. Само окно про заезд
    /// ничего не знает — поэтому его может открыть перед собой и любое другое окно.
    /// </summary>
    public sealed class UserBackPackWindow : MenuPageWindow
    {
        [Header("Панели")]

        [Tooltip("Панель предметов: сетка карточек и строка состояния. Видна вошедшему игроку.")]
        [SerializeField] private GameObject _itemsPanel;

        [Tooltip("Панель входа: призыв войти и кнопка входа. Видна, пока у игрока нет xsollaID.")]
        [SerializeField] private GameObject _signInPanel;

        [Header("Список предметов")]

        [Tooltip("Шаблон карточки предмета (обычно отдельный префаб). Лежит в сцене — держи " +
                 "выключенным: включённый останется на экране лишней карточкой.")]
        [SerializeField] private BackPackItemView _itemTemplate;

        [Tooltip("Куда класть карточки (объект с GridLayoutGroup). Пусто — родитель шаблона.")]
        [SerializeField] private Transform _itemsRoot;

        [Header("Состояние")]

        [Tooltip("Строка вместо списка: загрузка, пусто, ошибка. Необязательна.")]
        [SerializeField] private TMP_Text _statusLabel;

        [SerializeField] private string _loadingText = "LOADING...";

        [Tooltip("Рюкзак пуст — подходящих этой игре предметов в нём нет.")]
        [SerializeField] private string _emptyText = "NO COMPATIBLE ITEMS";

        [SerializeField] private string _errorText = "COULDN'T LOAD YOUR BACKPACK";

        [Header("Вход через Xsolla ID")]

        [Tooltip("Надпись на панели входа: призыв войти, «проверяем аккаунт», причина неудачи.")]
        [SerializeField] private TMP_Text _signInMessage;

        [Tooltip("Кнопка «войти». Видна только гостю и только когда вход вообще доступен.")]
        [SerializeField] private Button _signInButton;

        [Tooltip("Подпись кнопки входа: меняется на «входим…», пока открыто окно входа. Необязательна.")]
        [SerializeField] private TMP_Text _signInLabel;

        [SerializeField] private string _signInText = "SIGN IN";
        [SerializeField] private string _signingInText = "SIGNING IN...";
        [SerializeField] private string _signedOutText = "SIGN IN WITH XSOLLA TO SEE YOUR BACKPACK";
        [SerializeField] private string _checkingText = "CHECKING YOUR ACCOUNT...";
        [SerializeField] private string _signInUnavailableText = "SIGN-IN IS UNAVAILABLE";
        [SerializeField] private string _signInFailedText = "SIGN-IN FAILED. PLEASE TRY AGAIN";
        [SerializeField] private string _popupBlockedText = "POP-UP BLOCKED. ALLOW POP-UPS AND TRY AGAIN";

        [Tooltip("Сколько ждать, пока восстановится прошлая сессия, прежде чем показать гостя.")]
        [Min(1f)] [SerializeField] private float _readyTimeoutSeconds = 12f;

        [Header("Сайт BackPack")]

        [Tooltip("Кнопка «открыть BackPack»: сайт с рюкзаком игрока.")]
        [SerializeField] private Button _openBackPackButton;

        [SerializeField] private string _backPackUrl = "https://backpack.xsolla.com/";

        [Header("Продолжить")]

        [Tooltip("Кнопка «Продолжить»: отдаёт управление тому, кто открыл окно (см. Prepare).")]
        [SerializeField] private Button _continueButton;

        private readonly List<BackPackItemView> _itemViews = new List<BackPackItemView>();

        /// <summary>Выбранные на заезд предметы, в порядке выбора — в нём же и активируются.</summary>
        private readonly List<BackPackRewardItem> _selected = new List<BackPackRewardItem>();

        /// <summary>Под товары одного предмета, чтобы не заводить список на каждую проверку.</summary>
        private readonly List<IBackPackProduct> _productsScratch = new List<IBackPackProduct>();

        /// <summary>Под строки наград одной карточки — карточка разбирает их сразу.</summary>
        private readonly List<BackPackRewardEntry> _rewardsScratch = new List<BackPackRewardEntry>();

        private readonly RemoteSpriteCache _icons = new RemoteSpriteCache();

        /// <summary>
        /// Приносит загрузка игры — окно само про контейнер сервисов не знает. До того
        /// (и если не принесли) — <see cref="NullBackPackService"/>: выключенный BackPack.
        /// </summary>
        private IBackPackService _backPack = NullBackPackService.Instance;

        /// <summary>Вход через Xsolla ID. Приносит загрузка, как и <see cref="_backPack"/>.</summary>
        private IXsollaIdAuth _auth;

        /// <summary>Товары — их id и есть ключи наград. Приносит загрузка.</summary>
        private IBackPackProductCatalog _catalog;

        /// <summary>Идёт ли заезд, когда выдаём товар. Приносит загрузка.</summary>
        private Func<bool> _isRunActive;

        /// <summary>Окно входа открыто и ждёт игрока.</summary>
        private Task<XsollaIdResult> _signInTask;

        /// <summary>Что сказать гостю о последней неудачной попытке входа. Null — ничего.</summary>
        private string _signInError;

        /// <summary>Ждём, пока восстановится прошлая сессия: события об этом у IXsollaIdAuth нет.</summary>
        private Coroutine _readyWatch;
        private bool _readyTimedOut;

        /// <summary>Что делать по «Продолжить» — задаёт открывающее окно в <see cref="Prepare"/>.</summary>
        private Func<Task> _onContinue;

        /// <summary>Куда вернуться по «назад». Не задано — в главное меню, как у любой страницы меню.</summary>
        private Func<Task> _onBack;

        /// <summary>
        /// В какой момент будут выданы выбранные товары — задаёт открывающее окно в
        /// <see cref="Prepare"/>. До этого None: выбрать нельзя ничего.
        /// </summary>
        private ProductUseContext _context = ProductUseContext.None;

        /// <summary>
        /// Номер загрузки списка. Ответ на устаревший запрос (окно успели закрыть и
        /// открыть снова) отбрасываем по нему, иначе старый список лёг бы поверх нового.
        /// </summary>
        private int _loadVersion;

        /// <summary>
        /// Отмена идущей загрузки списка: новая загрузка, закрытие и уничтожение окна
        /// обрывают запрос, а не ждут ответа, который всё равно выбросят.
        /// </summary>
        private CancellationTokenSource _loadCancellation;

        private bool _warnedNoService;

        /// <summary>Выбранные на заезд предметы — активируются в <see cref="ActivateSelectedAsync"/>.</summary>
        public IReadOnlyList<BackPackRewardItem> SelectedItems => _selected;

        /// <summary>Предмет активирован: сервер подтвердил удаление, и его товары выданы.</summary>
        public event Action<BackPackRewardItem> ItemActivated;

        /// <summary>
        /// Отдать окну сервис BackPack-сервера. Зовёт загрузка; без него (или с
        /// выключенным) окно покажет ошибку вместо списка, а «Продолжить» работает как обычно.
        /// </summary>
        public void BindBackPack(IBackPackService backPack)
        {
            _backPack = backPack ?? NullBackPackService.Instance;

            if (IsOpen)
                Forget(ReloadAsync());
        }

        /// <summary>
        /// Отдать окну вход через Xsolla ID. Зовёт загрузка; без него кнопки входа
        /// нет, и гость просто видит, что вход недоступен.
        ///
        /// На вход и выход подписываемся сразу и насовсем: объект окна живёт всю
        /// игру (см. <see cref="WindowBase"/>), и вошедшему игроку открытое окно
        /// должно перечитать рюкзак тут же, не дожидаясь переоткрытия.
        /// </summary>
        public void BindAuth(IXsollaIdAuth auth)
        {
            UnsubscribeAuth();

            _auth = auth;

            if (_auth != null)
            {
                _auth.SignedIn += OnSignedIn;
                _auth.SignedOut += OnSignedOut;
            }

            RefreshSignInButton();

            if (IsOpen)
                Forget(ReloadAsync());
        }

        /// <summary>
        /// Задать, что делать по кнопкам окна. Зовёт открывающее окно до
        /// <c>OpenAsync</c>. Закрывается окно само — после того, как отработает
        /// переданное действие, так что на экране не мелькнёт пустота.
        /// </summary>
        /// <param name="context">
        /// В какой момент будут выданы выбранные товары: предметы с товаром, который
        /// для этого момента не отмечен (<see cref="IBackPackProduct.IsUsableIn"/>), выбрать нельзя.
        /// </param>
        /// <param name="onContinue">«Продолжить»: например, начать заезд.</param>
        /// <param name="onBack">«Назад»: например, снова открыть выбор уровня. Null — в главное меню.</param>
        public void Prepare(ProductUseContext context, Func<Task> onContinue, Func<Task> onBack)
        {
            // Выбор делали под другой момент — в новом он может быть неприменим.
            if (context != _context)
                _selected.Clear();

            _context = context;
            _onContinue = onContinue;
            _onBack = onBack;

            UpdateContinueButton();
            RefreshItemStates();
        }

        /// <summary>
        /// Есть ли связь с BackPack-сервером. Зовёт тот, кто собирается открыть
        /// окно: без связи рюкзак показать нечем, и игрока лучше сразу пустить
        /// дальше. BackPack выключен — связи тоже нет.
        /// </summary>
        public Task<BackPackConnectionResult> CheckConnectionAsync(CancellationToken cancellationToken = default) =>
            _backPack.CheckConnectionAsync(cancellationToken);

        /// <summary>
        /// Отдать окну каталог товаров (их id — ключи наград предметов) и способ узнать,
        /// идёт ли заезд, в который их выдавать. Зовёт загрузка.
        /// </summary>
        public void BindRewards(IBackPackProductCatalog catalog, Func<bool> isRunActive)
        {
            _catalog = catalog;
            _isRunActive = isRunActive;

            RefreshItemRewards();
            RefreshItemStates();
        }

        /// <summary>
        /// Активировать выбранные предметы. Зовёт тот, кто открыл окно, когда заезд
        /// идёт: СРАЗУ ПОСЛЕ его старта (до старта заезда нет, и проверять
        /// применимость не у чего, а сам старт сбросил бы выданное).
        /// Возвращает, сколько предметов активировано.
        ///
        /// По каждому предмету, по очереди (выданный щит меняет применимость
        /// следующего):
        /// <list type="number">
        /// <item>товар сейчас неприменим — предмет НЕ тратим, он остаётся в рюкзаке;</item>
        /// <item>удаляем предмет на сервере — по id экземпляра: по sku сервер снёс бы
        /// все предметы этого типа;</item>
        /// <item>товар выдаём, только если сервер ПОДТВЕРДИЛ удаление именно этого
        /// экземпляра (outcome "item_deleted" в отчёте, а не просто 200). Нет связи,
        /// ошибка, таймаут — не выдаём, предмет остаётся.</item>
        /// </list>
        /// Выбор при этом забирается из окна сразу: второй раз тот же предмет не
        /// пойдёт на сервер, даже если окно откроют, пока идут запросы.
        /// </summary>
        public async Task<int> ActivateSelectedAsync()
        {
            if (_selected.Count == 0) return 0;

            var items = _selected.ToArray();
            _selected.Clear();
            RefreshItemStates();

            if (_catalog == null || _isRunActive == null || !_backPack.IsAvailable)
            {
                Debug.LogError(
                    $"[BackPack] {items.Length} выбранных предметов не активировать: окну не принесли " +
                    "каталог товаров, заезд (BindRewards) или BackPack выключен. Предметы остаются в рюкзаке.", this);
                return 0;
            }

            var activated = 0;
            var products = new List<IBackPackProduct>();

            foreach (var item in items)
            {
                if (!TryResolveProducts(item, products))
                {
                    Debug.LogWarning(
                        $"[BackPack] предмет «{item.Sku}»: награды [{Describe(item.Rewards)}] нет среди " +
                        "товаров каталога — не тратим, предмет остаётся в рюкзаке.", this);
                    continue;
                }

                // Проверяем ДО удаления: неприменимый товар — не повод тратить предмет.
                if (!CanApplyAll(products, out var blocked))
                {
                    Debug.LogWarning(
                        $"[BackPack] предмет «{item.Sku}»: товар «{blocked.Id}» сейчас неприменим — " +
                        "не тратим, предмет остаётся в рюкзаке.", this);
                    continue;
                }

                // Без отмены: оборванный DELETE мог уже удалить предмет на сервере,
                // и тогда игрок остался бы и без предмета, и без товара.
                var deleted = await _backPack.DeleteItemAsync(item.Id);

                if (this == null) return activated;

                if (!deleted.IsDeleted)
                {
                    // «Уже не было» — не наша трата: предмет потратили в другом месте
                    // (вторая вкладка, другое устройство), и выдать за него товар — подарить его.
                    Debug.LogWarning(
                        deleted.Outcome == BackPackDeleteResult.OutcomeAlreadyGone
                            ? $"[BackPack] предмета «{item.Sku}» ({item.Id}) уже нет в рюкзаке — товар не выдаём."
                            : $"[BackPack] предмет «{item.Sku}» ({item.Id}) не удалён на сервере ({deleted.Error}) — " +
                              "товар не выдаём, предмет остаётся в рюкзаке.",
                        this);
                    continue;
                }

                // Удалён точно — выдаём. Применимость не перепроверяем: предмет уже
                // потрачен, и за доли секунды ответа отнять у игрока его товар нельзя.
                if (!_isRunActive())
                {
                    Debug.LogError(
                        $"[BackPack] предмет «{item.Sku}» удалён, но заезд закончился раньше, чем " +
                        "ответил сервер, — товар выдать некуда.", this);
                    continue;
                }

                foreach (var product in products)
                    product.Apply();

                activated++;
                Debug.Log(
                    $"[BackPack] предмет «{item.Sku}» активирован: удалён на сервере, выдано " +
                    $"[{Describe(item.Rewards)}].", this);
                ItemActivated?.Invoke(item);
            }

            return activated;
        }

        public override async Task OpenAsync()
        {
            await base.OpenAsync();

            // Не ждём: окно появляется сразу со строкой «загрузка», список дорисуется по ответу.
            Forget(ReloadAsync());
        }

        public override Task CloseAsync()
        {
            CancelLoad();
            return base.CloseAsync();
        }

        /// <summary>«Назад» ведёт туда, откуда пришли (см. <see cref="Prepare"/>), а не сразу в меню.</summary>
        public override async Task BackAsync()
        {
            if (_onBack == null)
            {
                await base.BackAsync();
                return;
            }

            await _onBack();
            await CloseSelfAsync();
        }

        protected override void Awake()
        {
            base.Awake();

            if (_continueButton != null)
                _continueButton.onClick.AddListener(OnContinue);

            if (_signInButton != null)
                _signInButton.onClick.AddListener(OnSignIn);

            if (_openBackPackButton != null)
                _openBackPackButton.onClick.AddListener(OnOpenBackPack);

            ResolveTemplate();
            UpdateContinueButton();
            RefreshSignInButton();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (_continueButton != null)
                _continueButton.onClick.RemoveListener(OnContinue);

            if (_signInButton != null)
                _signInButton.onClick.RemoveListener(OnSignIn);

            if (_openBackPackButton != null)
                _openBackPackButton.onClick.RemoveListener(OnOpenBackPack);

            CancelLoad();
            UnsubscribeAuth();
            _icons.Clear();
        }

        // =========================================================
        // СПИСОК
        // =========================================================

        /// <summary>Перечитать рюкзак с сервера и перестроить карточки.</summary>
        private async Task ReloadAsync()
        {
            var version = ++_loadVersion;

            CancelLoad();

            ClearItems();
            RefreshSignInButton();

            // Выключенный BackPack — не повод звать гостя входить: и после входа
            // показать будет нечего.
            if (!_backPack.IsAvailable)
            {
                if (!_warnedNoService)
                {
                    _warnedNoService = true;
                    Debug.LogWarning(
                        $"[BackPack] рюкзак выключен ({_backPack}): конфиг не заполнен или BackPackTask " +
                        "не отработал — показать нечего.", this);
                }

                ShowPanel(signedIn: true);
                ShowStatus(_errorText);
                return;
            }

            // Гость — не поломка: рюкзак есть только у вошедшего игрока. Вместо
            // предметов — панель входа.
            if (!_backPack.HasXsollaId)
            {
                // Выбор принадлежал вышедшему игроку — следующему он не достаётся.
                _selected.Clear();

                ShowPanel(signedIn: false);
                ShowSignInMessage(GuestStatus());
                StartReadyWatch();
                return;
            }

            ShowPanel(signedIn: true);
            ShowStatus(_loadingText);

            _loadCancellation = new CancellationTokenSource();

            BackPackRewardsReply reply;

            try
            {
                reply = await _backPack.GetRewardsAsync(_loadCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // Загрузку оборвали намеренно (новая загрузка, закрытие) — сказать нечего.
                return;
            }

            // Пока шёл запрос, окно закрыли, переоткрыли или уничтожили — этот ответ уже никому не нужен.
            if (this == null || version != _loadVersion || !IsOpen) return;

            if (!reply.Success)
            {
                // Списка на экране нет — и активировать игроку нечего: незаметный
                // выбор, ушедший на сервер после старта, стал бы для него сюрпризом.
                _selected.Clear();

                Debug.LogWarning($"[BackPack] рюкзак не загрузился: {reply}", this);
                ShowStatus(_errorText);
                return;
            }

            KeepSelectionOf(reply.Items);
            BuildItems(reply.Items);
            RefreshItemRewards();
            RefreshItemStates();
            ShowStatus(_itemViews.Count == 0 ? _emptyText : null);
        }

        private void CancelLoad()
        {
            if (_loadCancellation == null) return;

            _loadCancellation.Cancel();
            _loadCancellation.Dispose();
            _loadCancellation = null;
        }

        private void BuildItems(IReadOnlyList<BackPackRewardItem> items)
        {
            var template = ResolveTemplate();
            if (template == null) return;

            var parent = _itemsRoot != null ? _itemsRoot : template.transform.parent;

            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.Sku))
                {
                    Debug.LogError($"[BackPack] у предмета «{item.Id}» в ответе сервера нет sku — " +
                                   "активировать его не получится, в список он не попадёт.", this);
                    continue;
                }

                var view = Instantiate(template, parent);
                view.gameObject.name = item.Sku;
                view.gameObject.SetActive(true);
                view.Show(item);

                if (view.ActivateButton != null)
                    view.ActivateButton.onClick.AddListener(() => OnActivate(view));
                else
                    Debug.LogError($"[BackPack] у карточки «{item.Sku}» нет кнопки «активировать» — " +
                                   "активировать предмет не получится.", this);

                _itemViews.Add(view);
                Forget(LoadIconAsync(view, item.IconUrl));
            }
        }

        private async Task LoadIconAsync(BackPackItemView view, string url)
        {
            var sprite = await _icons.GetAsync(url);

            // Пока качали, список могли перестроить — карточки уже нет.
            if (view != null && sprite != null)
                view.SetIcon(sprite);
        }

        private void ClearItems()
        {
            foreach (var view in _itemViews)
            {
                if (view != null)
                    Destroy(view.gameObject);
            }

            _itemViews.Clear();
        }

        /// <summary>
        /// Шаблон карточки. В сцене его гасим: включённым он остался бы на экране
        /// лишней карточкой. Шаблон-префаб (не на сцене) не трогаем — это ассет.
        /// </summary>
        private BackPackItemView ResolveTemplate()
        {
            if (_itemTemplate == null)
            {
                Debug.LogError(
                    "[BackPack] рюкзаку не задан шаблон карточки (Item Template) — " +
                    "список останется пустым.", this);
                return null;
            }

            if (_itemTemplate.gameObject.scene.IsValid() && _itemTemplate.gameObject.activeSelf)
                _itemTemplate.gameObject.SetActive(false);

            return _itemTemplate;
        }

        /// <summary>Ровно одна панель на экране: предметы вошедшему, вход — гостю.</summary>
        private void ShowPanel(bool signedIn)
        {
            if (_itemsPanel != null)
                _itemsPanel.SetActive(signedIn);

            if (_signInPanel != null)
                _signInPanel.SetActive(!signedIn);
        }

        private void ShowSignInMessage(string text)
        {
            if (_signInMessage != null)
                _signInMessage.text = text ?? "";
        }

        /// <summary>Строка состояния вместо списка. Пусто — строку прячем, виден список.</summary>
        private void ShowStatus(string text)
        {
            if (_statusLabel == null) return;

            var visible = !string.IsNullOrEmpty(text);
            _statusLabel.gameObject.SetActive(visible);

            if (visible)
                _statusLabel.text = text;
        }

        // =========================================================
        // ВЫБОР НА ЗАЕЗД
        // =========================================================

        /// <summary>«Активировать» — выбрать предмет на заезд или снять выбор.</summary>
        private void OnActivate(BackPackItemView view)
        {
            var item = view != null ? view.Item : null;
            if (item == null) return;

            var index = IndexOfSelected(item);

            if (index >= 0)
                _selected.RemoveAt(index);
            else if (StateOf(item) == BackPackItemState.Available)
                _selected.Add(item);

            RefreshItemStates();
        }

        /// <summary>Перерисовать все карточки: выбор одной меняет доступность других (лимит товара).</summary>
        private void RefreshItemStates()
        {
            foreach (var view in _itemViews)
            {
                if (view != null && view.Item != null)
                    view.SetState(StateOf(view.Item));
            }
        }

        /// <summary>
        /// Показать на карточках, что игрок получит при активации. Иконки и подписи
        /// знает только каталог товаров, а он может прийти позже списка — поэтому зовём
        /// и из <see cref="BindRewards"/>.
        /// </summary>
        private void RefreshItemRewards()
        {
            foreach (var view in _itemViews)
            {
                if (view != null && view.Item != null)
                    view.SetRewards(RewardEntries(view.Item));
            }
        }

        /// <summary>
        /// Награды предмета строками для карточки: строки товара из каталога
        /// (<see cref="IBackPackProduct.Rewards"/>, «+$100»). Повтор товара —
        /// «x2» к каждой его строке. Эффектам нечего показать — строка с названием
        /// товара, чтобы награда не пропала с карточки молча. Ключи, которых игра
        /// не знает, пропускаем: показать их нечем, а выбрать такой предмет и так
        /// нельзя (см. <see cref="StateOf"/>).
        ///
        /// Список общий на все карточки: карточка разбирает его сразу, не храня.
        /// </summary>
        private IReadOnlyList<BackPackRewardEntry> RewardEntries(BackPackRewardItem item)
        {
            _rewardsScratch.Clear();

            if (_catalog == null || item.Rewards == null) return _rewardsScratch;

            _productsScratch.Clear();

            foreach (var key in item.Rewards)
            {
                if (_catalog.TryGetProduct(key, out var product))
                    _productsScratch.Add(product);
            }

            for (var i = 0; i < _productsScratch.Count; i++)
            {
                var product = _productsScratch[i];

                // Повтор уже посчитан на первом вхождении.
                if (_productsScratch.IndexOf(product) < i) continue;

                var count = CountOf(_productsScratch, product);
                var suffix = count > 1 ? " x" + count : "";
                var added = false;

                if (product.Rewards != null)
                {
                    foreach (var line in product.Rewards)
                    {
                        if (line.Icon == null && string.IsNullOrEmpty(line.Text))
                            continue;

                        _rewardsScratch.Add(new BackPackRewardEntry(line.Icon, line.Text + suffix));
                        added = true;
                    }
                }

                if (!added)
                {
                    var title = string.IsNullOrEmpty(product.Title) ? product.Id : product.Title;
                    _rewardsScratch.Add(new BackPackRewardEntry(null, title + suffix));
                }
            }

            return _rewardsScratch;
        }

        private BackPackItemState StateOf(BackPackRewardItem item)
        {
            if (IndexOfSelected(item) >= 0)
                return BackPackItemState.Selected;

            // Без id экземпляра потратить ровно этот предмет нельзя: по sku сервер
            // удалил бы все предметы этого типа.
            if (string.IsNullOrEmpty(item.Id))
                return BackPackItemState.Unavailable;

            if (!TryResolveProducts(item, _productsScratch))
                return BackPackItemState.Unavailable;

            // Хоть один товар не годится для этого момента — при выдаче предмет
            // оказался бы неприменим.
            foreach (var product in _productsScratch)
            {
                if (!product.IsUsableIn(_context))
                    return BackPackItemState.Unavailable;
            }

            // Уже выбранное плюс то, что даст этот предмет, — не больше, чем товара
            // можно держать разом (Max). А если заезд уже идёт, то и не больше, чем
            // он примет прямо сейчас. Лишний предмет при выдаче оказался бы неприменим.
            var runActive = _isRunActive != null && _isRunActive();

            for (var i = 0; i < _productsScratch.Count; i++)
            {
                var product = _productsScratch[i];

                // Повтор уже посчитан на первом вхождении.
                if (_productsScratch.IndexOf(product) < i) continue;

                var wanted = CountSelected(product.Id) + CountOf(_productsScratch, product);
                var limit = Mathf.Max(1, product.Max);

                if (runActive)
                    limit = Mathf.Min(limit, product.RemainingUses());

                if (wanted > limit)
                    return BackPackItemState.Unavailable;
            }

            return BackPackItemState.Available;
        }

        /// <summary>
        /// Товары, которые выдаёт предмет, — по одному на каждый ключ его наград.
        /// Хотя бы один ключ игре неизвестен — false: выдать предмет наполовину
        /// и потратить целиком нельзя.
        /// </summary>
        private bool TryResolveProducts(BackPackRewardItem item, List<IBackPackProduct> products)
        {
            products.Clear();

            if (_catalog == null || item.Rewards == null || item.Rewards.Length == 0)
                return false;

            foreach (var key in item.Rewards)
            {
                if (!_catalog.TryGetProduct(key, out var product))
                {
                    products.Clear();
                    return false;
                }

                products.Add(product);
            }

            return true;
        }

        private bool CanApplyAll(List<IBackPackProduct> products, out IBackPackProduct blocked)
        {
            foreach (var product in products)
            {
                if (!product.CanApply())
                {
                    blocked = product;
                    return false;
                }
            }

            blocked = null;
            return true;
        }

        /// <summary>Сколько раз товар встречается в списке — повтор ключа в наградах одного предмета.</summary>
        private static int CountOf(List<IBackPackProduct> products, IBackPackProduct product)
        {
            var count = 0;

            foreach (var other in products)
            {
                if (other == product)
                    count++;
            }

            return count;
        }

        /// <summary>Сколько раз товар встречается в наградах уже выбранных предметов.</summary>
        private int CountSelected(string productId)
        {
            var count = 0;

            foreach (var selected in _selected)
            {
                if (selected.Rewards == null) continue;

                foreach (var key in selected.Rewards)
                {
                    if (string.Equals(key?.Trim(), productId, StringComparison.Ordinal))
                        count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Выбор переживает перечитывание рюкзака: объекты предметов приходят новые,
        /// поэтому сверяем по id экземпляра. Чего в рюкзаке больше нет — из выбора уходит.
        /// </summary>
        private void KeepSelectionOf(IReadOnlyList<BackPackRewardItem> items)
        {
            for (var i = _selected.Count - 1; i >= 0; i--)
            {
                var fresh = FindSame(items, _selected[i]);

                if (fresh == null)
                    _selected.RemoveAt(i);
                else
                    _selected[i] = fresh;
            }
        }

        private int IndexOfSelected(BackPackRewardItem item)
        {
            for (var i = 0; i < _selected.Count; i++)
            {
                if (IsSame(_selected[i], item))
                    return i;
            }

            return -1;
        }

        private static BackPackRewardItem FindSame(IReadOnlyList<BackPackRewardItem> items, BackPackRewardItem wanted)
        {
            foreach (var item in items)
            {
                if (IsSame(item, wanted))
                    return item;
            }

            return null;
        }

        /// <summary>Тот же экземпляр в рюкзаке: по id, а без id — тот же объект.</summary>
        private static bool IsSame(BackPackRewardItem a, BackPackRewardItem b) =>
            ReferenceEquals(a, b) ||
            (!string.IsNullOrEmpty(a.Id) && string.Equals(a.Id, b.Id, StringComparison.Ordinal));

        private static string Describe(string[] keys) =>
            keys == null || keys.Length == 0 ? "" : string.Join(", ", keys);

        // =========================================================
        // ВХОД ЧЕРЕЗ XSOLLA ID
        // =========================================================

        /// <summary>Что показать гостю вместо списка.</summary>
        private string GuestStatus()
        {
            if (_auth == null || !_auth.IsAvailable)
                return _signInUnavailableText;

            // Прошлая сессия ещё восстанавливается: объявлять игрока гостем рано.
            if (!_auth.IsReady && !_readyTimedOut)
                return _checkingText;

            return string.IsNullOrEmpty(_signInError) ? _signedOutText : _signInError;
        }

        /// <summary>
        /// Кнопка входа видна только гостю, когда вход доступен и прошлая сессия
        /// уже не восстанавливается, — иначе игрок нажал бы «войти», будучи уже
        /// почти вошедшим.
        /// </summary>
        private void RefreshSignInButton()
        {
            var pending = _signInTask != null && !_signInTask.IsCompleted;

            if (_signInButton != null)
            {
                var visible = _auth != null && _auth.IsAvailable && !_auth.IsSignedIn &&
                              (_auth.IsReady || _readyTimedOut);

                _signInButton.gameObject.SetActive(visible);
                _signInButton.interactable = !pending;
            }

            if (_signInLabel != null)
                _signInLabel.text = pending ? _signingInText : _signInText;
        }

        /// <summary>
        /// ВНИМАНИЕ: SignInAsync зовём СИНХРОННО, прямо из нажатия, без единого
        /// await до него — иначе браузер сочтёт окно входа непрошеным и
        /// заблокирует (как в XsollaIdAccountWidget).
        /// </summary>
        private void OnSignIn()
        {
            if (_auth == null || !_auth.IsAvailable) return;

            // Окно входа уже открыто — второе не нужно.
            if (_signInTask != null && !_signInTask.IsCompleted) return;

            var task = _auth.SignInAsync();

            _signInTask = task;
            _signInError = null;
            RefreshSignInButton();

            Forget(WatchSignInAsync(task));
        }

        private async Task WatchSignInAsync(Task<XsollaIdResult> task)
        {
            XsollaIdResult result;

            // Из IXsollaIdAuth исключения не летят, но задача, сорванная чем-то
            // посторонним, не должна оставить кнопку в «входим…» навсегда.
            try
            {
                result = await task;
            }
            catch (Exception exception)
            {
                result = new XsollaIdResult.Failure(XsollaIdErrorCode.Network, exception.Message);
            }

            if (this == null) return;

            if (_signInTask == task)
                _signInTask = null;

            RefreshSignInButton();

            // Удачный вход перечитает рюкзак сам — через событие SignedIn.
            if (!(result is XsollaIdResult.Failure failure)) return;

            Debug.LogWarning($"[BackPack] вход через Xsolla ID не состоялся — {failure.Code}: {failure.Message}", this);

            _signInError = SignInErrorText(failure.Code);

            if (IsOpen && !_backPack.HasXsollaId)
                ShowSignInMessage(GuestStatus());
        }

        /// <summary>
        /// Игрок сам закрыл окно входа — это не ошибка, жаловаться не на что.
        /// Остальное игроку неотличимо: вход не состоялся, стоит повторить.
        /// </summary>
        private string SignInErrorText(XsollaIdErrorCode code)
        {
            switch (code)
            {
                case XsollaIdErrorCode.Cancelled:
                case XsollaIdErrorCode.SessionExpired:
                    return null;

                case XsollaIdErrorCode.PopupBlocked:
                    return _popupBlockedText;

                default:
                    return _signInFailedText;
            }
        }

        /// <summary>
        /// Игрок вошёл — здесь или в блоке аккаунта главного меню. xsollaID для
        /// BackPack к этому моменту уже записан: XsollaIdTask подписался на это
        /// событие раньше окна и записывает его в BackPackIdentity первым.
        /// </summary>
        private void OnSignedIn(XsollaIdSession session)
        {
            _signInError = null;
            RefreshSignInButton();

            if (IsOpen)
                Forget(ReloadAsync());
        }

        /// <summary>Игрок вышел: чужой рюкзак на экране оставлять нельзя.</summary>
        private void OnSignedOut()
        {
            RefreshSignInButton();

            if (IsOpen)
                Forget(ReloadAsync());
        }

        private void UnsubscribeAuth()
        {
            if (_auth == null) return;

            _auth.SignedIn -= OnSignedIn;
            _auth.SignedOut -= OnSignedOut;
        }

        /// <summary>
        /// У IsReady нет события, а восстановление, которому нечего было
        /// восстанавливать, не пришлёт и SignedOut, — поэтому, как и блок
        /// аккаунта, опрашиваем, пока ответ не станет окончательным.
        /// </summary>
        private void StartReadyWatch()
        {
            if (_readyWatch != null || _readyTimedOut || !isActiveAndEnabled) return;
            if (_auth == null || !_auth.IsAvailable || _auth.IsReady) return;

            _readyWatch = StartCoroutine(WatchReady());
        }

        private IEnumerator WatchReady()
        {
            // Реальное время: меню может жить при замороженном игровом.
            var deadline = Time.unscaledTime + _readyTimeoutSeconds;

            while (!_auth.IsReady)
            {
                if (Time.unscaledTime >= deadline)
                {
                    _readyTimedOut = true;
                    Debug.LogWarning(
                        $"[BackPack] сессия Xsolla ID не восстановилась за {_readyTimeoutSeconds:0} с — " +
                        "показываем гостя.", this);
                    break;
                }

                yield return new WaitForSecondsRealtime(0.2f);
            }

            _readyWatch = null;

            RefreshSignInButton();

            if (IsOpen)
                Forget(ReloadAsync());
        }

        // =========================================================
        // САЙТ BACKPACK
        // =========================================================

        /// <summary>
        /// Открываем прямо из нажатия: в WebGL браузер разрешает новую вкладку
        /// только как непосредственную реакцию на жест игрока.
        /// </summary>
        private void OnOpenBackPack()
        {
            if (!string.IsNullOrEmpty(_backPackUrl))
                Application.OpenURL(_backPackUrl);
        }

        // =========================================================
        // ПРОДОЛЖИТЬ
        // =========================================================

        /// <summary>Без действия «Продолжить» жать нечего — окно открыли, не позвав <see cref="Prepare"/>.</summary>
        private void UpdateContinueButton()
        {
            if (_continueButton != null)
                _continueButton.interactable = _onContinue != null;
        }

        private void OnContinue() => Forget(ContinueAsync());

        private async Task ContinueAsync()
        {
            if (_onContinue == null)
            {
                Debug.LogError(
                    "[BackPack] рюкзаку не сказали, что делать по «Продолжить» — " +
                    "открывающее окно должно позвать Prepare до OpenAsync.", this);
                return;
            }

            await _onContinue();
            await CloseSelfAsync();
        }
    }
}
