using System;

namespace LongLeap.Bootstrap
{
    /// <summary>
    /// A minimal service container. Loading tasks put everything they created in here,
    /// and game code pulls it back out by interface.
    /// </summary>
    public interface IServiceContainer
    {
        /// <summary>Register an implementation under the contract <typeparamref name="TContract"/>.</summary>
        void Register<TContract>(TContract service) where TContract : class;

        /// <summary>Get a service. Throws if it is not registered.</summary>
        TContract Resolve<TContract>() where TContract : class;

        bool TryResolve<TContract>(out TContract service) where TContract : class;

        bool Contains<TContract>() where TContract : class;

        bool Contains(Type contract);
    }
}
