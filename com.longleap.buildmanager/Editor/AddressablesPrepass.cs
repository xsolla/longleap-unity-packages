using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LongLeap.BuildManager.Editor
{
    // Builds Addressables content before the player build.
    //
    // Why at all: when Addressables is set not to build content with the
    // player (Build Addressables on Player Build = Do not Build) and nothing
    // calls BuildPlayerContent, NOBODY builds the bundles, and the player
    // build ships whatever was left from last time.
    //
    // Why through reflection: the window package must compile in a project
    // without Addressables. A hard dependency would make Addressables mandatory
    // for everyone who takes Build Manager.
    //
    // Edge-case decisions:
    //   package missing      -> skip silently, that is a normal project;
    //   no settings          -> skip with a message;
    //   API not found        -> CANCEL the build. Silently shipped stale
    //                           bundles are worse than a failed build, and only
    //                           a loud failure forces someone to fix the
    //                           reflection after a package update;
    //   Unity builds it      -> skip, so the work is not done twice.
    internal static class AddressablesPrepass
    {
        private const string EditorAssembly = "Unity.Addressables.Editor";

        private const string SettingsType =
            "UnityEditor.AddressableAssets.Settings.AddressableAssetSettings";

        // Careful: this type lives in UnityEditor.AddressableAssets, not in
        // ...AddressableAssets.Settings, where its closeness to
        // AddressableAssetSettings tempts you to put it. An earlier version of
        // this class failed on exactly that.
        private const string DefaultObjectType =
            "UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject";

        public enum Outcome
        {
            NotInstalled,
            NoSettings,
            AlreadyBuiltWithPlayer,

            // Check only: Run would build the content.
            WillBuild,

            // Run only: the content was built.
            Built,

            // Run: the build failed or the API is missing. Check: the API is
            // missing, so Run would cancel the player build.
            Failed,
        }

        // What Run would do, without building anything. For the window's
        // pre-build warnings, so they match what the build will really do.
        public static Outcome Check(out string message) => Prepare(out _, out message);

        public static Outcome Run(out string message)
        {
            Outcome outcome = Prepare(out MethodInfo build, out message);

            if (outcome != Outcome.WillBuild)
                return outcome;

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

        // Everything Run needs before it builds: resolves the API and reads the
        // settings. Returns WillBuild with the build method when there is
        // content to build.
        private static Outcome Prepare(out MethodInfo build, out string message)
        {
            build = null;
            message = null;

            Assembly assembly = FindEditorAssembly();

            if (assembly == null)
                return Outcome.NotInstalled;

            Type defaultObject = Resolve(assembly, DefaultObjectType);
            Type settingsType = Resolve(assembly, SettingsType);

            if (defaultObject == null || settingsType == null)
            {
                message =
                    $"Addressables is installed but {DefaultObjectType} or {SettingsType} was not " +
                    $"found in {EditorAssembly}, not even by its short name. The package API " +
                    "moved; fix AddressablesPrepass.";
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

            // The overload with the out parameter is taken precisely to learn
            // about errors: the one without it returns its result nowhere.
            build = settingsType
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

            return Outcome.WillBuild;
        }

        private static Assembly FindEditorAssembly() =>
            AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == EditorAssembly);

        // Unity can build the content together with the player by itself. If it
        // is set up that way, do not duplicate it: a full bundle rebuild is not
        // free.
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

                // The enum member name, not its number: PlayerBuildOption lives
                // in someone else's package and its order is not ours.
                return value != null && value.ToString() == "BuildWithPlayer";
            }
            catch (Exception)
            {
                // Could not read it, so build ourselves. An extra rebuild costs
                // time; a missed one ships stale bundles.
                return false;
            }
        }

        // Full name first, then the short name.
        //
        // The namespace of these two types has already once turned out not to
        // be what was expected (DefaultObject does NOT live next to Settings),
        // and the cost of that mistake is a cancelled build. A lookup by short
        // name inside the same assembly survives a move between namespaces; the
        // type name itself changes far less often.
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
    }
}
