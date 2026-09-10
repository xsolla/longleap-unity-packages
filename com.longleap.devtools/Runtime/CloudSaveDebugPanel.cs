using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using LongLeap.Core.CloudSaves;
using UnityEngine;

namespace LongLeap.DevTools
{
    // Экранная панель с содержимым сейва. Живёт в пакете и не знает ни об одной
    // конкретной игре: откуда брать сервис, ей сообщают снаружи делегатом.
    //
    // Читает только ICloudSaveBulkAccess.SnapshotAsync — это чтение памяти под
    // тем же замком, что GetStringAsync, без сети и без решений за игрока.
    // Поэтому панель показывает КЕШ СЕРВИСА, а не содержимое облака: ключ,
    // который в этой сессии никто не читал и не писал, в снимок не попадёт.
    // Лучше и нельзя — ни IPlayerPrefs, ни PlayerPrefs не умеют перечислять ключи.
    //
    // Вся сборка существует только при определённом LONGLEAP_DEBUG (см.
    // defineConstraints в LongLeap.DevTools.asmdef), так что в обычном билде от
    // этого файла не остаётся ничего.
    public sealed class CloudSaveDebugPanel : MonoBehaviour
    {
        private const float PanelWidth = 380f;
        private const float Margin = 10f;
        private const float DesignHeight = 720f;
        private const float CollapsedHeight = 44f;
        private const float HeaderButtonHeight = 30f;
        private const float RowButtonHeight = 24f;
        private const float DeleteButtonWidth = 48f;

        // Разворачивать все 500+ строк каждый кадр незачем: IMGUI перерисовывает
        // всё содержимое при каждом событии.
        private const int MaxRenderedRows = 200;
        private const int ValuePreviewLength = 120;

        private static CloudSaveDebugPanel _instance;

        private Func<ICloudSaveService> _source;
        private string _writeScope;

        private IReadOnlyDictionary<string, CloudSaveEntry> _snapshot;
        private IReadOnlyList<CloudSaveGroup> _groups;
        private readonly HashSet<string> _expandedGroups = new HashSet<string>(StringComparer.Ordinal);

        private string _filter = string.Empty;
        private string _builtForFilter;
        private bool _viewDirty = true;

        private bool _collapsed = true;
        private bool _busy;
        private bool _refreshRequested;
        private string _status = "not read yet";
        private Vector2 _scroll;
        private string _pendingDeleteKey;
        private string _expandedRowKey;
        private ICloudSaveService _lastService;

        // Всё, что меняет ЧИСЛО контролов, копится здесь и применяется только в
        // событии Layout. Поменять такое между Layout и Repaint одного кадра —
        // это ArgumentException «Getting control 0's position in a group with
        // only 0 controls», проверено на живой панели.
        private bool _pendingCollapseToggle;
        private string _pendingGroupToggle;
        private string _pendingRowToggle;
        private bool _filtering;

        // Стили — поля экземпляра и строятся лениво. Статические пережили бы
        // перезагрузку сцены и могли бы указывать на уже мёртвый шрифт.
        private GUIStyle _label;
        private GUIStyle _title;
        private GUIStyle _keyButton;

