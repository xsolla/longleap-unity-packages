using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace LongLeap.Localization
{
    public class LocalizationService : ILocalizationService
    {
        private readonly LocalizedStringTable[] _localizedTables;

        // Загруженные таблицы — строго по индексам _localizedTables, а не по
        // именам.
        //
        // Раньше кэш был словарём с ключом TableReference.TableCollectionName, и
        // именно на этом локализация «включалась через раз». Ссылки на таблицы в
        // префабе сохранены по GUID (так их пишет инспектор), а для GUID-ссылки
        // это свойство в рантайме возвращает имя ТОЛЬКО если SharedTableData этой
        // таблицы уже догрузилась (TableReference.SharedTableData: без
        // UNITY_EDITOR оно лишь заглядывает в StringDatabase.
        // SharedTableDataOperations и отдаёт null, если операция ещё не готова).
        // Имя приходило пустым — таблица не попадала в кэш совсем, и Resolve до
        // конца запуска отдавал сырые ключи. В редакторе имя достаётся из
        // AssetDatabase всегда, поэтому там всё выглядело исправным, а ломалось
        // только в сборке.
        private StringTable[] _tables;

        private MonoBehaviour _coroutineHost;

        // Номер актуальной загрузки таблиц. Растёт на каждой смене локали, чтобы
        // догрузившаяся старая загрузка не затёрла результат более свежей: язык
        // могут переключить дважды подряд (портал выставил свой, потом приехали
        // настройки игрока), и тогда в полёте оказываются две загрузки сразу.
        private int _reloadGeneration;

        // Идёт загрузка таблиц под новый язык: Resolve пока отдаёт прежний
        // перевод, а окна показывать рано.
        private bool _reloading;

        public event Action OnLanguageChanged;

        // Таблицы активного языка в кэше и ничего не грузится. Сервис создают уже
        // после первичной загрузки (LocalizationBootstrapper), поэтому на старте
        // готовы — «не готовы» бывает только на время смены языка.
        public bool IsReady => !_reloading;

        public LocalizationService(LocalizedStringTable[] localizedTables,
            StringTable[] preloaded = null)
        {
            _localizedTables = localizedTables ?? Array.Empty<LocalizedStringTable>();
            _tables = new StringTable[_localizedTables.Length];

            if (preloaded != null)
            {
                int count = Mathf.Min(preloaded.Length, _tables.Length);
                for (int i = 0; i < count; i++)
                    _tables[i] = preloaded[i];
            }

            // Подписка идёт через LocalizationSettings.Instance, а он без настроек
            // в проекте бросается NullReference — проверяем, иначе сервис не
            // создастся вовсе и бутстрап встанет на пустом месте.
            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged += OnUnityLocaleChanged;
        }

        public void SetCoroutineHost(MonoBehaviour host)
        {
            _coroutineHost = host;
        }

        public void Shutdown()
        {
            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged -= OnUnityLocaleChanged;

            // Загрузки в полёте объявляем устаревшими: их корутины живут на хосте,
            // который прямо сейчас умирает вместе со сценой, и их результат уже
            // никому не нужен.
            _reloadGeneration++;
            _reloading = false;

            _coroutineHost = null;
        }

        private void OnUnityLocaleChanged(Locale locale)
        {
            // Событие приходит и на наш собственный SwitchLanguage (кадром позже),
            // и на смену локали со стороны — например из инспектора Unity. Второй
            // проход по таблицам почти бесплатен: уже загруженные GetTableAsync
            // отдаёт готовым хендлом из кэша базы.
            StartReload();
        }

        // Перезагрузить таблицы под текущую локаль. Пока идёт загрузка, IsReady ==
        // false: по нему и ждут те, кому нельзя показывать текст раньше времени.
        private void StartReload()
        {
            _reloadGeneration++;

            // Грузить таблицы некому: хост не назначен, уже уничтожен вместе со
            // сценой либо выключен (на выключенном объекте StartCoroutine не
            // запускается — корутина не пошла бы, а мы навсегда остались бы «в
            // загрузке»). Старые таблицы при этом врут — в них прежний язык, —
            // так что бросаем их и говорим об этом вслух: тексты уедут в ключи, и
            // молча такое прятать нельзя.
            if (_coroutineHost == null || !_coroutineHost.gameObject.activeInHierarchy)
            {
                _tables = new StringTable[_localizedTables.Length];
                _reloading = false;

                Debug.LogWarning("[Localization] Язык сменился, но грузить таблицы " +
                    "некому (нет живого coroutine host) — тексты останутся ключами.");

                OnLanguageChanged?.Invoke();
                return;
            }

            _reloading = true;

            _coroutineHost.StartCoroutine(ReloadTables(_reloadGeneration));
        }

        private IEnumerator ReloadTables(int generation)
        {
            // Смена локали пересоздаёт операцию инициализации Unity — в неё входит
            // и предзагрузка таблиц нового языка. Ждём её первой: так «готово»
            // означает, что догрузилось всё, а не только то, что мы спросили сами.
            if (LocalizationSettings.HasSettings)
            {
                var initOp = LocalizationSettings.InitializationOperation;

                if (initOp.IsValid() && !initOp.IsDone)
                    yield return initOp;
            }

            // Грузим в отдельный массив и подменяем целиком, а не чистим старый
            // заранее: пока таблицы нового языка едут (на WebGL это несколько
            // кадров, а то и секунд), Resolve отдаёт прежний перевод, а не сырой
            // ключ. Иначе на экране успевают мигнуть «menu_play» вместо текста.
            var loaded = new StringTable[_localizedTables.Length];

            yield return LoadTables(_localizedTables, loaded);

            // Язык переключили ещё раз, пока мы грузились — наш результат устарел,
            // и подменять им таблицы нельзя: в них уже более свежий язык. Флаг
            // загрузки тоже не снимаем — его снимет та перезагрузка, что идёт
            // сейчас.
            if (generation != _reloadGeneration)
                yield break;

            _tables = loaded;
            _reloading = false;

            OnLanguageChanged?.Invoke();
        }

        public string Resolve(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            for (int i = 0; i < _tables.Length; i++)
            {
                var table = _tables[i];
                if (table == null)
                    continue;

                var entry = table.GetEntry(key);
                if (entry != null)
                {
                    var localized = entry.GetLocalizedString();
                    if (!string.IsNullOrEmpty(localized))
                        return localized;
                }
            }

            Debug.LogWarning($"[Localization] Missing translation for key '{key}'.");
            return key;
        }

        public string ResolveFormat(string key, params object[] args)
        {
            string resolved = Resolve(key);

            // Перевод — это данные из таблицы, и в нём может оказаться и лишняя
            // фигурная скобка, и меньше подстановок, чем аргументов. Ронять на
            // этом кадр нельзя: показываем строку как есть и кричим в лог.
            try
            {
                return string.Format(resolved, args);
            }
            catch (FormatException e)
            {
                Debug.LogWarning("[Localization] Не удалось подставить аргументы в " +
                    $"'{key}' ('{resolved}'): {e.Message}");

                return resolved;
            }
        }

        /// <summary>
        /// Переключает язык. false — такой локали нет среди доступных либо она уже
        /// выбрана, то есть перезагрузки таблиц не будет и сервис остаётся готовым.
        /// </summary>
        public bool SwitchLanguage(string localeCode)
        {
            if (string.IsNullOrEmpty(localeCode))
                return false;

            if (!LocalizationSettings.HasSettings)
            {
                Debug.LogWarning("[Localization] Нет LocalizationSettings — " +
                    $"язык '{localeCode}' не переключить.");
                return false;
            }

            var locales = LocalizationSettings.AvailableLocales.Locales;

            for (int i = 0; i < locales.Count; i++)
            {
                var locale = locales[i];
                if (locale == null || locale.Identifier.Code != localeCode)
                    continue;

                // Уже выбран — молча выходим: присваивание того же языка таблицы
                // не перезагружает и события не даёт, а нам важно вернуть false,
                // чтобы вызывающий не ждал загрузку, которой не будет.
                if (LocalizationSettings.SelectedLocale == locale)
                    return false;

                LocalizationSettings.SelectedLocale = locale;

                // Перезагрузку запускаем сами, не дожидаясь SelectedLocaleChanged:
                // Unity дёргает это событие не из сеттера — он пересоздаёт операцию
                // инициализации и, пока та не готова, отправляет событие из своей
                // корутины, то есть кадром позже (см.
                // LocalizationSettings.SendLocaleChangedEvents). Полагаясь на
                // событие, мы бы между переключением языка и началом загрузки
                // отвечали «всё готово» — ровно в это окно и успевали открыться
                // окна с текстом на прежнем языке или с ключами.
                StartReload();

                return true;
            }

            // Раньше молчали, и опечатка в коде языка выглядела как «локализация
            // не включилась»: язык не менялся, а в логе ни строчки.
            Debug.LogWarning($"[Localization] Локали '{localeCode}' нет среди " +
                "доступных — язык не переключён.");

            return false;
        }

        /// <summary>
        /// Грузит таблицы активной локали в <paramref name="target"/> — по тем же
        /// индексам, что и в <paramref name="tables"/>. Массив должен быть не
        /// короче: незагруженные позиции остаются null.
        /// </summary>
        public static IEnumerator LoadTables(
            LocalizedStringTable[] tables,
            StringTable[] target)
        {
            if (tables == null || target == null)
                yield break;

            int count = Mathf.Min(tables.Length, target.Length);

            for (int i = 0; i < count; i++)
            {
                var table = tables[i];
                if (table == null)
                    continue;

                var op = table.GetTableAsync();
                yield return op;

                if (op.IsValid() && op.Result != null)
                {
                    target[i] = op.Result;
                }
                else
                {
                    // Не загрузившаяся таблица — это молча пропавший перевод:
                    // Resolve по её ключам будет отдавать сами ключи. Таблицу
                    // называем по GUID из ссылки: имя (TableCollectionName) в
                    // рантайме доступно не всегда — из-за этого и был баг.
                    Debug.LogError("[Localization] Не удалось загрузить таблицу " +
                        $"#{i} (GUID {table.TableReference.TableCollectionNameGuid}).");
                }
            }
        }
    }
}
