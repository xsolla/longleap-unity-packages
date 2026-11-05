using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LongLeap.Core.Editor
{
    // Shared provider discovery for options, presets, build targets and
    // warnings: one loop with the constructor check and try/catch instead of
    // four copies.
    internal static class ProviderDiscovery
    {
        // The provider type name is returned with each item: without it a
        // duplicate-Id message would not say WHICH provider to fix.
        public static List<(TItem Item, string Provider)> Collect<TProvider, TItem>(
            Func<TProvider, IEnumerable<TItem>> select)
            where TProvider : class
            where TItem : class
        {
            var found = new List<(TItem, string)>();

            foreach (Type type in TypeCache.GetTypesDerivedFrom<TProvider>())
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                    continue;

                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    Debug.LogError(
                        $"Build options: {type.FullName} implements {typeof(TProvider).Name} but has " +
                        "no public parameterless constructor, so what it provides is ignored.");
                    continue;
                }

                // Each provider in its own try: one broken provider must not
                // leave the window empty.
                try
                {
                    var provider = (TProvider)Activator.CreateInstance(type);
                    IEnumerable<TItem> items = select(provider);

                    if (items == null)
                        continue;

                    foreach (TItem item in items)
                    {
                        if (item != null)
                            found.Add((item, type.FullName));
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"Build options: {type.FullName} failed and was skipped. {exception}");
                }
            }

            return found;
        }
    }
}
