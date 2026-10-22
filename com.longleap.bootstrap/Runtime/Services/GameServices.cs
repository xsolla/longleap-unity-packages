using UnityEngine;

namespace LongLeap.Bootstrap
{
    /// <summary>
    /// Access point to the services for objects living in a scene.
    ///
    /// The game's entry point owns the container and publishes it here with <see cref="Bind"/>.
    /// A scene can wake up before loading has finished (for example, the game was
    /// started straight from the game scene), so a service is best taken lazily —
    /// on first use rather than in Awake — and its absence tolerated.
    /// </summary>
    public static class GameServices
    {
        /// <summary>The service container, or null if the entry point has not published one yet.</summary>
        public static IServiceContainer Container { get; private set; }

        /// <summary>Publish the container. Called by the game's entry point when it starts.</summary>
        public static void Bind(IServiceContainer container) => Container = container;

        /// <summary>Withdraw the container, if it is the one published. Called when the entry point dies.</summary>
        public static void Unbind(IServiceContainer container)
        {
            if (ReferenceEquals(Container, container))
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
                throw new System.InvalidOperationException("No service container is bound yet.");

            return Container.Resolve<TContract>();
        }

        // Statics survive entering Play Mode without a domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Container = null;
    }
}
