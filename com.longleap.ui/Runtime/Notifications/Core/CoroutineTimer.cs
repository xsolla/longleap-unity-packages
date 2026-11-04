using System;
using System.Collections;
using UnityEngine;

namespace LongLeap.Notifications
{
    public sealed class CoroutineTimer : ITimer
    {
        private readonly MonoBehaviour _host;
        private Coroutine _coroutine;
        private Action _onElapsed;

        public CoroutineTimer(MonoBehaviour host)
        {
            _host = host;
        }

        public void Start(TimeSpan duration, Action onElapsed)
        {
            Cancel();
            _onElapsed = onElapsed;
            _coroutine = _host.StartCoroutine(CountdownRoutine(duration));
        }

        public void Cancel()
        {
            if (_coroutine != null)
            {
                _host.StopCoroutine(_coroutine);
                _coroutine = null;
            }
            _onElapsed = null;
        }

        private IEnumerator CountdownRoutine(TimeSpan duration)
        {
            yield return new WaitForSecondsRealtime((float)duration.TotalSeconds);
            var callback = _onElapsed;
            _onElapsed = null;
            _coroutine = null;
            callback?.Invoke();
        }
    }
}
