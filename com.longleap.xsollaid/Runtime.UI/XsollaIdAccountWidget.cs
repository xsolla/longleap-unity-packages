using System.Collections;
using System.Threading.Tasks;
using LongLeap.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.XsollaId.UI
{
    // Блок аккаунта Xsolla ID в правом верхнем углу главного меню. Один префаб,
    // три состояния-ребёнка: «проверяем», «войти», «вошёл».
    //
    // МОДАЛЬНОСТЬ НЕ ТРОГАЕТ. Это не окно: ScreenService.TryOpen/Close здесь нет
    // и быть не должно — заняв гейт, виджет не дал бы открыться самому меню.
    // Образец для такого «всегда включённого» элемента — CoinBalanceLabel:
    // подписка в OnEnable, отписка в OnDisable, перерисовка на каждом включении
    // (пока объект был выключен, события до нас не доходили).
    //
    // Ни одного #if: доступность спрашиваем у IXsollaIdAuth.IsAvailable, а
    // картинку — у IXsollaIdAvatarLoader, у обоих есть заглушки. Видимость
    // ссылки на BackPack — тоже: она приходит флагом в Bind, потому что зависит
    // от дефайнов КОНКРЕТНОЙ игры, про которые пакет не знает и знать не должен.
    //
    // Зависимости приходят через Bind и никак иначе. Виджет НИЧЕГО не ищет сам:
    // пакет не знает и не должен знать про локатор сервисов конкретной игры.
    // Тот же приём, что у LongLeap.Settings.Controls.SettingsToggle.Bind.
    //
    // Bind можно звать в любой момент, в том числе до первого OnEnable и
    // повторно: до него виджет показывает «проверяем», а не гостя.
    [DisallowMultipleComponent]
    public sealed class XsollaIdAccountWidget : MonoBehaviour
    {
        // Ключи статических подписей. Держим КЛЮЧИ, а не готовые строки: при
        // смене языка перерисовываемся сами (Docs/Localization.md).
        private const string KeySignIn = "btn_xsolla_sign_in";
        private const string KeySigningIn = "btn_xsolla_signing_in";
        private const string KeyNameUnknown = "xsolla_name_unknown";
        private const string BackPackUrl = "https://backpack.xsolla.com/";

        [Header("Состояния")]
        [Tooltip("Три взаимоисключающих ребёнка. Все три выключены — блока нет " +
            "вовсе (вход недоступен). САМ объект виджета выключать нельзя: " +
            "выключенный компонент себя обратно не включит и сервис не переспросит.")]
        [SerializeField] private GameObject checkingRoot;
        [SerializeField] private GameObject signedOutRoot;
        [SerializeField] private GameObject signedInRoot;

        [Header("Вход")]
        [SerializeField] private Button signInButton;

        [Tooltip("Подпись кнопки входа. Нужен именно LocalizedText, а не " +
            "TMP_Text: ключ переключается кодом на «входим…», и компонент сам " +
            "переведёт заново при смене языка.")]
        [SerializeField] private LongLeap.Localization.LocalizedText signInLabel;

        [Tooltip("Строка причины неудачи. Пусто — причину покажем только в логе.")]
        [SerializeField] private TMP_Text errorLabel;

        [Header("Вошёл")]
        [SerializeField] private Image avatarImage;

        [Tooltip("Заглушка на всё время, пока картинки нет: не загрузилась, " +
            "не отдалась CDN или клейма picture у аккаунта не оказалось.")]
        [SerializeField] private Sprite avatarPlaceholder;

        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private Button logOutButton;

        [Tooltip("Ссылка на рюкзак Xsolla. В префабе лежит РЯДОМ с тремя " +
            "состояниями-детьми, а не внутри них, поэтому её видимость " +
            "переключается отдельно — флагом в Bind.")]
        [SerializeField] private Button openBackpackUrl;

        [Tooltip("Свёрнутая строка: логотип Xsolla и аватар. Нажатие раскрывает " +
            "карточку и складывает её обратно.")]
        [SerializeField] private Button expandButton;

        [Tooltip("Карточка с именем, username и «выйти». Выключена, пока по " +
            "свёрнутой строке не нажали.")]
        [SerializeField] private GameObject expandedRoot;

        [Tooltip("Строка username под именем. Клейма username может не быть — " +
            "тогда строка прячется целиком, а не остаётся пустой.")]
        [SerializeField] private TMP_Text userNameLabel;

        [Header("Настройки")]
        [Tooltip("Как часто опрашивать IsReady. События у него НЕТ, а бутстрап " +
            "выбрасывает задачу RestoreAsync — опрос это единственный способ " +
            "узнать, что ответ окончательный.")]
        [Min(0.02f)] [SerializeField] private float readyPollSeconds = 0.2f;

        [Tooltip("Сколько всего ждать окончательного ответа, прежде чем показать " +
            "гостя. Восстановление ходит в сеть; вечное «проверяем» игрок не " +
            "заслужил.")]
        [Min(1f)] [SerializeField] private float readyTimeoutSeconds = 12f;

        [Tooltip("Сколько держать строку ошибки. 0 — до следующего нажатия.")]
        [Min(0f)] [SerializeField] private float errorHoldSeconds = 8f;

        [Tooltip("Максимум символов в имени. Длиннее — обрезаем с многоточием, " +
            "иначе длинный e-mail растянет строку через полэкрана.")]
        [Min(4)] [SerializeField] private int nameMaxChars = 18;

        // Отданное через Bind. Переживает выключение объекта.
        private IXsollaIdAuth _boundAuth;
        private IXsollaIdAvatarLoader _boundAvatars;

        // Показывать ли ссылку на рюкзак. По умолчанию ДА: так виджет, которому
        // Bind ещё не позвали, и превью в редакторе выглядят как в сборке, где
        // ссылка включена. Игра, которой она не нужна, передаёт false явно.
        private bool _showBackPackLink = true;

        // На что подписаны прямо сейчас. null — подписки нет.
        private IXsollaIdAuth _account;
        private IXsollaIdAvatarLoader _avatars;
        private Task<XsollaIdResult> _signInTask;
        private Coroutine _readyWatch;
        private Coroutine _signInWatch;
        private Coroutine _signOutWatch;
        private Coroutine _errorTimer;
        private bool _readyTimedOut;
        private bool _signOutPending;

        // Раскрыта ли карточка аккаунта. Живёт только в памяти и намеренно НЕ
        // сохраняется: развёрнутая карточка — это ответ на нажатие «прямо
        // сейчас», а не настройка. Открытой после перезагрузки страницы она
        // выглядела бы застрявшей.
        private bool _expanded;

        // КЛЮЧ, не готовая строка: иначе при смене языка на экране осталась бы
        // прошлая локализация.
        private string _errorKey;

        // Загруженный аватар. Спрайт ОБЩИЙ и принадлежит загрузчику — не
        // уничтожаем и не меняем, см. IXsollaIdAvatarLoader.
        private Sprite _avatar;
        private AvatarUrl _avatarUrl;

        void Awake()
        {
            if (checkingRoot == null || signedOutRoot == null || signedInRoot == null)
            {
                Debug.LogError(
                    "XsollaIdAccountWidget: the state objects are not assigned " +
                    "(checkingRoot/signedOutRoot/signedInRoot) — nothing to draw.", this);
            }

            if (signInButton == null)
                Debug.LogError("XsollaIdAccountWidget: the sign-in button is not assigned.", this);

            if (logOutButton == null)
                Debug.LogError("XsollaIdAccountWidget: the sign-out button is not assigned.", this);

            if (openBackpackUrl == null)
                Debug.LogError("XsollaIdAccountWidget: the BackPack button is not assigned.", this);

            // Дублированный префаб: ссылка может указывать на объект чужой копии.
            if (signInLabel != null && !signInLabel.transform.IsChildOf(transform))
            {
                Debug.LogError(
                    "XsollaIdAccountWidget: signInLabel points outside the widget — " +
                    "the prefab was probably duplicated.", this);
            }

            if (expandButton == null)
                Debug.LogError("XsollaIdAccountWidget: the expand button is not assigned.", this);

            if (expandedRoot == null)
            {
                Debug.LogError(
                    "XsollaIdAccountWidget: expandedRoot is not assigned — nothing to expand, " +
                    "and nowhere to show the name and username.", this);
            }

            // Клики вешаем здесь и один раз, с предварительным RemoveListener —
            // как MainMenuScreen.Wire, чтобы повторное включение объекта не
            // наплодило дублей.
            Wire(signInButton, OnSignInClicked);
            Wire(logOutButton, OnLogOutClicked);
            Wire(openBackpackUrl, OnOpenBackPackClicked);
            Wire(expandButton, OnExpandClicked);
        }

        void OnEnable()
        {
            // Имя и строку ошибки ставим кодом, значит подписка на смену языка
            // обязательна — и парная отписка тоже.
            Loc.OnLanguageChanged += Apply;

            SubscribeAccount();
            RefreshAvatar();
            Apply();

            StartReadyWatch();
        }

        void OnDisable()
        {
            Loc.OnLanguageChanged -= Apply;

            UnbindAccount();

            StopWatch(ref _readyWatch);
            StopWatch(ref _signInWatch);
            StopWatch(ref _signOutWatch);
            StopWatch(ref _errorTimer);

            // Задачу входа НЕ отменяем — отмены у SignInAsync нет. Ссылку
            // бросаем: при повторном включении Apply увидит IsSignedIn, а
            // повторный клик по контракту вернёт ТУ ЖЕ задачу и второго окна не
            // откроет.
            _signInTask = null;
            _signOutPending = false;

            // Меню закрыли — складываем карточку. Иначе она встретит игрока
            // раскрытой при следующем открытии, хотя он её не открывал.
            _expanded = false;
        }

        // Страховка: при выгрузке сцены OnDisable может не прийти.
        void OnDestroy() => UnbindAccount();

        // Отдать виджету вход и загрузчик картинок.
        //
        // Звать можно когда угодно: и до того, как объект впервые включится, и
        // после. До вызова виджет честно показывает «проверяем» — объявлять
        // гостем игрока, про которого мы ещё ничего не знаем, нельзя.
        //
        // showBackPackLink — показывать ли кнопку рюкзака. Решает игра: рюкзак —
        // отдельный продукт Xsolla, и в сборке, которая с ним не работает,
        // ссылка ведёт игрока в пустоту. Дефайном здесь это не решить: пакет
        // уезжает в другие проекты, где такого дефайна нет вовсе.
        public void Bind(
            IXsollaIdAuth auth, IXsollaIdAvatarLoader avatars, bool showBackPackLink = true)
        {
            _boundAuth = auth;
            _boundAvatars = avatars;
            _showBackPackLink = showBackPackLink;

            if (!isActiveAndEnabled)
                return;

            SubscribeAccount();
            RefreshAvatar();
            Apply();
            StartReadyWatch();
        }

        void SubscribeAccount()
        {
            if (_account != null)
                return;

            // Не ошибка: владелец сцены мог ещё не позвать Bind — сторожевая
            // корутина дождётся и подпишется тогда же.
            if (_boundAuth == null)
                return;

            _account = _boundAuth;
            _avatars = _boundAvatars;

            _account.SignedIn += OnSignedIn;
            _account.SignedOut += OnSignedOut;
            _account.SessionUpdated += OnSessionUpdated;
        }

        void UnbindAccount()
        {
            if (_account == null)
                return;

            _account.SignedIn -= OnSignedIn;
            _account.SignedOut -= OnSignedOut;
            _account.SessionUpdated -= OnSessionUpdated;

            // Обнуляем только подписку. Переданное в Bind не трогаем: объект
            // могут выключить и включить снова, а второй раз Bind никто не
            // позовёт.
            _account = null;
        }

        void OnSignedIn(XsollaIdSession session)
        {
            _errorKey = null;
            StopWatch(ref _readyWatch);
            RefreshAvatar();
            Apply();
        }

        void OnSignedOut()
        {
            // Чужое лицо и чужое имя после выхода — самый заметный баг такого
            // блока.
            _avatar = null;
            _avatarUrl = AvatarUrl.None;
            _signOutPending = false;

            // Карточка принадлежала вышедшему игроку: оставить её раскрытой
            // означало бы показать следующему чужие имя и username.
            _expanded = false;

            Apply();
        }

        // Обновился токен либо догрузился профиль: игрок тот же, а объект сессии
        // новый. Картинка могла появиться только сейчас.
        void OnSessionUpdated(XsollaIdSession session)
        {
            RefreshAvatar();
            Apply();
        }

        // ---- ожидание окончательного ответа ----

        void StartReadyWatch()
        {
            if (_readyWatch != null)
                return;

            // Ответ уже окончательный — ждать нечего.
            if (_account != null && (!_account.IsAvailable || _account.IsReady))
                return;

            _readyWatch = StartCoroutine(WatchReady());
        }

        // У IsReady нет события: RestoreAsync выставляет его в finally, а
        // бутстрап саму задачу ВЫБРАСЫВАЕТ (#pragma warning disable CS4014) —
        // ручки, чтобы её дождаться, ни у кого нет. Вдобавок если восстанавливать
        // было нечего, RestoreAsync вернёт Expired и SignedOut не выстрелит
        // вовсе. Поэтому опрос — единственный способ узнать, что «гость» это
        // окончательный ответ.
        //
        // Корутина, а не Update с флагом: Update тикал бы каждый кадр всю жизнь
        // виджета, а корутина заканчивается ровно тогда, когда ответ получен, и
        // умирает вместе с объектом.
        IEnumerator WatchReady()
        {
            // Реальное время: меню живёт при замороженном игровом
            // (GameplayFreeze), и WaitForSeconds там не тикает.
            float deadline = Time.unscaledTime + readyTimeoutSeconds;

            while (true)
            {
                // Сервис мог появиться только что — тогда же и подпишемся.
                SubscribeAccount();

                if (_account != null && (!_account.IsAvailable || _account.IsReady))
                    break;

                if (Time.unscaledTime >= deadline)
                {
                    _readyTimedOut = true;

                    Debug.LogWarning(
                        $"XsollaIdAccountWidget: session restore did not answer in " +
                        $"{readyTimeoutSeconds:0} s — showing the player as a guest.", this);
                    break;
                }

                yield return new WaitForSecondsRealtime(readyPollSeconds);
            }

            _readyWatch = null;

            RefreshAvatar();
            Apply();
        }

        // ---- вход ----

        // ВНИМАНИЕ: SignInAsync зовём СИНХРОННО, первой же строкой после
        // проверок. Ни одного await до него быть не может — браузер посчитает
        // всплывающее окно непрошеным и заблокирует. Рабочий образец:
        // XsollaIdDebugPanel.
        void OnSignInClicked()
        {
            if (_account == null || !_account.IsAvailable)
            {
                Debug.LogError(
                    "XsollaIdAccountWidget: sign-in clicked while the service is missing " +
                    "or unavailable — the button should not be here.", this);
                return;
            }

            // Окно уже открыто. По контракту повторный SignInAsync вернул бы ту
            // же задачу и второго окна не открыл, но лишний жест гасим здесь.
            if (_signInTask != null && !_signInTask.IsCompleted)
                return;

            StopWatch(ref _errorTimer);
            _errorKey = null;

            _signInTask = _account.SignInAsync();

            Apply();

            StopWatch(ref _signInWatch);
            _signInWatch = StartCoroutine(WatchSignIn(_signInTask));
        }

        // Задачу дожидаемся КОРУТИНОЙ, а не async void. Причина не
        // стилистическая: сцена перезагружается при смерти игрока, и продолжение
        // async-метода писало бы в уже уничтоженный объект
        // (MissingReferenceException где-то через кадр). Корутина умирает вместе
        // с GameObject'ом.
        IEnumerator WatchSignIn(Task<XsollaIdResult> task)
        {
            while (!task.IsCompleted)
                yield return null;

            _signInWatch = null;

            if (_signInTask == task)
                _signInTask = null;

            // Из IXsollaIdAuth исключения не летят НИКОГДА, но задача, сорванная
            // чем-то посторонним, не должна валить виджет.
            XsollaIdResult result = task.Status == TaskStatus.RanToCompletion
                ? task.Result
                : new XsollaIdResult.Failure(
                    XsollaIdErrorCode.Network,
                    task.Exception?.GetBaseException().Message ?? "sign-in task faulted");

            if (result is XsollaIdResult.Success)
            {
                Debug.Log("XsollaIdAccountWidget: the player signed in with Xsolla ID.");

                _errorKey = null;

                // SignedIn мог прийти событием раньше — Resolve это учитывает.
                RefreshAvatar();
                Apply();
                yield break;
            }

            var failure = (XsollaIdResult.Failure)result;

            // Логируем ВСЕГДА, включая молчаливые исходы: молчание — для игрока,
            // не для лога.
            Debug.LogWarning(
                $"XsollaIdAccountWidget: sign-in failed — {failure.Code}: {failure.Message}");

            _errorKey = XsollaIdAccountWidgetLogic.ErrorKey(failure.Code);

            Apply();

            if (!string.IsNullOrEmpty(_errorKey) && errorHoldSeconds > 0f)
            {
                StopWatch(ref _errorTimer);
                _errorTimer = StartCoroutine(HideErrorAfterDelay());
            }
        }

        IEnumerator HideErrorAfterDelay()
        {
            yield return new WaitForSecondsRealtime(errorHoldSeconds);

            _errorTimer = null;
            _errorKey = null;

            Apply();
        }

        // ---- раскрытие карточки ----

        // Никакой модальности и никакого перехвата клика мимо карточки.
        // Ловушка на весь экран свернула бы её «как принято», но виджет живёт
        // поверх главного меню и съедал бы нажатия по нему — а этот блок
        // модальность не трогает принципиально (см. комментарий у класса).
        // Поэтому закрывается карточка тем же нажатием, что и открылась.
        void OnExpandClicked()
        {
            if (_account == null || !_account.IsSignedIn)
                return;

            _expanded = !_expanded;

            Apply();
        }

        // ---- выход ----

        // Открываем прямо из обработчика клика: в WebGL браузер разрешает
        // новую вкладку только как непосредственную реакцию на жест игрока.
        void OnOpenBackPackClicked() => Application.OpenURL(BackPackUrl);

        void OnLogOutClicked()
        {
            if (_account == null || !_account.IsSignedIn)
                return;

            // Подтверждения нет по решению: выход обратим — гостевой сейв живёт
            // отдельной ветвью и после выхода возвращается как был, а прогресс
            // аккаунта остаётся при аккаунте (см. SaveSwitch). Двойной выход всё
            // равно гасим.
            if (_signOutPending)
                return;

            Debug.Log("XsollaIdAccountWidget: signing out.");

            _signOutPending = true;
            Apply();

            // Локальную часть выхода сервис делает всегда, даже если отзыв
            // токена на сервере не прошёл — событие SignedOut придёт и
            // перерисует нас. Задачу всё равно дожидаемся: если сервер отвалился,
            // кнопка не должна остаться серой навсегда.
            StopWatch(ref _signOutWatch);
            _signOutWatch = StartCoroutine(WatchSignOut(_account.SignOutAsync()));
        }

        IEnumerator WatchSignOut(Task task)
        {
            while (!task.IsCompleted)
                yield return null;

            _signOutWatch = null;
            _signOutPending = false;

            Apply();
        }

        // ---- аватар ----

        // Виджет сам ТЯНЕТ картинку у загрузчика: адрес живёт в сессии, а
        // готовый спрайт — в кэше загрузчика, который переживает перезагрузку
        // сцены.
        void RefreshAvatar()
        {
            AvatarUrl url = _account?.Current?.Picture ?? AvatarUrl.None;

            if (url == _avatarUrl && _avatar != null)
                return;

            _avatarUrl = url;
            _avatar = null;

            if (_avatars == null || !url.IsValid)
                return;

            // Уже в памяти — берём без ожидания, чтобы не показывать заглушку
            // лишний кадр после перезагрузки сцены.
            if (_avatars.TryGetCached(url, out Sprite cached))
            {
                _avatar = cached;
                return;
            }

            StartCoroutine(WatchAvatar(url, _avatars.LoadAsync(url)));
        }

        IEnumerator WatchAvatar(AvatarUrl url, Task<AvatarResult> task)
        {
            while (!task.IsCompleted)
                yield return null;

            // Пока качали, игрок мог выйти или войти другим — тогда результат
            // уже не про него.
            if (url != _avatarUrl)
                yield break;

            if (task.Status == TaskStatus.RanToCompletion && task.Result is AvatarResult.Success ok)
            {
                _avatar = ok.Sprite;
                Apply();
                yield break;
            }

            // Заглушка остаётся на месте: игроку об отсутствии картинки не
            // сообщаем. А в консоль пишем ВСЕГДА — она разработчику, и именно
            // эта строка показала, что Xsolla ID отдаёт webp, который
            // DownloadHandlerTexture не берёт.
            var failure = task.Result as AvatarResult.Failure;

            if (failure != null)
                Debug.LogWarning($"XsollaIdAccountWidget: avatar unavailable — {failure.Code}: {failure.Message}");
        }

        // ---- отрисовка ----

        // ЕДИНСТВЕННОЕ место, где состояние превращается в картинку. Все
        // события, таймеры и клики только меняют поля и зовут Apply — двух путей
        // к экрану нет намеренно.
        void Apply()
        {
            AccountWidgetState state = XsollaIdAccountWidgetLogic.Resolve(
                serviceExists: _account != null,
                isAvailable: _account != null && _account.IsAvailable,
                isReady: _account != null && _account.IsReady,
                readyTimedOut: _readyTimedOut,
                isSignedIn: _account != null && _account.IsSignedIn,
                signInPending: _signInTask != null && !_signInTask.IsCompleted,
                signOutPending: _signOutPending,
                hasError: !string.IsNullOrEmpty(_errorKey));

            SetActiveSafe(checkingRoot, state == AccountWidgetState.Checking);

            SetActiveSafe(
                signedOutRoot,
                state == AccountWidgetState.SignedOut ||
                state == AccountWidgetState.SigningIn ||
                state == AccountWidgetState.SignedOutWithError);

            SetActiveSafe(
                signedInRoot,
                state == AccountWidgetState.SignedIn ||
                state == AccountWidgetState.SigningOut);

            if (signInLabel != null)
                signInLabel.SetKey(state == AccountWidgetState.SigningIn ? KeySigningIn : KeySignIn);

            if (errorLabel != null)
            {
                bool showError = state == AccountWidgetState.SignedOutWithError;

                SetActiveSafe(errorLabel.gameObject, showError);

                if (showError)
                    errorLabel.text = Loc.Resolve(_errorKey);
            }

            SetActiveSafe(
                expandedRoot,
                XsollaIdAccountWidgetLogic.ShowsExpandedPanel(state, _expanded));

            // Сессию не кэшируем: обновление токена подменяет Current новым
            // объектом, и читать надо живое значение.
            XsollaIdSession current = _account?.Current;

            if (nameLabel != null)
            {
                // Имя из профиля идёт первым, DisplayName — запасным. Клейма
                // name у аккаунта может не быть вовсе, а пустая строка на месте
                // имени выглядит как сломанная верстка, поэтому запасной путь
                // обязателен: DisplayName выбран из четырёх клеймов и пустым
                // почти не бывает.
                string shown = XsollaIdAccountWidgetLogic.ShortName(current?.FullName, nameMaxChars)
                               ?? XsollaIdAccountWidgetLogic.ShortName(current?.DisplayName, nameMaxChars);

                nameLabel.text = shown ?? Loc.Resolve(KeyNameUnknown);
            }

            if (userNameLabel != null)
            {
                // Shorten, а не ShortName: username не адрес почты, и резать его
                // по '@' нечего — но длину ограничить надо, сгенерированный
                // id<цифры> иначе растянет карточку.
                string user = XsollaIdAccountWidgetLogic.Shorten(current?.UserName, nameMaxChars);

                // Строку прячем целиком: пустая оставила бы в карточке дырку
                // ровно там, где игрок ожидает увидеть значение.
                SetActiveSafe(userNameLabel.gameObject, user != null);

                if (user != null)
                    userNameLabel.text = user;
            }

            if (avatarImage != null)
            {
                Sprite sprite = _avatar != null ? _avatar : avatarPlaceholder;

                avatarImage.sprite = sprite;
                avatarImage.enabled = sprite != null;
            }

            if (logOutButton != null)
                logOutButton.interactable = !_signOutPending;

            // ПРЯЧЕМ, а не гасим — как и весь виджет при недоступном входе.
            //
            // И делаем это здесь, хотя остальные кнопки видимостью не управляют
            // вовсе: BackPackBtn лежит в префабе ребёнком КОРНЯ, рядом с тремя
            // состояниями, а не внутри signedInRoot. Ни одно переключение
            // состояний её не касается, а сам корень не выключается никогда —
            // без этой строки кнопка видна всегда, включая Hidden.
            if (openBackpackUrl != null)
                SetActiveSafe(openBackpackUrl.gameObject, _showBackPackLink);
        }

        // ---- мелочи ----

        static void Wire(Button button, UnityEngine.Events.UnityAction handler)
        {
            if (button == null)
                return;

            button.onClick.RemoveListener(handler);
            button.onClick.AddListener(handler);
        }

        // SetActive только на изменение: лишний вызов перестраивает разметку
        // канваса.
        static void SetActiveSafe(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
                target.SetActive(active);
        }

        void StopWatch(ref Coroutine routine)
        {
            if (routine == null)
                return;

            StopCoroutine(routine);
            routine = null;
        }

#if UNITY_EDITOR
        // Превью верстки в редакторе: шестерёнка компонента → Preview: …
        //
        // Нужно потому, что настоящий вход в редакторе недоступен ВСЕГДА:
        // XsollaIdWebBrowser.IsSupported живёт под #if UNITY_WEBGL &&
        // !UNITY_EDITOR, поэтому фабрика отдаёт UnavailableXsollaIdAuth, и
        // виджет честно прячется. Верстку «вошёл» иначе не увидеть вовсе.
        //
        // Зовите на ЭКЗЕМПЛЯРЕ в сцене, а не в режиме правки префаба — иначе
        // SetActive осядет в префабе как правка.
        // username намеренно похож на настоящий: Xsolla ID выдаёт аккаунту без
        // своего username сгенерированный id<цифры>, и превью должно показывать
        // именно эту пару, а не два похожих слова.
        private const string PreviewUserName = "id19334455";
        private const string PreviewFullName = "Anton Smirnov";

        [ContextMenu("Preview: signed in")]
        private void PreviewSignedIn() => PreviewWith(
            FakeXsollaIdAuth.SignedInAs("anton@mail.com", PreviewUserName, PreviewFullName));

        // Раскрытая карточка отдельным пунктом: сама раскрытость живёт в поле, а
        // не в сервисе, поэтому иначе её в редакторе не увидеть — нажатие по
        // кнопке в режиме правки не идёт.
        [ContextMenu("Preview: signed in (expanded)")]
        private void PreviewSignedInExpanded()
        {
            PreviewWith(FakeXsollaIdAuth.SignedInAs("anton@mail.com", PreviewUserName, PreviewFullName));

            _expanded = true;

            Apply();
        }

        // Аккаунт без клейма name: имя должно уехать на запасной путь, а строка
        // username — исчезнуть целиком. Именно здесь ломается верстка, если
        // пустые значения не прятать.
        [ContextMenu("Preview: signed in (expanded, no claims)")]
        private void PreviewSignedInExpandedBare()
        {
            PreviewWith(FakeXsollaIdAuth.SignedInAs("anton@mail.com"));

            _expanded = true;

            Apply();
        }

        [ContextMenu("Preview: signed out")]
        private void PreviewSignedOut() => PreviewWith(FakeXsollaIdAuth.Guest());

        [ContextMenu("Preview: checking")]
        private void PreviewChecking() => PreviewWith(FakeXsollaIdAuth.Restoring());

        [ContextMenu("Preview: error (popup blocked)")]
        private void PreviewError()
        {
            PreviewWith(FakeXsollaIdAuth.Guest());

            _errorKey = XsollaIdAccountWidgetLogic.ErrorKeyPopupBlocked;

            Apply();
        }

        [ContextMenu("Preview: hidden (unavailable)")]
        private void PreviewHidden() => PreviewWith(FakeXsollaIdAuth.Unavailable());

        private void PreviewWith(IXsollaIdAuth fake)
        {
            UnbindAccount();

            _account = fake;
            _avatars = null;
            _readyTimedOut = false;
            _errorKey = null;
            _signOutPending = false;
            _signInTask = null;
            _avatar = null;
            _avatarUrl = AvatarUrl.None;
            _expanded = false;

            Apply();
        }
#endif
    }
}
