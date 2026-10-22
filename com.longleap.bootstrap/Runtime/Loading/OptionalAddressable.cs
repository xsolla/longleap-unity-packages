using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace LongLeap.Bootstrap.Loading
{
    public static class OptionalAddressable
    {
        private sealed class Lease<T> : IDisposable where T : UnityEngine.Object
        {
            private AsyncOperationHandle<T> _handle;
            public Lease(AsyncOperationHandle<T> handle) => _handle = handle;
            public void Dispose()
            {
                if (_handle.IsValid()) Addressables.Release(_handle);
                _handle = default;
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
                {
                    context.ThrowIfCancelled();
                    await Task.Yield();
                }
                context.ThrowIfCancelled();
                if (locations.Status != AsyncOperationStatus.Succeeded || locations.Result == null || locations.Result.Count == 0)
                {
                    Debug.LogWarning($"[Loading] optional asset '{key}' is unavailable; using the code-only fallback.");
                    return null;
                }

                handle = Addressables.LoadAssetAsync<T>(locations.Result[0]);
                while (!handle.IsDone)
                {
                    context.ThrowIfCancelled();
                    await Task.Yield();
                }
                context.ThrowIfCancelled();
                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                {
                    Debug.LogWarning($"[Loading] optional asset '{key}' is unavailable; using the code-only fallback.");
                    return null;
                }
                context.Services.Register(new Lease<T>(handle));
                retained = true;
                return handle.Result;
            }
            catch (OperationCanceledException)
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
