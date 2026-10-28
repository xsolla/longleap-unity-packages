using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Skins;
using UnityEngine;

namespace LongLeap.Customization
{
    /// <summary>
    /// Надетые скины: один вариант на папку Spine, несколько папок на вкладку UI.
    /// Исключение — группы с множественным выбором (Multi Select в Skin Layout Editor,
    /// аксессуары): там надето сколько угодно вариантов сразу.
    /// Визуальные конфликты между папками (борода из-под шлема) решает не инвентарь,
    /// а рендер скелета на стороне хоста (правила перекрытия слотов).
    /// </summary>
    public sealed class EquipmentService : IEquipmentService, IDisposable
    {
        private readonly IInventoryService _inventory;
        private readonly IEquipmentPersistence _persistence;
        private readonly Dictionary<SkinCategoryId, List<SkinId>> _equipped =
            new Dictionary<SkinCategoryId, List<SkinId>>();
        private readonly List<SkinId> _outfit = new List<SkinId>();
        private readonly HashSet<SkinCategoryId> _unsaved = new HashSet<SkinCategoryId>();
        private bool _saving;
        private bool _disposed;

        public EquipmentService(IInventoryService inventory, IEquipmentPersistence persistence = null)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _persistence = persistence;

            foreach (var category in _inventory.Categories)
                _equipped[category] = new List<SkinId>();

            DressDefaults();
            RebuildOutfit();
            _inventory.Changed += OnInventoryChanged;
        }

        public event Action<SkinCategoryId, SkinId> Changed;
        public IReadOnlyList<SkinId> Outfit => _outfit;

        public SkinId GetEquipped(SkinCategoryId category) =>
            _equipped.TryGetValue(category, out var skins) && skins.Count > 0 ? skins[0] : default;

        public bool IsEquipped(SkinId skin) => !skin.IsNone && _outfit.Contains(skin);

        /// <summary>
        /// В сейве нашлось надетое хотя бы одной категории — то есть это не новый игрок
        /// в стартовом наборе. По нему загрузка решает, красить ли стартовый набор
        /// цветами из Skin Layout Editor (так делает загрузка хоста): выбор игрока не трогаем.
        /// </summary>
        public bool HasSavedOutfit { get; private set; }

        public async Task LoadAsync()
        {
            if (_persistence == null) return;

            var before = new Dictionary<SkinCategoryId, List<SkinId>>();
            var loaded = new Dictionary<SkinCategoryId, List<SkinId>>();
            foreach (var category in _inventory.Categories)
                before[category] = new List<SkinId>(_equipped[category]);

            var touched = new HashSet<SkinCategoryId>();
            foreach (var category in _inventory.Categories)
            {
                var saved = await _persistence.LoadAsync(category);
                if (saved == null) continue;

                HasSavedOutfit = true;

                var valid = new List<SkinId>();
                foreach (var skin in saved)
                    if (!skin.IsNone && _inventory.Has(skin) &&
                        _inventory.TryGetCategory(skin, out var actual) && actual == category)
                        valid.Add(skin);

                // Если все записи устарели, оставляем резерв категории. Пустой
                // список в сейве, напротив, означает намеренно снятые скины.
                if (saved.Count > 0 && valid.Count == 0) continue;
                loaded[category] = valid;
                _equipped[category].Clear();
                foreach (var skin in valid)
                    Insert(category, skin, touched);
            }

            RebuildOutfit();
            foreach (var category in _inventory.Categories)
            {
                var expected = loaded.TryGetValue(category, out var saved) ? saved : before[category];
                if (!SameSkins(_equipped[category], expected)) _unsaved.Add(category);
            }
            SaveSoon();
        }

        private static bool SameSkins(IReadOnlyList<SkinId> left, IReadOnlyList<SkinId> right)
        {
            if (left.Count != right.Count) return false;
            for (var i = 0; i < left.Count; i++)
                if (left[i] != right[i]) return false;
            return true;
        }

        public bool Equip(SkinId skin)
        {
            if (skin.IsNone || !_inventory.Has(skin) ||
                !_inventory.TryGetCategory(skin, out var category))
                return false;

            if (IsEquipped(skin)) return true;

            var touched = new HashSet<SkinCategoryId>();
            Insert(category, skin, touched);
            Commit(touched);
            return true;
        }

        public bool Unequip(SkinId skin)
        {
            if (skin.IsNone || !_inventory.TryGetCategory(skin, out var category) ||
                !_equipped.TryGetValue(category, out var selected) || !selected.Contains(skin))
                return false;

            // Некоторые группы (базовая голова, доска) не могут остаться пустыми —
            // последний вариант такой группы снять нельзя, только заменить другим
            // (см. Insert), поэтому повторное нажатие на него ничего не делает. В группе
            // с множественным выбором снять можно любой вариант, кроме последнего.
            var group = GroupOf(skin);
            if (!_inventory.CanGroupBeOff(group) && CountInGroup(selected, group) <= 1) return false;

            selected.Remove(skin);

            var touched = new HashSet<SkinCategoryId> { category };
            Commit(touched);
            return true;
        }

        /// <summary>
        /// Новый выбор вытесняет прежний вариант своей же папки в той же категории —
        /// кроме групп с множественным выбором: там он добавляется к уже надетым.
        /// </summary>
        private void Insert(SkinCategoryId category, SkinId skin, HashSet<SkinCategoryId> touched)
        {
            var group = GroupOf(skin);
            var selected = _equipped[category];

            if (_inventory.IsGroupMultiSelect(group))
            {
                if (!selected.Contains(skin)) selected.Add(skin);
                touched.Add(category);
                return;
            }

            for (var i = selected.Count - 1; i >= 0; i--)
            {
                if (GroupOf(selected[i]) == group)
                    selected.RemoveAt(i);
            }

            selected.Add(skin);
            touched.Add(category);
        }