        // writeScope — человеческая подпись к кнопке удаления: удаление уходит
        // в облако не на всякой сборке, а знать, какая это сборка, может только
        // игра, не пакет.
        public static void Spawn(Func<ICloudSaveService> source, string writeScope = null)
        {
            if (source == null)
            {
                Debug.LogError($"{nameof(CloudSaveDebugPanel)}: source is null, panel not spawned.");
                return;
            }

            if (_instance != null)
            {
                // Второй панели не бывает: сцена перезагружается после смерти, а
                // делегат при этом надо переставить на новый бутстраппер.
                _instance._source = source;
                _instance._writeScope = writeScope;
                return;
            }

            var go = new GameObject(nameof(CloudSaveDebugPanel));
            var panel = go.AddComponent<CloudSaveDebugPanel>();
            panel._source = source;
            panel._writeScope = writeScope;
            _instance = panel;
            DontDestroyOnLoad(go);

            Debug.Log($"{nameof(CloudSaveDebugPanel)}: on-screen cloud save viewer enabled (right side, collapsed). Debug only, guarded by the LONGLEAP_DEBUG define.");
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void OnGUI()
        {
            EnsureStyles();

            // Тихое чтение: OnGUI зовётся каждый кадр, и ещё не собранный сервис
            // не должен сыпать в консоль по строке на кадр.
            ICloudSaveService service = _source?.Invoke();
            DetectServiceChange(service);

            // Разметка кадра и его перерисовка обязаны видеть одно и то же
            // состояние, поэтому меняем его только здесь.
            if (Event.current.type == EventType.Layout)
                ApplyPendingLayoutChanges();

            float scale = Mathf.Max(1f, Screen.height / DesignHeight);
            Matrix4x4 restore = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            float viewWidth = Screen.width / scale;
            float viewHeight = Screen.height / scale;

            // Свёрнутая панель занимает полоску, а не весь экран: развёрнутая
            // область перехватывала бы мышь у игры под ней.
            float height = _collapsed ? CollapsedHeight : viewHeight - Margin * 2f;

            GUILayout.BeginArea(new Rect(viewWidth - PanelWidth - Margin, Margin, PanelWidth, height));
            GUILayout.BeginVertical(GUI.skin.box);

            DrawHeader();
            if (!_collapsed)
                DrawBody(service);

            GUILayout.EndVertical();
            GUILayout.EndArea();

            GUI.matrix = restore;

            // Запуск асинхронной работы — после разметки, никогда внутри.
            if (_refreshRequested && !_busy)
            {
                _refreshRequested = false;
                _ = RefreshAsync();
            }
        }

        private void ApplyPendingLayoutChanges()
        {
            if (_pendingCollapseToggle)
            {
                _pendingCollapseToggle = false;
                _collapsed = !_collapsed;

                if (!_collapsed && _snapshot == null)
                    _refreshRequested = true;
            }

            if (_pendingGroupToggle != null)
            {
                if (!_expandedGroups.Remove(_pendingGroupToggle))
                    _expandedGroups.Add(_pendingGroupToggle);

                _pendingGroupToggle = null;
            }

            if (_pendingRowToggle != null)
            {
                _expandedRowKey = string.Equals(_expandedRowKey, _pendingRowToggle, StringComparison.Ordinal)
                    ? null
                    : _pendingRowToggle;

                _pendingRowToggle = null;
            }

            // Фильтр правится в событии клавиатуры, поэтому и список, и признак
            // «фильтруем» снимаются тут же, а не по ходу отрисовки.
            _filtering = !string.IsNullOrEmpty(_filter);
            RebuildViewIfNeeded();
        }

        // Сцена перезагружается после смерти, бутстраппер собирает новый сервис,
        // а панель это переживает. Снимок от прошлой сессии показывать как
        // текущий нельзя.
        private void DetectServiceChange(ICloudSaveService service)
        {
            if (ReferenceEquals(service, _lastService))
                return;

            _lastService = service;
            _snapshot = null;
            _groups = null;
            _viewDirty = true;
            _pendingDeleteKey = null;
            _expandedRowKey = null;
            _pendingGroupToggle = null;
            _pendingRowToggle = null;
            _refreshRequested = false;
            _status = service == null ? "service is not there yet" : "not read yet";
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("cloud save (service cache)", _title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(_collapsed ? "+" : "-", GUILayout.Width(24f)))
            {
                _pendingCollapseToggle = true;
                _pendingDeleteKey = null;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawBody(ICloudSaveService service)
        {
            GUILayout.Label(_status, _label);

            if (service == null)
            {
                GUILayout.Label("service is not there yet (still starting up?)", _label);
                return;
            }

            if (!(service is ICloudSaveBulkAccess))
            {
                GUILayout.Label("bulk read not available for this service", _label);
                return;
            }

            DrawToolbar();
            DrawWriteNotice();
            DrawGroups(service);
        }

        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("filter", _label, GUILayout.Width(34f));
            string filter = GUILayout.TextField(_filter ?? string.Empty);
            GUILayout.EndHorizontal();

            if (!string.Equals(filter, _filter, StringComparison.Ordinal))
            {
                _filter = filter;
                _viewDirty = true;
                _pendingDeleteKey = null;
            }

            // Не скрываем кнопки, а гасим — так видно, что они есть.
            GUI.enabled = !_busy;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("refresh", GUILayout.Height(HeaderButtonHeight)))
            {
                _refreshRequested = true;
                _pendingDeleteKey = null;
            }
            if (GUILayout.Button("dump to console", GUILayout.Height(HeaderButtonHeight)))
            {
                DumpToConsole();
                _pendingDeleteKey = null;
            }
            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }

        private void DrawWriteNotice()
        {
            if (!string.IsNullOrEmpty(_writeScope))
                GUILayout.Label($"delete writes: {_writeScope}", _label);

            GUILayout.Label(
                "deleted keys can be rewritten by running systems (stats and wallet auto-save on a timer) - reload the scene to see the real effect",
                _label);
        }

        private void RebuildViewIfNeeded()
        {
            bool sameFilter = string.Equals(_builtForFilter, _filter, StringComparison.Ordinal);
            if (!_viewDirty && _groups != null && sameFilter)
                return;

            _groups = CloudSaveDebugView.Build(_snapshot, _filter);
            _builtForFilter = _filter;
            _viewDirty = false;
        }

        private void DrawGroups(ICloudSaveService service)
        {
            if (_groups == null)
            {
                GUILayout.Label("nothing read yet", _label);
                return;
            }

            int total = _snapshot?.Count ?? 0;
            GUILayout.Label($"{total} keys in cache, {_groups.Count} groups shown", _label);

            _scroll = GUILayout.BeginScrollView(_scroll);

            foreach (CloudSaveGroup group in _groups)
            {
                // При активном фильтре группы раскрыты сами: иначе отфильтровал
                // и смотришь на пустой список.
                bool expanded = _filtering || _expandedGroups.Contains(group.Name);
                string mark = expanded ? "-" : "+";

                if (GUILayout.Button($"{mark} {group.Name}  ({group.Rows.Count})",
                        GUILayout.Height(RowButtonHeight)))
                {
                    _pendingGroupToggle = group.Name;
                    _pendingDeleteKey = null;
                }

                if (!expanded)
                    continue;

                int shown = 0;
                foreach (CloudSaveRow row in group.Rows)
                {
                    if (shown >= MaxRenderedRows)
                    {
                        GUILayout.Label($"   ... {group.Rows.Count - shown} more, narrow the filter", _label);
                        break;
                    }

                    DrawRow(service, row);
                    shown++;
                }
            }

            GUILayout.EndScrollView();
        }

        private void DrawRow(ICloudSaveService service, CloudSaveRow row)
        {
            bool rowExpanded = string.Equals(_expandedRowKey, row.Key, StringComparison.Ordinal);

            GUILayout.BeginHorizontal();

            if (GUILayout.Button(row.Key, _keyButton))
            {
                _pendingRowToggle = row.Key;
                _pendingDeleteKey = null;
            }

            GUI.enabled = !_busy;

            // Удаление в два клика: первый вооружает, второй по той же строке
            // выполняет. Диалога нет намеренно — лишней зависимости не хочется.
            bool armed = string.Equals(_pendingDeleteKey, row.Key, StringComparison.Ordinal);
            if (GUILayout.Button(armed ? "del?" : "del",
                    GUILayout.Width(DeleteButtonWidth), GUILayout.Height(RowButtonHeight)))
            {
                if (armed)
                {
                    _pendingDeleteKey = null;
                    _ = DeleteAsync(service, row.Key);
                }
                else
                {
                    _pendingDeleteKey = row.Key;
                }
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            string value = rowExpanded
                ? CloudSaveDebugView.Describe(row.Value, int.MaxValue)
                : CloudSaveDebugView.Describe(row.Value, ValuePreviewLength);

            GUILayout.Label($"   {value}", _label);
            GUILayout.Label($"   {row.Timestamp}", _label);
        }

        private async Task RefreshAsync()
        {
            if (!(_source?.Invoke() is ICloudSaveBulkAccess bulk))
            {
                _status = "bulk read not available";
                return;
            }

            _busy = true;
            _status = "reading...";
            try
            {
                IReadOnlyDictionary<string, CloudSaveEntry> snapshot = await bulk.SnapshotAsync();
                _snapshot = snapshot;
                _viewDirty = true;
                _status = $"{snapshot.Count} keys read";
            }
            catch (ObjectDisposedException)
            {
                // Сервис уничтожен вместе с прошлой сценой, а панель это
                // переживает. Терять нечего, шуметь в консоль незачем.
                _snapshot = null;
                _groups = null;
                _viewDirty = true;
                _status = "service was disposed (scene reload?) - press refresh";
            }
            catch (Exception ex)
            {
                _status = $"read failed: {ex.Message}";
                Debug.LogWarning($"{nameof(CloudSaveDebugPanel)}: snapshot read failed: {ex.Message}");
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task DeleteAsync(ICloudSaveService service, string key)
        {
            _busy = true;
            _status = $"deleting '{key}'...";
            try
            {
                await service.DeleteKeyAsync(new CloudSaveKey(key));
                _status = $"deleted '{key}'";
                Debug.Log($"{nameof(CloudSaveDebugPanel)}: deleted cloud save key '{key}'.");
            }
            catch (ObjectDisposedException)
            {
                _status = "service was disposed (scene reload?) - delete skipped";
            }
            catch (Exception ex)
            {
                _status = $"delete failed: {ex.Message}";
                Debug.LogWarning($"{nameof(CloudSaveDebugPanel)}: delete of '{key}' failed: {ex.Message}");
            }
            finally
            {
                _busy = false;
            }

            _refreshRequested = true;
        }

        // Отдельным сообщением на группу: одна огромная строка в консоли
        // браузера обрезается.
        private void DumpToConsole()
        {
            if (_snapshot == null || _snapshot.Count == 0)
            {
                Debug.Log($"{nameof(CloudSaveDebugPanel)}: cache is empty, nothing to dump.");
                return;
            }

            IReadOnlyList<CloudSaveGroup> groups = CloudSaveDebugView.Build(_snapshot, null);
            Debug.Log($"{nameof(CloudSaveDebugPanel)}: {_snapshot.Count} keys in the service cache, {groups.Count} groups.");

            foreach (CloudSaveGroup group in groups)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[{group.Name}] {group.Rows.Count} keys");
                foreach (CloudSaveRow row in group.Rows)
                    sb.AppendLine($"  {row.Key} = {CloudSaveDebugView.Describe(row.Value, int.MaxValue)}   ({row.Timestamp})");

                Debug.Log(sb.ToString());
            }
        }

        private void EnsureStyles()
        {
            if (_label != null)
                return;

            _label = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            _keyButton = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true, fontStyle = FontStyle.Bold };
        }
    }
}
