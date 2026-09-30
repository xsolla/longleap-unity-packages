using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LongLeap.BuildManager.Editor
{
    // Сборка контента Addressables перед сборкой плеера.
    //
    // Зачем вообще: в проекте m_BuildAddressablesWithPlayerBuild = 0 и ни одна
    // строка кода не вызывает BuildPlayerContent. То есть бандлы не собирает
    // НИКТО, и сборка плеера уносит с собой те, что лежали с прошлого раза.
    //
    // Почему через рефлексию: пакет окна обязан собираться и в проекте без
    // Addressables. Жёсткая зависимость сделала бы Addressables обязательным
    // для всех, кто возьмёт Build Manager.
    //
    // Решения на краях:
    //   пакета нет           -> молча пропускаем, это нормальный проект;
    //   настроек нет         -> пропускаем с сообщением;
    //   API не нашёлся       -> ОТМЕНЯЕМ сборку. Молча уехавшие устаревшие
    //                           бандлы хуже упавшей сборки, и только громкий
    //                           отказ заставит починить рефлексию после
    //                           обновления пакета;
    //   Unity соберёт сама   -> пропускаем, чтобы не делать работу дважды.
    internal static class AddressablesPrepass
    {
        private const string EditorAssembly = "Unity.Addressables.Editor";

        private const string SettingsType =
            "UnityEditor.AddressableAssets.Settings.AddressableAssetSettings";

        // Внимание: этот тип лежит в UnityEditor.AddressableAssets, а не в
        // ...AddressableAssets.Settings, куда его тянет положить соседство с
        // AddressableAssetSettings. Ровно на этом прошлая версия и падала.
        private const string DefaultObjectType =
            "UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject";

        public enum Outcome
        {
            NotInstalled,
            NoSettings,
            AlreadyBuiltWithPlayer,
            Built,
            Failed,
        }

        public static Outcome Run(out string message)
        {
            message = null;

            Assembly assembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == EditorAssembly);

            if (assembly == null)
                return Outcome.NotInstalled;

            Type defaultObject = Resolve(assembly, DefaultObjectType);
            Type settingsType = Resolve(assembly, SettingsType);

            if (defaultObject == null || settingsType == null)
            {
                message =
                    $"Addressables is installed but neither {DefaultObjectType} nor a type named " +
                    $"AddressableAssetSettingsDefaultObject / AddressableAssetSettings was found in " +
                    $"{EditorAssembly}. The package API moved; fix AddressablesPrepass.";
                return Outcome.Failed;
            }

            PropertyInfo exists = defaultObject.GetProperty(
                "SettingsExists", BindingFlags.Public | BindingFlags.Static);

            if (exists == null)
            {
                message =
                    $"Addressables is installed but {DefaultObjectType}.SettingsExists was not " +
                    "found. The package API moved; fix AddressablesPrepass.";
                return Outcome.Failed;
            }

            if (!(bool)exists.GetValue(null))
                return Outcome.NoSettings;

            if (BuildsWithPlayer(defaultObject, settingsType))
                return Outcome.AlreadyBuiltWithPlayer;

            // Берём перегрузку с out-параметром именно ради того, чтобы узнать
            // об ошибке: у версии без него результат никуда не возвращается.
            MethodInfo build = settingsType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "BuildPlayerContent"
                                     && m.GetParameters().Length == 1
                                     && m.GetParameters()[0].IsOut);

            if (build == null)
            {
                message =
                    $"Addressables is installed but {SettingsType}.BuildPlayerContent(out result) " +
                    "was not found. The package API moved; fix AddressablesPrepass.";
                return Outcome.Failed;
            }

            var arguments = new object[1];

            try
            {
                build.Invoke(null, arguments);
            }
            catch (Exception exception)
            {
                message = $"Addressables content build threw. {exception}";
                return Outcome.Failed;
            }

            object result = arguments[0];
            var error = result?.GetType()
                .GetProperty("Error", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(result) as string;

            if (!string.IsNullOrEmpty(error))
            {
                message = $"Addressables content build reported: {error}";
                return Outcome.Failed;
            }

            return Outcome.Built;
        }

        // Unity умеет собирать контент вместе с плеером сама. Если так
        // настроено — не дублируем: полная пересборка бандлов не бесплатна.
        private static bool BuildsWithPlayer(Type defaultObject, Type settingsType)
        {
            try
            {
                object settings = defaultObject
                    .GetProperty("Settings", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null);

                if (settings == null)
                    return false;

                object value = settingsType
                    .GetProperty("BuildAddressablesWithPlayerBuild",
                        BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(settings);

                // Имя члена перечисления, а не число: PlayerBuildOption лежит
                // в чужом пакете и её порядок нам не принадлежит.
                return value != null && value.ToString() == "BuildWithPlayer";
            }
            catch (Exception)
            {
                // Не смогли прочитать — соберём сами. Лишняя пересборка это
                // потерянное время, пропущенная — уехавшие устаревшие бандлы.
                return false;
            }
        }

        // Сначала полное имя, потом короткое.
        //
        // Пространство имён у этих двух типов уже один раз оказалось не тем,
        // что ожидалось (DefaultObject живёт НЕ рядом с Settings), и цена
        // ошибки — отменённая сборка. Поиск по короткому имени внутри той же
        // сборки переживает переезд между пространствами имён; имя самого типа
        // меняется гораздо реже.
        private static Type Resolve(Assembly assembly, string fullName)
        {
            if (assembly == null)
                return null;

            Type type = assembly.GetType(fullName);

            if (type != null)
                return type;

            string shortName = fullName.Substring(fullName.LastIndexOf('.') + 1);

            foreach (Type candidate in SafeTypes(assembly))
            {
                if (candidate.Name == shortName)
                    return candidate;
            }

            return null;
        }

        private static IEnumerable<Type> SafeTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(t => t != null);
            }
        }

        public static bool IsInstalled =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetName().Name == EditorAssembly);

        // Для замечания в окне: собирает ли Unity контент сама.
        public static bool WillUnityBuildContent()
        {
            Assembly assembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == EditorAssembly);

            Type defaultObject = Resolve(assembly, DefaultObjectType);
            Type settingsType = Resolve(assembly, SettingsType);

            return defaultObject != null
                   && settingsType != null
                   && BuildsWithPlayer(defaultObject, settingsType);
        }
    }
}