        private static string GroupOf(SkinId skin)
        {
            var value = skin.Value;
            var slash = value.LastIndexOf('/');
            return slash < 0 ? value : value.Substring(0, slash);
        }

        private static int CountInGroup(List<SkinId> selected, string group)
        {
            var count = 0;
            foreach (var skin in selected)
                if (GroupOf(skin) == group) count++;
            return count;
        }

        /// <summary>
        /// Стартовый набор — во что одет новый игрок, пока сейв не сказал иного: скины
        /// Owned From Start (<see cref="IInventoryService.GetStartingOutfit"/>). В обычной
        /// группе — первый такой скин группы (порядок каталога, чтобы новичок выглядел
        /// одинаково от билда к билду), в группе с множественным выбором — все.
        ///
        /// Раньше здесь надевался один Default на категорию, а в категории несколько
        /// групп: голова, грип и колёса у новичка оставались пустыми. Поэтому вторым
        /// проходом — группы, которым нельзя быть пустыми (Can Be Off выключен): без
        /// своего Owned From Start они получают резерв (<see cref="FindOwnedInGroup"/>).
        /// Остальные группы без Owned From Start на старте пусты — так задумано раскладкой.
        /// </summary>
        private void DressDefaults()
        {
            var touched = new HashSet<SkinCategoryId>();
            foreach (var category in _inventory.Categories)
            {
                var selected = _equipped[category];

                foreach (var skin in _inventory.GetStartingOutfit(category))
                {
                    if (!_inventory.Has(skin)) continue;

                    var group = GroupOf(skin);
                    if (!_inventory.IsGroupMultiSelect(group) && CountInGroup(selected, group) > 0) continue;

                    Insert(category, skin, touched);
                }

                foreach (var skin in _inventory.GetSkins(category))
                {
                    var group = GroupOf(skin);
                    if (_inventory.CanGroupBeOff(group) || CountInGroup(selected, group) > 0) continue;

                    var reserve = FindOwnedInGroup(category, group);
                    if (!reserve.IsNone) Insert(category, reserve, touched);
                }
            }
        }

        private void OnInventoryChanged()
        {
            var touched = new HashSet<SkinCategoryId>();
            foreach (var category in _inventory.Categories)
            {
                var selected = _equipped[category];
                for (var i = selected.Count - 1; i >= 0; i--)
                {
                    var skin = selected[i];
                    if (_inventory.Has(skin)) continue;

                    var group = GroupOf(skin);
                    selected.RemoveAt(i);
                    touched.Add(category);

                    // Замена — только если группа опустела: в группе с множественным
                    // выбором могли остаться другие надетые варианты, и докладывать к ним
                    // ещё один значило бы надеть то, чего игрок не выбирал.
                    if (CountInGroup(selected, group) > 0) continue;

                    var replacement = FindOwnedInGroup(category, group);
                    if (!replacement.IsNone) Insert(category, replacement, touched);
                }
            }

            if (touched.Count > 0) Commit(touched);
        }

        /// <summary>
        /// Чем занять опустевшую группу: её стартовый скин (тот же, что у новичка), иначе
        /// резерв категории, если он из этой группы, иначе первый открытый скин группы.
        /// </summary>
        private SkinId FindOwnedInGroup(SkinCategoryId category, string group)
        {
            foreach (var skin in _inventory.GetStartingOutfit(category))
                if (GroupOf(skin) == group && _inventory.Has(skin)) return skin;

            var preferred = _inventory.GetDefault(category);
            if (GroupOf(preferred) == group && _inventory.Has(preferred)) return preferred;

            foreach (var skin in _inventory.GetOwned(category))
                if (GroupOf(skin) == group) return skin;

            return default;
        }

        private void Commit(HashSet<SkinCategoryId> touched)
        {
            RebuildOutfit();
            foreach (var category in touched) _unsaved.Add(category);
            SaveSoon();
            foreach (var category in touched) RaiseChanged(category, GetEquipped(category));
        }

        private void RebuildOutfit()
        {
            _outfit.Clear();
            foreach (var category in _inventory.Categories)
                _outfit.AddRange(_equipped[category]);
        }

        public async Task FlushAsync()
        {
            if (_persistence == null || _saving) return;

            _saving = true;
            try
            {
                while (_unsaved.Count > 0)
                {
                    var category = FirstUnsaved();
                    var snapshot = new List<SkinId>(_equipped[category]);
                    _unsaved.Remove(category);

                    try
                    {
                        await _persistence.SaveAsync(category, snapshot);
                    }
                    catch (Exception e)
                    {
                        _unsaved.Add(category);
                        Debug.LogWarning($"[Player] не удалось сохранить надетое в '{category}': {e.Message}");
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
            if (_persistence != null) _ = FlushAsync();
        }

        private SkinCategoryId FirstUnsaved()
        {
            foreach (var category in _unsaved) return category;
            return default;
        }

        private void RaiseChanged(SkinCategoryId category, SkinId skin)
        {
            try { Changed?.Invoke(category, skin); }
            catch (Exception e)
            {
                Debug.LogError($"[Player] подписчик Changed кинул исключение: {e}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _inventory.Changed -= OnInventoryChanged;
        }
    }
}
