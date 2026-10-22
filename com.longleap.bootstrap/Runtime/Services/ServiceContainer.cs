using System;
using System.Collections.Generic;
using UnityEngine;

namespace LongLeap.Bootstrap
{
    /// <summary>
    /// Dictionary-backed implementation of <see cref="IServiceContainer"/>. Lives as long
    /// as the game's entry point. On Dispose it shuts down every service implementing
    /// <see cref="IDisposable"/>, in reverse registration order.
    /// </summary>
    public sealed class ServiceContainer : IServiceContainer, IDisposable
    {
        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
        private readonly List<object> _registrationOrder = new List<object>();
        private bool _disposed;

        public void Register<TContract>(TContract service) where TContract : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            ThrowIfDisposed();

            var contract = typeof(TContract);
            if (_services.ContainsKey(contract))
                throw new InvalidOperationException($"Service '{contract.Name}' is already registered.");

            _services.Add(contract, service);
            _registrationOrder.Add(service);
            Debug.Log($"[Services] + {contract.Name} ({service.GetType().Name})");
        }

        public TContract Resolve<TContract>() where TContract : class
        {
            if (TryResolve(out TContract service)) return service;
            throw new KeyNotFoundException(
                $"Service '{typeof(TContract).Name}' is not registered. " +
                "Check that the loading task creating it is in the pipeline and ran earlier.");
        }

        public bool TryResolve<TContract>(out TContract service) where TContract : class
        {
            if (_services.TryGetValue(typeof(TContract), out var raw))
            {
                service = (TContract)raw;
                return true;
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

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ServiceContainer));
        }
    }
}
