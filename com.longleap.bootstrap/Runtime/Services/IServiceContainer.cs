using System;

namespace LongLeap.Bootstrap
{
    /// <summary>
    /// A minimal service container. Loading tasks put everything they created in here,
    /// and game code pulls it back out by interface.
    /// </summary>
    public interface IServiceContainer
    {
        /// <summary>A service was registered under this contract. Main thread.</summary>
        event Action<Type> Registered;

        /// <summary>
        /// Register an implementation under the contract <typeparamref name="TContract"/>.
        /// Without an explicit type argument the contract is the concrete type of the
        /// argument: write <c>Register&lt;IFoo&gt;(foo)</c> when callers ask for the interface.
        /// </summary>
        void Register<TContract>(TContract service) where TContract : class;

        /// <summary>Get a service. Throws if it is not registered.</summary>
        TContract Resolve<TContract>() where TContract : class;

        bool TryResolve<TContract>(out TContract service) where TContract : class;

        bool Contains<TContract>() where TContract : class;

        bool Contains(Type contract);
    }
}
