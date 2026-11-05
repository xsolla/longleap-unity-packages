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
    // The portal build target: the build is made by the vendor plugin's
    // builder, not by BuildPipeline. It picks its own output path (html5) and
    // forces Compression Format = Disabled — the .gpx compresses itself.
    //
    // The entry point is SEARCHED for, not hard-coded: the package references
    // neither the plugin's editor assembly (it is constrained by GAMEPIX) nor
    // a particular plugin version, and a hard-coded type name would break
    // silently on a plugin update. Search order:
    //
    //   1. a menu item containing "Build", declared by an assembly whose
    //      sources live under Assets/Plugins/GamePix. The most reliable way:
    //      the vendor must keep a menu item for people, and menu paths change
    //      far less often than internal type names. The safe build "No override
    //      textures\sounds" is preferred, otherwise the shortest path;
    //   2. a public static parameterless Build/BuildGame/BuildHtml5/BuildAll on
    //      a type whose name contains Builder or UnityConfigurator (the vendor
    //      type names);
    //   3. nothing found — the target is unavailable, and the window says
    //      exactly what was searched for.
    //
    // Finding plugin assemblies by their source files is the same technique
    // XsollaIdCallbackPagePostprocessor uses.
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

        // The vendor item "GamePix/Safe builds/Build and Run (No override
        // textures\sounds)". The plain "Build and Run" rewrites the import
        // settings of textures, atlases and sounds to uncompressed
        // (AssetsCompression.Uncompressed), so the safe variant is the default.
        private const string SafeBuildMenuHint = "No override";

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

        // Names of the assemblies whose sources live under the plugin folder.
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
            string safe = null;

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

                    // The safe build without texture/sound overrides: leaves
                    // the assets' import settings as they are.
                    if (attribute.menuItem.IndexOf(SafeBuildMenuHint, StringComparison.OrdinalIgnoreCase) >= 0)
                        safe = attribute.menuItem;

                    // The shortest path is almost certainly the main build
                    // item rather than a variation of it.
                    if (best == null || attribute.menuItem.Length < best.Length)
                        best = attribute.menuItem;
                }
            }

            return safe ?? best;
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
