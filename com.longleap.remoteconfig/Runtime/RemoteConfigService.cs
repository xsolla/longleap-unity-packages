using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using LongLeap.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace LongLeap.RemoteConfig
{
    public sealed class RemoteConfigService : IRemoteConfigService, IDisposable
    {
        private readonly IRemoteConfigProvider _provider;
        private readonly IPlayerPrefs _playerPrefs;
        private Dictionary<string, string> _parameters;
        private IReadOnlyList<AbExperimentInfo> _experiments;
        private Task _fetchTask;
        private readonly HashSet<string> _missingKeysLogged;
        private bool _disposed;

        public RemoteConfigService(IRemoteConfigProvider provider, IPlayerPrefs cache)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _playerPrefs = cache ?? throw new ArgumentNullException(nameof(cache));
            _parameters = new Dictionary<string, string>();
            _experiments = Array.Empty<AbExperimentInfo>();
            _missingKeysLogged = new HashSet<string>();

            LoadCache();
        }

        public async Task FetchAsync()
        {
            if (_disposed) return;

            // Не потокобезопасен — рассчитан на вызовы из главного потока Unity
            var tcs = new TaskCompletionSource<bool>();
            var previousTask = _fetchTask;
            _fetchTask = tcs.Task;

            try
            {
                if (previousTask != null && !previousTask.IsCompleted)
                {
                    await previousTask;
                    tcs.SetResult(true);
                    return;
                }

                var result = await _provider.FetchAsync();

                if (_disposed)
                {
                    tcs.SetResult(false);
                    return;
                }

                if (result.IsSuccess && result.Parameters != null)
                {
                    _parameters = new Dictionary<string, string>(result.Parameters);
                    _experiments = result.Experiments != null ? result.Experiments : Array.Empty<AbExperimentInfo>();

                    var json = JsonConvert.SerializeObject(_parameters);
                    _playerPrefs.SetString("rc_cache", json);
                    _playerPrefs.Save();
                }
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(RemoteConfigService)}: ошибка при получении Remote Config: {ex.Message}");
                tcs.SetResult(false);
            }
        }

        public string GetString(string key, string defaultValue)
        {
            if (_disposed) return defaultValue;
            if (_parameters.TryGetValue(key, out var value)) return value;
            LogMissingKeyOnce(key);
            return defaultValue;
        }

        public int GetInt(string key, int defaultValue)
        {
            if (_disposed) return defaultValue;
            if (_parameters.TryGetValue(key, out var value))
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
                    return result;
                Debug.LogWarning($"{nameof(RemoteConfigService)}: не удалось преобразовать '{key}'='{value}' в int, используется значение по умолчанию {defaultValue}");
                return defaultValue;
            }
            LogMissingKeyOnce(key);
            return defaultValue;
        }

        public float GetFloat(string key, float defaultValue)
        {
            if (_disposed) return defaultValue;
            if (_parameters.TryGetValue(key, out var value))
            {
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
                    return result;
                Debug.LogWarning($"{nameof(RemoteConfigService)}: не удалось преобразовать '{key}'='{value}' в float, используется значение по умолчанию {defaultValue}");
                return defaultValue;
            }
            LogMissingKeyOnce(key);
            return defaultValue;
        }

        public bool GetBool(string key, bool defaultValue)
        {
            if (_disposed) return defaultValue;
            if (_parameters.TryGetValue(key, out var value))
            {
                if (bool.TryParse(value, out var result)) return result;
                if (value == "1") return true;
                if (value == "0") return false;
                Debug.LogWarning($"{nameof(RemoteConfigService)}: не удалось преобразовать '{key}'='{value}' в bool, используется значение по умолчанию {defaultValue}");
                return defaultValue;
            }
            LogMissingKeyOnce(key);
            return defaultValue;
        }

        public IReadOnlyList<AbExperimentInfo> ActiveExperiments => _experiments;

        private void LogMissingKeyOnce(string key)
        {
            if (_missingKeysLogged.Add(key))
                Debug.LogWarning($"{nameof(RemoteConfigService)}: ключ '{key}' не найден в Remote Config, используется значение по умолчанию");
        }

        private void LoadCache()
        {
            var json = _playerPrefs.GetString("rc_cache", "");
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                _parameters = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
            catch (Exception)
            {
                Debug.LogWarning($"{nameof(RemoteConfigService)}: кэш Remote Config повреждён, используется значения по умолчанию");
                _parameters = new Dictionary<string, string>();
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _parameters.Clear();
            _experiments = Array.Empty<AbExperimentInfo>();
            _missingKeysLogged.Clear();
            _fetchTask = null;
        }
    }
}
