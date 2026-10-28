using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Skins;
using UnityEngine;

namespace LongLeap.Customization
{
    /// <summary>
    /// Инвентарь скинов: что открыто, в памяти, с записью в хранилище сразу
    /// после выдачи. Запись сразу, а не по таймеру: выдача
    /// редка, игрок её видел, и терять её при вылете нельзя.
    ///
    /// Состав игры берётся из каталога, а в сейв уходят только ВЫДАННЫЕ скины —
    /// без тех, что открыты с самого начала. Базовый вид приходит из каталога
    /// при каждой загрузке: его нельзя потерять, и, если завтра базовым станет
    /// другой скин, у игроков не останется записи про старый.
    /// </summary>
    public sealed class InventoryService : IInventoryService
    {
        private static readonly SkinId[] NoSkins = new SkinId[0];

        private readonly ISkinCatalog _catalog;
        private readonly IInventoryPersistence _persistence;

        private readonly HashSet<SkinId> _owned = new HashSet<SkinId>();

        /// <summary>
        /// Скины из сейва, которых нет в каталоге: отключены (Disabled), убраны или
        /// переименованы. Игре их не показываем, но и не забываем — при каждой записи
        /// они уходят в сейв обратно (см. <see cref="BuildSavedList"/>). Иначе первая же
        /// выдача после загрузки переписала бы сейв без них, и временно отключённый
        /// скин пропал бы у игрока навсегда.
        /// </summary>
        private readonly List<SkinId> _keptFromSave = new List<SkinId>();

        // Открытое по категориям, в порядке каталога — то, что спрашивает UI.
        // Держим готовым, а не считаем на каждый запрос: панель кастомизации
        // дёргает это на каждом кадре перерисовки.
        private readonly Dictionary<SkinCategoryId, List<SkinId>> _ownedByCategory =
            new Dictionary<SkinCategoryId, List<SkinId>>();

        private bool _saving;
        private bool _unsaved;

        /// <param name="catalog">Что вообще есть в игре: категории и скины.</param>
        /// <param name="persistence">
        /// Хранилище открытых скинов. null — инвентарь живёт одну сессию (так
        /// бывает, если не поднялись облачные сейвы).
        /// </param>
        public InventoryService(ISkinCatalog catalog, IInventoryPersistence persistence = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _persistence = persistence;

            for (int i = 0; i < _catalog.Categories.Count; i++)
                _ownedByCategory[_catalog.Categories[i]] = new List<SkinId>();

            GrantStartingSkins();
            RebuildAll();
        }

        public event Action Changed;

        public IReadOnlyList<SkinCategoryId> Categories => _catalog.Categories;

        /// <summary>
        /// Прочитать открытые скины. Зовётся один раз при загрузке, до того как
        /// инвентарь попал к игре, поэтому <see cref="Changed"/> при этом не
        /// летит: подписчиков ещё нет.
        /// </summary>
        public async Task LoadAsync()
        {
            if (_persistence == null) return;

            var saved = await _persistence.LoadAsync();

            for (int i = 0; i < saved.Count; i++)
            {
                var skin = saved[i];

                if (!_catalog.Contains(skin))
                {
                    // Скин отключили, убрали из каталога или переименовали. Запись НЕ
                    // теряем: вернут скин в каталог — вернётся и игроку, а потеря
                    // стёрла бы это навсегда (см. _keptFromSave).
                    if (!_keptFromSave.Contains(skin)) _keptFromSave.Add(skin);

                    if (!_catalog.IsDisabled(skin))
                        Debug.Log($"[Player] скин '{skin}' есть в сейве, но не в каталоге — пропущен.");

                    continue;
                }

                _owned.Add(skin);
            }

            RebuildAll();
        }

        public IReadOnlyList<SkinId> GetSkins(SkinCategoryId category) => _catalog.GetSkins(category);

        public IReadOnlyList<SkinId> GetOwned(SkinCategoryId category)
        {
            if (!category.IsNone && _ownedByCategory.TryGetValue(category, out var skins))
                return skins;

            return NoSkins;
        }

        public string GetCategoryTitle(SkinCategoryId category) => _catalog.GetCategoryTitle(category);

        public string GetTitle(SkinId skin) => _catalog.GetTitle(skin);

        public SkinId GetDefault(SkinCategoryId category) => _catalog.GetDefault(category);

        public IReadOnlyList<SkinId> GetStartingOutfit(SkinCategoryId category) =>
            _catalog.GetStartingOutfit(category);

        public bool Has(SkinId skin) => !skin.IsNone && _owned.Contains(skin);

        public bool TryGetCategory(SkinId skin, out SkinCategoryId category) =>
            _catalog.TryGetCategory(skin, out category);

        public IReadOnlyList<string> GetColorZones(string group) => _catalog.GetGroupColorZones(group);

        public bool CanGroupBeOff(string group) => _catalog.CanGroupBeOff(group);

        public bool IsGroupMultiSelect(string group) => _catalog.IsGroupMultiSelect(group);

        public bool TryGetDefaultColor(SkinId skin, out Color color) => _catalog.TryGetDefaultColor(skin, out color);

