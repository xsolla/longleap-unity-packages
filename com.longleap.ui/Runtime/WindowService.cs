using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace LongLeap.UI
{
    /// <summary>
    /// Корень UI и он же <see cref="IWindowService"/>.
    ///
    /// Окна в префабе корня не лежат. Корень держит каталог <c>Windows</c> — список
    /// <see cref="WindowEntry"/>: ссылка на префаб окна в проекте плюс правила его жизни.
    /// Сервис сам создаёт окно под своим корнем, когда оно нужно, и уничтожает, когда
    /// не нужно (ссылки — Addressables, поэтому выгруженное окно освобождает и память
    /// префаба, а не только экземпляр):
    /// <list type="bullet">
    /// <item><b>Preload</b> — создаётся при старте (<see cref="InitializeAsync"/>), живёт до выхода;</item>
    /// <item>без галок — создаётся при первом открытии, дальше живёт до выхода;</item>
    /// <item><b>Unload On Close</b> — создаётся при открытии, уничтожается при закрытии.</item>
    /// </list>
    ///
    /// Порядок строк каталога — порядок отрисовки: нижняя строка рисуется поверх верхней,
    /// в каком бы порядке окна ни создавались.
    ///
    /// Префаб корня инстанцируется один раз задачей <c>WindowServiceTask</c>, живёт до
    /// выхода из игры (DontDestroyOnLoad) и переживает смену сцен. Загрузка покадровая,
    /// без потоков — WebGL-safe. async у открытия и закрытия — ещё и затем, чтобы окно
    /// могло проиграть свою анимацию, а вызывающий её дождался.
    ///
    /// Активностью объектов сервис не управляет: показать и спрятать себя — дело окна
    /// (<see cref="WindowBase"/> включает и гасит свой Root). Сервис решает, какое окно
    /// создано и открыто, и ждёт его анимаций. Новое окно он сразу закрывает кодом,
    /// поэтому неважно, каким сохранили префаб окна.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WindowService : MonoBehaviour, IWindowService
    {
        /// <summary>
        /// Адрес префаба корня в Addressables по умолчанию — по нему его грузит загрузка игры
        /// (может взять и свой ключ).
        /// Совпадает с путём ассета: именно такой адрес Addressables ставит по умолчанию,
        /// когда префаб помечают галкой Addressable.
        /// </summary>
        public const string AddressableKey = "Assets/Content/Prefab/UI/WindowServiceCanvas.prefab";

        [Tooltip("Каталог окон: префаб + правила (предзагрузка, выгрузка при закрытии). " +
                 "Порядок строк — порядок отрисовки, нижнее окно поверх верхних.")]
        [SerializeField] private WindowEntry[] _windows;

        /// <summary>Рабочее состояние одной строки каталога.</summary>
        private sealed class Slot
        {
            public WindowEntry Entry;
            public Type Type;

            /// <summary>Позиция в каталоге — она же порядок отрисовки.</summary>
            public int Order;

            /// <summary>Созданный экземпляр; null, пока окно не создано или уже выгружено.</summary>
            public WindowBase Instance;

            /// <summary>Загрузка префаба, под которую создан <see cref="Instance"/>: отпускается при выгрузке.</summary>
            public AsyncOperationHandle<GameObject> Handle;

            /// <summary>Создание идёт прямо сейчас: параллельные вызовы ждут одну и ту же задачу.</summary>
            public Task<WindowBase> Loading;

            public readonly List<Action<WindowBase>> Setups = new List<Action<WindowBase>>();
        }

        private readonly Dictionary<Type, Slot> _slots = new Dictionary<Type, Slot>();
        private readonly List<Slot> _ordered = new List<Slot>();
        private readonly HashSet<Type> _open = new HashSet<Type>();

        /// <summary>Куда ведёт «назад» страниц меню. Задаёт игра: <see cref="SetHome{TWindow}"/>.</summary>
        private Func<Task> _openHome;

        /// <summary>EventSystem, через который работает UI: свой или найденный в сцене.</summary>
        private EventSystem _eventSystem;

        /// <summary>
        /// Создать окна с галкой Preload. Зовёт WindowServiceTask после инстанцирования
        /// корня. Ошибка одного окна не останавливает остальные: она уходит в лог,
        /// а открытие этого окна позже попробует создать его снова.
        /// </summary>
        public Task InitializeAsync()
        {
            var tasks = new List<Task>();

            foreach (var slot in _ordered)
            {
                if (slot.Entry.Preload)
                    tasks.Add(PreloadAsync(slot));
            }

            return Task.WhenAll(tasks);
        }

        public bool IsRegistered<TWindow>() where TWindow : class, IWindow => _slots.ContainsKey(typeof(TWindow));

        public bool TryGet<TWindow>(out TWindow window) where TWindow : class, IWindow
        {
            window = _slots.TryGetValue(typeof(TWindow), out var slot) && slot.Instance != null
                ? slot.Instance as TWindow
                : null;

            return window != null;
        }

        public async Task<TWindow> LoadAsync<TWindow>() where TWindow : class, IWindow =>
            await LoadSlotAsync(Resolve<TWindow>()) as TWindow;

        public async Task UnloadAsync<TWindow>() where TWindow : class, IWindow
        {
            var slot = Resolve<TWindow>();

            await CloseAsync(slot.Type);

            // Окно могли как раз создавать: дожидаемся, иначе выгружать будет нечего,
            // а созданное следом останется висеть.
            if (slot.Loading != null)
            {
                try
                {
                    await slot.Loading;
                }
                catch
                {
                    // Не создалось — выгружать нечего; причину уже видел тот, кто его создавал.
                }
            }

            if (!_open.Contains(slot.Type))
                Unload(slot);
        }

        public void OnCreated<TWindow>(Action<TWindow> setup) where TWindow : class, IWindow
        {
            if (setup == null) return;

            if (!_slots.TryGetValue(typeof(TWindow), out var slot))
            {
                Debug.LogError(
                    $"[Windows] {typeof(TWindow).Name}: подготовку окна некуда повесить — " +
                    $"его нет в каталоге Windows на префабе {AddressableKey}.");
                return;
            }

            void Apply(WindowBase window) => setup(window as TWindow);

            slot.Setups.Add(Apply);

            if (slot.Instance != null)
                RunSetup(slot, Apply);
        }

        public async Task<TWindow> OpenAsync<TWindow>() where TWindow : class, IWindow
        {
            var slot = Resolve<TWindow>();

            // Флаг ставим до await: два параллельных OpenAsync не проиграют анимацию дважды.
            var first = _open.Add(slot.Type);

            WindowBase window;
            try
            {
                window = await LoadSlotAsync(slot);
            }
            catch
            {
                // Не создали — окно не открыто, и повторное открытие должно попробовать снова.
                if (first)
                    _open.Remove(slot.Type);

                throw;
            }

            // Пока окно создавалось, его могли закрыть — показывать уже нечего.
            if (first && _open.Contains(slot.Type))
            {
                // Включить свой Root — забота окна: см. WindowBase.OpenAsync.
                await window.OpenAsync();
                Debug.Log($"[Windows] открыто {typeof(TWindow).Name}");
            }

            return window as TWindow;
        }

        public Task CloseAsync<TWindow>() where TWindow : class, IWindow => CloseAsync(typeof(TWindow));

        public Task CloseAsync(IWindow window) =>
            window != null ? CloseAsync(window.GetType()) : Task.CompletedTask;

        public async Task CloseAllAsync()
        {
            foreach (var type in new List<Type>(_open))
                await CloseAsync(type);
        }

        public bool IsOpen<TWindow>() where TWindow : class, IWindow => _open.Contains(typeof(TWindow));

        public void SetHome<TWindow>() where TWindow : class, IWindow
        {
            _openHome = async () => await OpenAsync<TWindow>();
        }

        public Task OpenHomeAsync()
        {
            if (_openHome != null) return _openHome();

            Debug.LogWarning("[Windows] домашнее окно не задано (SetHome) — «назад» некуда вести.");
            return Task.CompletedTask;
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            EnsureEventSystem();
            DisableForeignEventSystems();
            Index();

            // Visual press feedback is available even when audio has not loaded.
            if (GetComponent<MenuClickAudioBinder>() == null)
                gameObject.AddComponent<MenuClickAudioBinder>();

            // Игровая сцена грузится позже UI и может принести свой EventSystem.
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

#if UNITY_EDITOR
        /// <summary>Подменить каталог целиком. Нужно только инструменту переноса окон из корня.</summary>
        public void SetCatalog(WindowEntry[] entries)
        {
            _windows = entries;
            UnityEditor.EditorUtility.SetDirty(this);
        }

        /// <summary>Тип и подпись строки берутся из префаба — руками их не вводят.</summary>
        private void OnValidate()
        {
            if (_windows == null) return;

            foreach (var entry in _windows)
                entry?.Refresh();
        }
#endif

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => DisableForeignEventSystems();

        /// <summary>
        /// Раскладывает каталог по типам окон. Сами окна тут не создаются — только
        /// проверяется, что у каждой строки есть префаб и известный тип.
        /// </summary>
        private void Index()
        {
            if (_windows == null) return;

            for (var i = 0; i < _windows.Length; i++)
            {
                var entry = _windows[i];

                if (entry == null || entry.Prefab == null || !entry.Prefab.RuntimeKeyIsValid())
                {
                    Debug.LogError($"[Windows] в строке {i} каталога окон префаба '{name}' не задан префаб.");
                    continue;
                }

                var type = string.IsNullOrEmpty(entry.TypeName) ? null : Type.GetType(entry.TypeName);
                if (type == null || !typeof(WindowBase).IsAssignableFrom(type))
                {
                    Debug.LogError(
                        $"[Windows] строка {i} каталога ('{entry.Label}'): тип окна не определён. " +
                        $"Открой префаб {AddressableKey} в редакторе — тип подтянется из префаба окна.");
                    continue;
                }

                if (_slots.ContainsKey(type))
                {
                    Debug.LogError(
                        $"[Windows] {type.Name} в каталоге дважды — вторая строка открываться не будет.");
                    continue;
                }

                var slot = new Slot { Entry = entry, Type = type, Order = _ordered.Count };
                _slots.Add(type, slot);
                _ordered.Add(slot);
            }
        }

        private async Task CloseAsync(Type type)
        {
            if (!_slots.TryGetValue(type, out var slot)) return;

            // Из открытых убираем до await: повторный CloseAsync не станет ждать анимацию второй раз.
            if (!_open.Remove(type)) return;

            // Закрыли, пока окно ещё создавалось: показывать его не начинали, так что
            // анимации ухода нет — дожидаемся создания лишь затем, чтобы применить правило выгрузки.
            if (slot.Instance != null)
            {
                // Погасить свой Root — забота окна: см. WindowBase.CloseAsync.
                await slot.Instance.CloseAsync();
            }
            else if (slot.Loading != null)
            {
                try
                {
                    await slot.Loading;
                }
                catch
                {
                    // Не создалось — выгружать нечего; причину увидит тот, кто открывал.
                    return;
                }
            }

            // Пока закрывалось, окно могли открыть снова — выгружать его нельзя.
            if (slot.Entry.UnloadOnClose && !_open.Contains(type))
                Unload(slot);
        }

        private Slot Resolve<TWindow>() where TWindow : class, IWindow
        {
            if (_slots.TryGetValue(typeof(TWindow), out var slot)) return slot;

            throw new InvalidOperationException(
                $"Окно {typeof(TWindow).Name} не подключено: добавь его префаб в каталог Windows " +
                $"на префабе {AddressableKey}.");
        }

        private async Task PreloadAsync(Slot slot)
        {
            try
            {
                await LoadSlotAsync(slot);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        /// <summary>Созданное окно или идущее создание; иначе начинает создавать.</summary>
        private Task<WindowBase> LoadSlotAsync(Slot slot)
        {
            if (slot.Instance != null)
                return Task.FromResult(slot.Instance);

            if (slot.Loading != null)
                return slot.Loading;

            var loading = CreateAsync(slot);

            // Закончилась на месте — запоминать нечего (и finally в CreateAsync уже отработал).
            if (!loading.IsCompleted)
                slot.Loading = loading;

            return loading;
        }

        /// <summary>
        /// Грузит префаб и ставит экземпляр под корень. Всё после загрузки — в одном куске
        /// без await: окно создаётся, закрывается и получает подготовку в том же кадре,
        /// поэтому не мелькает на экране включённым.
        /// </summary>
        private async Task<WindowBase> CreateAsync(Slot slot)
        {
            var handle = Addressables.LoadAssetAsync<GameObject>(slot.Entry.Prefab.RuntimeKey);

            try
            {
                while (!handle.IsDone)
                    await Task.Yield();

                // Корень снесли, пока грузилось (выход из игры): создавать некуда.
                if (this == null)
                    throw new OperationCanceledException();

                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                {
                    var reason = handle.OperationException?.Message ?? "ассет не найден в Addressables";
                    throw new InvalidOperationException(
                        $"Окно {slot.Type.Name} не загрузилось ({slot.Entry.Prefab.RuntimeKey}): {reason}");
                }

                var instance = Instantiate(handle.Result, transform, false);
                var window = instance.GetComponent<WindowBase>();

                if (window == null || !slot.Type.IsInstanceOfType(window))
                {
                    Destroy(instance);
                    throw new InvalidOperationException(
                        $"На корне префаба окна '{handle.Result.name}' нет компонента {slot.Type.Name}: " +
                        "каталог устарел — открой префаб сервиса в редакторе, чтобы он обновился.");
                }

                // Объект окна включён всегда (см. WindowBase): сохранённое выключенным
                // ему не подходит — на нём живёт компонент.
                if (!instance.activeSelf)
                    instance.SetActive(true);

                instance.name = slot.Type.Name;

                // Чтобы окно могло открыть следующее за собой: см. WindowBase.Windows.
                window.Bind(this);

                // Начальное состояние окон задаёт код, а не то, каким сохранили префаб.
                window.CloseImmediate();

                slot.Instance = window;
                slot.Handle = handle;

                PlaceInOrder(slot);

                foreach (var setup in slot.Setups.ToArray())
                    RunSetup(slot, setup);

                return window;
            }
            catch
            {
                // Handle ушёл в слот только вместе с экземпляром — иначе отпускать его нам.
                if (slot.Instance == null && handle.IsValid())
                    Addressables.Release(handle);

                throw;
            }
            finally
            {
                slot.Loading = null;
            }
        }

        /// <summary>
        /// Окна создаются в произвольном порядке, а рисуются в порядке каталога:
        /// ставим новое сразу за ближайшим выше по списку из уже созданных.
        /// </summary>
        private void PlaceInOrder(Slot slot)
        {
            var index = 0;

            for (var i = slot.Order - 1; i >= 0; i--)
            {
                var above = _ordered[i].Instance;
                if (above == null) continue;

                index = above.transform.GetSiblingIndex() + 1;
                break;
            }

            slot.Instance.transform.SetSiblingIndex(index);
        }

        private void RunSetup(Slot slot, Action<WindowBase> setup)
        {
            try
            {
                setup(slot.Instance);
            }
            catch (Exception exception)
            {
                // Сломанная подготовка не должна оставить окно несозданным.
                Debug.LogException(exception);
            }
        }

        /// <summary>Уничтожить экземпляр и отпустить префаб. Окно создастся заново при следующем открытии.</summary>
        private void Unload(Slot slot)
        {
            if (slot.Instance == null) return;

            var handle = slot.Handle;

            Destroy(slot.Instance.gameObject);
            slot.Instance = null;
            slot.Handle = default;

            Debug.Log($"[Windows] выгружено {slot.Type.Name}");

            _ = ReleaseAfterDestroyAsync(handle);
        }

        /// <summary>
        /// Префаб отпускаем кадром позже: Destroy срабатывает в конце кадра, а выгрузка
        /// бандла прямо сейчас утащила бы ассеты из-под экземпляра, доживающего этот кадр.
        /// </summary>
        private static async Task ReleaseAfterDestroyAsync(AsyncOperationHandle<GameObject> handle)
        {
            await Task.Yield();

            if (handle.IsValid())
                Addressables.Release(handle);
        }

        /// <summary>
        /// UI поднимается ещё в стартовой сцене, где EventSystem может не быть, —
        /// без него кнопки не нажимаются. Свой EventSystem живёт вместе с корнем UI.
        /// </summary>
        private void EnsureEventSystem()
        {
            var host = new GameObject("[EventSystem]");
            host.transform.SetParent(transform, false);
            _eventSystem = host.AddComponent<EventSystem>();
            host.AddComponent<InputSystemUIInputModule>();
        }

        /// <summary>
        /// EventSystem в игре должен быть ровно один. UI живёт с DontDestroyOnLoad и
        /// поднимается раньше игровой сцены, поэтому свой EventSystem в сцене — это
        /// второй: два InputSystemUIInputModule делят один набор input actions,
        /// новый забирает его себе, и кнопки UI перестают нажиматься. Гасим лишние
        /// и говорим об этом в лог — в сцене такой объект держать не надо.
        /// </summary>
        private void DisableForeignEventSystems()
        {
            var systems = FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (systems.Length < 2) return;

            foreach (var system in systems)
            {
                if (system == null || system == _eventSystem) continue;

                Debug.LogWarning(
                    $"[Windows] в сцене нашёлся лишний EventSystem '{system.name}' — выключаю его, " +
                    "иначе кнопки UI перестанут нажиматься. Удали этот объект из сцены.",
                    system);

                foreach (var module in system.GetComponents<BaseInputModule>())
                    module.enabled = false;

                system.enabled = false;
            }

            // На всякий случай: current мог указывать на выключенный — вернём наш.
            if (_eventSystem != null)
                EventSystem.current = _eventSystem;
        }
    }
}
