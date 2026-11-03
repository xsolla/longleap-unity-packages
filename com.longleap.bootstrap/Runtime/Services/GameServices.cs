using System;
using System.Collections.Generic;
using UnityEngine;

namespace LongLeap.Bootstrap
{
    /// <summary>
    /// Access point to the services for objects living in a scene.
    ///
    /// The game's entry point owns the container and publishes it here with <see cref="Bind"/>.
    /// A scene can wake up before loading has finished (for example, the game was
    /// started straight from the game scene), so a service is best taken with
    /// <see cref="WhenAvailable{TContract}"/> rather than in Awake, and its absence tolerated.
    /// </summary>
    public static class GameServices
    {
        private static readonly List<Waiter> Waiters = new List<Waiter>();

        /// <summary>The service container, or null if the entry point has not published one yet.</summary>
        public static IServiceContainer Container { get; private set; }

        /// <summary>Publish the container. Called by the game's entry point when it starts.</summary>
        public static void Bind(IServiceContainer container)
        {
            if (ReferenceEquals(Container, container)) return;
            if (Container != null) Container.Registered -= OnRegistered;

            Container = container;
            if (Container == null) return;

            Container.Registered += OnRegistered;
            CompleteWaiters();
        }

        /// <summary>Withdraw the container, if it is the one published. Called when the entry point dies.</summary>
        public static void Unbind(IServiceContainer container)
        {
            if (container == null || !ReferenceEquals(Container, container)) return;

            Container.Registered -= OnRegistered;
            Container = null;
        }

        /// <summary>Get a service if it is already registered.</summary>
        public static bool TryGet<TContract>(out TContract service) where TContract : class
        {
            var container = Container;
            if (container != null)
                return container.TryResolve(out service);

            service = null;
            return false;
        }

        /// <summary>Get a service. Throws if it does not exist.</summary>
        public static TContract Get<TContract>() where TContract : class
        {
            if (Container == null)
                throw new InvalidOperationException("No service container is bound yet.");

            return Container.Resolve<TContract>();
        }

        /// <summary>
        /// Calls <paramref name="callback"/> once with the service: right away if it is
        /// already registered, otherwise as soon as a loading task registers it. Without a
        /// container (a sandbox scene) the callback simply never comes.
        /// Dispose the result in OnDestroy to stop waiting — safe after the callback too.
        /// </summary>
        public static IDisposable WhenAvailable<TContract>(Action<TContract> callback) where TContract : class
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            var waiter = new Waiter<TContract>(callback);
            if (!waiter.TryComplete(Container))
                Waiters.Add(waiter);
            return waiter;
        }

        private static void OnRegistered(Type contract) => CompleteWaiters();

        private static void CompleteWaiters()
        {
            var container = Container;
            if (container == null) return;

            // A callback may start waiting for something else or stop another waiter.
            foreach (var waiter in Waiters.ToArray())
            {
                if (Waiters.Contains(waiter) && waiter.TryComplete(container))
                    Waiters.Remove(waiter);
            }
        }

        private abstract class Waiter : IDisposable
        {
            public abstract bool TryComplete(IServiceContainer container);

            public void Dispose() => Waiters.Remove(this);
        }

        private sealed class Waiter<TContract> : Waiter where TContract : class
        {
            private Action<TContract> _callback;

            public Waiter(Action<TContract> callback) => _callback = callback;

            public override bool TryComplete(IServiceContainer container)
            {
                if (_callback == null) return true;
                if (container == null || !container.TryResolve(out TContract service)) return false;

                var callback = _callback;
                _callback = null;
                try
                {
                    callback(service);
                }
                catch (Exception ex)
                {
                    // Runs inside the loading task that registered the service: a scene
                    // object's mistake must not fail that task.
                    Debug.LogException(ex);
                }

                return true;
            }
        }

        // Statics survive entering Play Mode without a domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Container = null;
            Waiters.Clear();
        }
    }
}
