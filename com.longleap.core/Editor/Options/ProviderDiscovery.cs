using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LongLeap.Core.Editor
{
    // Общий сбор поставщиков: настроек, пресетов, способов сборки,
    // предупреждений. Четыре одинаковых цикла с одинаковыми оговорками про
    // конструктор и try/catch — это ровно та дубликация, из-за которой
    // SetDefine в проекте существовал в четырёх видах.
    internal static class ProviderDiscovery
    {
        // Имя типа-поставщика возвращаем рядом с элементом: без него сообщение
        // о дублирующемся Id не говорит, КОГО править.
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

                // Каждый поставщик в своём try: один сломанный описатель не
                // должен оставить окно пустым.
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
