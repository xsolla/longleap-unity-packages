using System;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace LongLeap.UI
{
    /// <summary>
    /// Строка каталога окон в <see cref="WindowService"/>: префаб окна в проекте
    /// и правила, по которым сервис им распоряжается.
    ///
    /// Три режима получаются из двух галок:
    /// <list type="bullet">
    /// <item><b>Preload</b> — окно создаётся сразу при старте UI и остаётся в памяти.
    /// Для окон, которые должны открываться мгновенно, и тех, к которым код обращается
    /// синхронно через <see cref="IWindowService.TryGet{TWindow}"/>.</item>
    /// <item><b>ни одной галки</b> — окно создаётся при первом открытии и остаётся в памяти:
    /// повторное открытие мгновенное.</item>
    /// <item><b>Unload On Close</b> — окно создаётся при каждом открытии и выгружается
    /// при закрытии: экземпляр уничтожается, префаб отпускается из памяти.
    /// Для тяжёлых и редких окон.</item>
    /// </list>
    /// Обе галки вместе тоже допустимы: окно создаётся заранее для первого показа,
    /// а после первого закрытия выгружается и дальше живёт по правилу «создать при открытии».
    /// </summary>
    [Serializable]
    public sealed class WindowEntry
    {
        // Первое строковое поле: Unity берёт его подписью элемента списка в инспекторе.
        [Tooltip("Заполняется само по префабу — только подпись в списке.")]
        [SerializeField] private string _label;

        [Tooltip("Префаб окна в проекте (Addressable). Компонент окна — на корне префаба.")]
        [SerializeField] private AssetReferenceGameObject _prefab;

        [Tooltip("Тип окна. Заполняется само по префабу: окно адресуется типом, а префаб " +
                 "до загрузки не открыть — тип надо знать заранее.")]
        [SerializeField] private string _typeName;

        [Tooltip("Создать окно сразу при старте UI и держать в памяти.")]
        [SerializeField] private bool _preload;

        [Tooltip("Уничтожать окно и отпускать префаб при закрытии; создавать заново при открытии.")]
        [SerializeField] private bool _unloadOnClose;

        public AssetReferenceGameObject Prefab => _prefab;

        public string TypeName => _typeName;

        public bool Preload => _preload;

        public bool UnloadOnClose => _unloadOnClose;

        public string Label => string.IsNullOrEmpty(_label) ? _typeName : _label;

#if UNITY_EDITOR
        public WindowEntry()
        {
        }

        public WindowEntry(AssetReferenceGameObject prefab, bool preload, bool unloadOnClose)
        {
            _prefab = prefab;
            _preload = preload;
            _unloadOnClose = unloadOnClose;
            Refresh();
        }

        /// <summary>Подтянуть тип и подпись из префаба: вызывается при правке каталога в редакторе.</summary>
        public void Refresh()
        {
            var prefab = _prefab != null ? _prefab.editorAsset as GameObject : null;
            var window = prefab != null ? prefab.GetComponent<WindowBase>() : null;

            _typeName = window != null ? window.GetType().AssemblyQualifiedName : null;
            _label = window != null ? window.GetType().Name : prefab != null ? $"{prefab.name} (нет WindowBase)" : "—";
        }
#endif
    }
}