        public Sprite GetIcon(SkinId skin) => _catalog.GetIcon(skin);

        public bool Grant(SkinId skin)
        {
            if (skin.IsNone)
                throw new ArgumentException("Скин не задан (default(SkinId)).", nameof(skin));

            // Отключённый скин — не опечатка, а штатное состояние контента: отказ, а не
            // исключение, чтобы награда с таким скином не роняла того, кто её выдаёт.
            if (_catalog.IsDisabled(skin))
            {
                Debug.LogWarning(
                    $"[Player] скин '{skin}' отключён (Disabled в Skin Layout Editor) — не выдан.");
                return false;
            }

            if (!_catalog.TryGetCategory(skin, out var category))
                throw new InvalidOperationException(
                    $"Скина '{skin}' нет в каталоге. Добавь его в раскладку скинов " +
                    "(Skin Layout Editor, Resources/Player/SkinLayoutSource) — иначе выдавать нечего.");

            if (!_owned.Add(skin))
                return false;

            Rebuild(category);

            // Скин, открытый с самого начала, в сейв не пишем: он и так придёт
            // из каталога. Выдать его кодом не ошибка — просто ничего не меняет.
            if (!_catalog.IsOwnedFromStart(skin))
                SaveSoon();

            RaiseChanged();
            return true;
        }

        /// <summary>
        /// ЧИТ: забыть все выданные скины, оставив только дефолтные. Зовётся из
        /// меню Tools, в игровом коде ему делать нечего — поэтому метода нет в
        /// <see cref="IInventoryService"/>.
        /// </summary>
        public void Clear()
        {
            _owned.Clear();
            _keptFromSave.Clear();
            GrantStartingSkins();
            RebuildAll();

            // Пишем пустой список, а не удаляем ключ: запись идёт тем же путём,
            // что и выдача, поэтому сохранение, начатое секунду назад, не может
            // догнать очистку и вернуть скины обратно.
            SaveSoon();

            Debug.Log("[Player] инвентарь очищен читом: остались только дефолтные скины.");
            RaiseChanged();
        }

        /// <summary>
        /// Дописать в хранилище то, что туда ещё не доехало. Обычно не нужно —
        /// изменения уходят сами, — но пригодится перед выходом из игры или
        /// чтобы дождаться записи в тесте.
        /// </summary>
        public async Task FlushAsync()
        {
            if (_persistence == null || _saving) return;

            _saving = true;
            try
            {
                while (_unsaved)
                {
                    // Снимаем пометку до записи: если скин выдадут, пока мы ждём
                    // хранилище, цикл пойдёт на второй круг и запишет уже его.
                    _unsaved = false;

                    var snapshot = BuildSavedList();

                    try
                    {
                        await _persistence.SaveAsync(snapshot);
                    }
                    catch (Exception e)
                    {
                        // Не дошло — список остаётся в памяти, следующая выдача
                        // попробует снова.
                        _unsaved = true;
                        Debug.LogWarning($"[Player] не удалось сохранить инвентарь: {e.Message}");
                        return;
                    }
                }
            }
            finally
            {
                _saving = false;
            }
        }

        private void SaveSoon()
        {
            if (_persistence == null) return;

            _unsaved = true;

            // Fire-and-forget: FlushAsync ловит ошибки хранилища внутри.
            _ = FlushAsync();
        }

        private List<SkinId> BuildSavedList()
        {
            var saved = new List<SkinId>(_owned.Count + _keptFromSave.Count);

            foreach (var skin in _owned)
            {
                if (_catalog.IsOwnedFromStart(skin)) continue;
                saved.Add(skin);
            }

            saved.AddRange(_keptFromSave);

            return saved;
        }

        private void GrantStartingSkins()
        {
            var categories = _catalog.Categories;

            for (int i = 0; i < categories.Count; i++)
            {
                var skins = _catalog.GetSkins(categories[i]);

                for (int j = 0; j < skins.Count; j++)
                {
                    if (_catalog.IsOwnedFromStart(skins[j]))
                        _owned.Add(skins[j]);
                }
            }
        }

        private void RebuildAll()
        {
            var categories = _catalog.Categories;

            for (int i = 0; i < categories.Count; i++)
                Rebuild(categories[i]);
        }

        private void Rebuild(SkinCategoryId category)
        {
            if (!_ownedByCategory.TryGetValue(category, out var owned))
                return;

            owned.Clear();

            // Порядок каталога, а не порядок выдачи: игрок видит вкладку такой
            // же, какой её собрали в ассете, независимо от того, что и когда ему
            // открылось.
            var all = _catalog.GetSkins(category);

            for (int i = 0; i < all.Count; i++)
            {
                if (_owned.Contains(all[i]))
                    owned.Add(all[i]);
            }
        }

        private void RaiseChanged()
        {
            try
            {
                Changed?.Invoke();
            }
            catch (Exception e)
            {
                // Упавший подписчик не должен ронять выдачу: скин уже открыт.
                Debug.LogError($"[Player] подписчик Changed кинул исключение: {e}");
            }
        }
    }
}
