using System;
using System.Collections.Generic;
using UnityEngine;

namespace LongLeap.Bootstrap
{
    /// <summary>
    /// Dictionary-backed implementation of <see cref="IServiceContainer"/>. Lives as long
    /// as the game's entry point. On Dispose it shuts down every service implementing
    /// <see cref="IDisposable"/>, in reverse registration order, each instance once
    /// even if it is registered under several contracts.
    /// </summary>
    public sealed class ServiceContainer : IServiceContainer, IDisposable
    {
        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
        private readonly List<object> _registrationOrder = new List<object>();
        private readonly HashSet<Type> _hintedContracts = new HashSet<Type>();
        private bool _disposed;

        public event Action<Type> Registered;

        public void Register<TContract>(TContract service) where TContract : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            ThrowIfDisposed();

            var contract = typeof(TContract);
            if (_services.ContainsKey(contract))
                throw new InvalidOperationException($"Service '{contract.Name}' is already registered.");

            _services.Add(contract, service);
            if (!IsTracked(service))
                _registrationOrder.Add(service);
            Debug.Log($"[Services] + {contract.Name} ({service.GetType().Name})");

            Registered?.Invoke(contract);
        }

        public TContract Resolve<TContract>() where TContract : class
        {
            if (TryResolve(out TContract service)) return service;

            var contract = typeof(TContract);
            throw new KeyNotFoundException(
                $"Service '{contract.Name}' is not registered. " +
                (FindRegisteredAsOther(contract) ??
                 "Check that the loading task creating it is in the pipeline and ran earlier."));
        }

        public bool TryResolve<TContract>(out TContract service) where TContract : class
        {
            if (_services.TryGetValue(typeof(TContract), out var raw))
            {
                service = (TContract)raw;
                return true;
            }

            // Absence is a normal answer here, but not when the service is in fact
            // registered under another contract — say so once.
            var contract = typeof(TContract);
            if (!_hintedContracts.Contains(contract))
            {
                var hint = FindRegisteredAsOther(contract);
                if (hint != null)
                {
                    _hintedContracts.Add(contract);
                    Debug.LogWarning($"[Services] '{contract.Name}' is not registered. {hint}");
                }
            }

            service = null;
            return false;
        }

        public bool Contains<TContract>() where TContract : class => Contains(typeof(TContract));

        public bool Contains(Type contract) => contract != null && _services.ContainsKey(contract);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Registered = null;

            for (int i = _registrationOrder.Count - 1; i >= 0; i--)
            {
                if (_registrationOrder[i] is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Services] error disposing '{disposable.GetType().Name}': {ex.Message}");
                    }
                }
            }

            _registrationOrder.Clear();
            _services.Clear();
        }

        private bool IsTracked(object service)
        {
            foreach (var tracked in _registrationOrder)
                if (ReferenceEquals(tracked, service)) return true;
            return false;
        }

        /// <summary>A registered service that implements <paramref name="contract"/> under a different key.</summary>
        private string FindRegisteredAsOther(Type contract)
        {
            foreach (var pair in _services)
            {
                if (pair.Key != contract && contract.IsInstanceOfType(pair.Value))
                    return $"'{pair.Value.GetType().Name}' implements it but is registered as '{pair.Key.Name}' — " +
                           $"register it with Register<{contract.Name}>(...).";
            }

            return null;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ServiceContainer));
        }
    }
}
