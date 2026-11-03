using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace LongLeap.Bootstrap.Loading
{
    /// <summary>
    /// Loads an Addressable that may not exist yet: a missing key or a failed load gives
    /// null and a warning instead of an exception. A loaded asset stays in memory until
    /// the service container is disposed.
    /// </summary>
    public static class OptionalAddressable
    {
        /// <summary>
        /// Every asset loaded through this class, held until the container is disposed.
        /// One per container: the container keeps one service per type, so a holder per
        /// asset type would reject the second asset of the same type.
        /// </summary>
        private sealed class Leases : IDisposable
        {
            private readonly List<AsyncOperationHandle> _handles = new List<AsyncOperationHandle>();

            public static Leases Of(IServiceContainer services)
            {
                if (!services.TryResolve(out Leases leases))
                {
                    leases = new Leases();
                    services.Register(leases);
                }

                return leases;
            }

            public void Add(AsyncOperationHandle handle) => _handles.Add(handle);

            public void Dispose()
            {
                for (int i = _handles.Count - 1; i >= 0; i--)
                {
                    if (_handles[i].IsValid()) Addressables.Release(_handles[i]);
                }

                _handles.Clear();
            }
        }

        public static async Task<T> LoadAsync<T>(string key, LoadingContext context) where T : UnityEngine.Object
        {
            context.ThrowIfCancelled();
            AsyncOperationHandle<IList<IResourceLocation>> locations = default;
            AsyncOperationHandle<T> handle = default;
            var retained = false;
            try
            {
                // Optional content may not have been authored yet. A location lookup returns
                // an empty list for missing keys without reporting an InvalidKeyException.
                locations = Addressables.LoadResourceLocationsAsync(key, typeof(T));
                while (!locations.IsDone)
                    await Awaitable.NextFrameAsync(context.CancellationToken);
                context.ThrowIfCancelled();
                if (locations.Status != AsyncOperationStatus.Succeeded || locations.Result == null || locations.Result.Count == 0)
                {
                    Debug.LogWarning($"[Loading] optional asset '{key}' is unavailable; using the code-only fallback.");
                    return null;
                }

                handle = Addressables.LoadAssetAsync<T>(locations.Result[0]);
                while (!handle.IsDone)
                    await Awaitable.NextFrameAsync(context.CancellationToken);
                context.ThrowIfCancelled();
                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                {
                    Debug.LogWarning($"[Loading] optional asset '{key}' is unavailable; using the code-only fallback.");
                    return null;
                }
                Leases.Of(context.Services).Add(handle);
                retained = true;
                return handle.Result;
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                context.ThrowIfCancelled();
                Debug.LogWarning($"[Loading] optional asset '{key}' could not be loaded: {exception.Message}");
                return null;
            }
            finally
            {
                if (locations.IsValid()) Addressables.Release(locations);
                if (!retained && handle.IsValid()) Addressables.Release(handle);
            }
        }
    }
}
