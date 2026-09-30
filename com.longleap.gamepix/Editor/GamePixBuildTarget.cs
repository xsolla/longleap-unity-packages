using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LongLeap.Core.Editor;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace LongLeap.GamePix.Editor
{
    // Портальная сборка: её делает вендорный билдер плагина, а не
    // BuildPipeline. Он сам подменяет путь вывода (html5) и сам ставит
    // Compression Format = Disabled — .gpx сжимает себя сам.
    //
    // Точку входа ИЩЕМ, а не угадываем. Причина простая: папки
    // Assets/Plugins/GamePix в репозитории сейчас нет вовсе, так что имя типа
    // вендорного билдера неизвестно, а угаданное имя сломалось бы молча.
    // Ищем в таком порядке:
    //
    //   1. пункт меню со словом Build, объявленный сборкой, исходники которой
    //      лежат под Assets/Plugins/GamePix. Самый надёжный способ: пункт меню
    //      у вендора обязан быть для людей, и путь меню меняется гораздо реже
    //      внутренних имён типов;
    //   2. публичный статический метод без аргументов с именем Build/BuildGame/
    //      BuildHtml5/BuildAll на типе, в имени которого есть Builder или
    //      UnityConfigurator — именно эти имена названы в комментариях
    //      GamePixPluginMenu и GamePixIndexPostprocessor;
    //   3. не нашли — способ сборки недоступен, и в окне написано, что именно
    //      искали.
    //
    // Способ поиска сборок плагина по исходникам взят у
    // XsollaIdCallbackPagePostprocessor: в проекте это уже проверенный приём.
    internal sealed class GamePixBuildTarget : IProjectBuildTargetProvider
    {
        private static readonly string[] EntryMethodNames =
        {
            "Build", "BuildGame", "BuildHtml5", "BuildAll",
        };

        private static readonly string[] EntryTypeHints =
        {
            "Builder", "UnityConfigurator",
        };

        public IEnumerable<ProjectBuildTarget> GetBuildTargets()
        {
            yield return new ProjectBuildTarget(
                "gamepix.vendor",
                DescribeEntry(),
                100,
                Build,
                isApplicable: state => state != null
                                       && state.IsDefineOn(GamePixOptions.DefineSymbol),
                availability: Availability,
                description:
                "The vendor GamePix builder. It picks its own output folder (html5) and forces " +
                "Compression Format = Disabled, because .gpx compresses itself.");
        }

        private static string DescribeEntry()
        {
            string menuPath = FindVendorMenuItem();

            if (menuPath != null)
                return $"GamePix: {menuPath}";

            return FindVendorMethod() != null ? "GamePix plugin builder" : "GamePix plugin";
        }

        private static ProjectBuildTargetAvailability Availability()
        {
            if (!GamePixAsmdefConstraints.IsPluginVisible())
            {
                return ProjectBuildTargetAvailability.Blocked(
                    $"GAMEPIX is on but the plugin is not installed at " +
                    $"{GamePixAsmdefConstraints.PluginPath}.");
            }

            if (FindVendorMenuItem() != null || FindVendorMethod() != null)
                return ProjectBuildTargetAvailability.Available;

            return ProjectBuildTargetAvailability.Blocked(
                "The GamePix plugin is installed but no build entry point was found: expected a " +
                $"[MenuItem] containing \"Build\" declared under {GamePixAsmdefConstraints.PluginPath}, " +
                "or a public static Build() on a *Builder type there. Run the plugin's own build " +
                "menu item instead, and add its path to GamePixBuildTarget.");
        }

        private static void Build()
        {
            string menuPath = FindVendorMenuItem();

            if (menuPath != null)
            {
                Debug.Log($"Build Manager: running the GamePix plugin build item \"{menuPath}\".");

                if (!EditorApplication.ExecuteMenuItem(menuPath))
                {
                    Debug.LogError(
                        $"Build Manager: the menu item \"{menuPath}\" refused to run. " +
                        "Run it from the menu bar to see why.");
                }

                return;
            }

            MethodInfo method = FindVendorMethod();

            if (method == null)
            {
                Debug.LogError(
                    "Build Manager: no GamePix build entry point was found. " +
                    "Run the plugin's own build menu item.");
                return;
            }

            Debug.Log(
                $"Build Manager: calling {method.DeclaringType?.FullName}.{method.Name} " +
                "from the GamePix plugin.");

            try
            {
                method.Invoke(null, null);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Build Manager: the GamePix plugin builder threw. {exception}");
            }
        }

        // Имена сборок, чьи исходники лежат под папкой плагина.
        private static HashSet<string> PluginAssemblyNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            string prefix = GamePixAsmdefConstraints.PluginPath + "/";

            foreach (UnityEditor.Compilation.Assembly assembly
                     in CompilationPipeline.GetAssemblies(AssembliesType.Editor))
            {
                foreach (string file in assembly.sourceFiles)
                {
                    if (file.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        names.Add(assembly.name);
                        break;
                    }
                }
            }

            return names;
        }

        private static string FindVendorMenuItem()
        {
            HashSet<string> plugin = PluginAssemblyNames();

            if (plugin.Count == 0)
                return null;

            string best = null;

            foreach (MethodInfo method in TypeCache.GetMethodsWithAttribute<MenuItem>())
            {
                if (!plugin.Contains(method.DeclaringType?.Assembly.GetName().Name))
                    continue;

                foreach (MenuItem attribute in method.GetCustomAttributes<MenuItem>())
                {
                    if (attribute.validate)
                        continue;

                    if (attribute.menuItem == null
                        || attribute.menuItem.IndexOf("build", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    // Самый короткий путь — почти наверняка основной пункт
                    // сборки, а не его вариация.
                    if (best == null || attribute.menuItem.Length < best.Length)
                        best = attribute.menuItem;
                }
            }

            return best;
        }

        private static MethodInfo FindVendorMethod()
        {
            HashSet<string> plugin = PluginAssemblyNames();

            if (plugin.Count == 0)
                return null;

            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!plugin.Contains(assembly.GetName().Name))
                    continue;

                foreach (Type type in SafeTypes(assembly))
                {
                    if (!EntryTypeHints.Any(hint =>
                            type.Name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0))
                        continue;

                    foreach (string name in EntryMethodNames)
                    {
                        MethodInfo method = type.GetMethod(
                            name,
                            BindingFlags.Public | BindingFlags.Static,
                            null,
                            Type.EmptyTypes,
                            null);

                        if (method != null)
                            return method;
                    }
                }
            }

            return null;
        }

        private static IEnumerable<Type> SafeTypes(System.Reflection.Assembly assembly)
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
    }
}
