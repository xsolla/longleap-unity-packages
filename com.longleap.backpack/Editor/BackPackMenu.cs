using System;
using System.Collections.Generic;
using System.Linq;
using LongLeap.Backpack;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// Переключатель BackPack-сервера и быстрый доступ к его конфигу.
//
// Меню разбито на подменю по тому же правилу, что XsollaIdMenu и
// GamePixPluginMenu:
//
//   Required/Send Events to Server (BACKPACK) — дефайн. Без него ачивки
//     сохраняются локально, как до появления сервера, и ни один запрос из
//     сборки не уходит. Это ответ на вопрос «локальные данные или серверные»,
//     и он дефайн, а не поле ассета, потому что касается КОДА: без сервера
//     ветка отправки не должна попадать в сборку вовсе;
//   Required/Environment/{Local,Development,Production} — КУДА ходить, если
//     ходить. Это уже данные, поэтому не дефайн, а поле в ассете: правим его
//     через SerializedObject, чтобы правка попала в .asset и в git, а не
//     осталась в памяти редактора.
//
// Select Config Asset остаётся в корне: это действие (выделить ассет), а не
// переключатель состояния проекта.
//
// Подсказок к пунктам нет и быть не может: у атрибута MenuItem только
// menuItem/priority/secondaryPriority/validate/editorModes. Подробности — в
// Debug.Log при переключении.
//
// На типы BloodRedSnow ссылаемся напрямую (в отличие от XsollaIdMenu, который
// лезет к полям по строкам): этот файл лежит в Assets/Editor, то есть в
// Assembly-CSharp-Editor, а та ссылается на BloodRedSnow сама. Ограничений по
// дефайнам у сборки BloodRedSnow нет, так что круга «включатель требует того,
// что включает» здесь не возникает.
public static class BackPackMenu
{
    private const string Define = "BACKPACK";

    private const string EnabledMenuPath = "BackPack/Required/Send Events to Server (BACKPACK)";
    private const string ConfigMenuPath = "BackPack/Select Config Asset";
    private const string EnvLocalMenuPath = "BackPack/Required/Environment/Local";
    private const string EnvDevelopmentMenuPath = "BackPack/Required/Environment/Development";
    private const string EnvProductionMenuPath = "BackPack/Required/Environment/Production";

    private const string ConfigAssetPath = "Assets/Resources/Backend/BackPackConfig.asset";

    // Имя поля ассета. Совпадает с полем BackPackConfigSO — через
    // SerializedObject иначе никак, а именно он ставит ассету dirty и пишет
    // правку на диск.
    private const string EnvironmentProperty = "_environment";

    [MenuItem(EnabledMenuPath, false, 100)]
    private static void ToggleEnabled()
    {
        bool enabled = IsEnabled();

        SetDefine(!enabled);

        if (enabled)
        {
            Debug.Log("BackPack: отправка выключена (BACKPACK снят) — ачивки сохраняются " +
                "только локально, запросов из сборки нет. Награды при этом не выдаются: " +
                "сервер о событиях не узнает.");
            return;
        }

        var config = AssetDatabase.LoadAssetAtPath<BackPackConfigSO>(ConfigAssetPath);

        Debug.Log("BackPack: отправка включена (BACKPACK добавлен) — ачивка уезжает в сейв " +
            $"только после удачного ответа сервера. Активная среда: {Describe(config)}.");

        WarnIfIncomplete(config);
        WarnIfNotWebGL();
    }

    [MenuItem(EnabledMenuPath, true)]
    private static bool ToggleEnabledValidate()
    {
        Menu.SetChecked(EnabledMenuPath, IsEnabled());
        return true;
    }

    // Среда — не переключатель, а выбор из трёх, поэтому три пункта с
    // галочкой, а не чекбокс: так видно, куда именно уходят события, и не
    // приходится догадываться, что значит снятая галочка.
    //
    // Порядок пунктов — от «своей машины» к «боевому», как и в перечислении:
    // соседние приоритеты держат их одной группой, без разделителей.

    [MenuItem(EnvLocalMenuPath, false, 103)]
    private static void SelectLocal() => SetEnvironment(BackPackEnvironment.Local);

    [MenuItem(EnvLocalMenuPath, true)]
    private static bool SelectLocalValidate() =>
        ValidateEnvironment(EnvLocalMenuPath, BackPackEnvironment.Local);

    [MenuItem(EnvDevelopmentMenuPath, false, 104)]
    private static void SelectDevelopment() => SetEnvironment(BackPackEnvironment.Development);

    [MenuItem(EnvDevelopmentMenuPath, true)]
    private static bool SelectDevelopmentValidate() =>
        ValidateEnvironment(EnvDevelopmentMenuPath, BackPackEnvironment.Development);

    [MenuItem(EnvProductionMenuPath, false, 105)]
    private static void SelectProduction() => SetEnvironment(BackPackEnvironment.Production);

    [MenuItem(EnvProductionMenuPath, true)]
    private static bool SelectProductionValidate() =>
        ValidateEnvironment(EnvProductionMenuPath, BackPackEnvironment.Production);

    [MenuItem(ConfigMenuPath, false, 102)]
    private static void SelectConfig()
    {
        var config = AssetDatabase.LoadAssetAtPath<BackPackConfigSO>(ConfigAssetPath);

        if (config == null)
        {
            Debug.LogError(
                $"BackPack: конфиг не найден по пути {ConfigAssetPath}. " +
                "Создайте его через Assets / Create / Blood Red Snow / BackPack Config.");
            return;
        }

        Selection.activeObject = config;
        EditorGUIUtility.PingObject(config);
    }

    // ---- среда ----

    private static SerializedProperty LoadEnvironmentProperty(out SerializedObject serialized)
    {
        var asset = AssetDatabase.LoadAssetAtPath<BackPackConfigSO>(ConfigAssetPath);

        serialized = asset == null ? null : new SerializedObject(asset);

        return serialized?.FindProperty(EnvironmentProperty);
    }

    // Индекс ищем по имени, а не пишем 0/1 числом: перестановка членов
    // BackPackEnvironment иначе тихо переключила бы среду — и в проде
    // оказался бы localhost.
    private static int IndexOf(SerializedProperty environment, BackPackEnvironment value) =>
        Array.IndexOf(environment.enumNames, value.ToString());

    private static bool ValidateEnvironment(string menuPath, BackPackEnvironment value)
    {
        SerializedProperty environment = LoadEnvironmentProperty(out _);

        Menu.SetChecked(menuPath,
            environment != null && environment.enumValueIndex == IndexOf(environment, value));

        // Ассета нет — переключать нечего.
        return environment != null;
    }

    private static void SetEnvironment(BackPackEnvironment value)
    {
        SerializedProperty environment = LoadEnvironmentProperty(out SerializedObject serialized);

        if (environment == null)
        {
            Debug.LogError(
                $"BackPack: конфиг не найден по пути {ConfigAssetPath} " +
                $"(либо в нём нет поля {EnvironmentProperty}).");
            return;
        }

        int index = IndexOf(environment, value);

        if (index < 0)
        {
            Debug.LogError($"BackPack: в BackPackEnvironment нет члена '{value}' — перечисление переименовали.");
            return;
        }

        if (environment.enumValueIndex != index)
        {
            environment.enumValueIndex = index;
            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        var config = serialized.targetObject as BackPackConfigSO;

        Debug.Log($"BackPack: среда — {value} ({Describe(config)}).");

        WarnIfIncomplete(config);
        WarnIfLocal(value);

        // Среду переключили, а отправка выключена дефайном: события всё равно
        // никуда не уйдут. Молчать нельзя — со стороны это выглядит как
        // сделанная настройка.
        if (!IsEnabled())
        {
            Debug.LogWarning("BackPack: дефайна BACKPACK нет — события не отправляются независимо от среды. " +
                $"Включается пунктом «{EnabledMenuPath}».");
        }
    }

    // Пустой адрес или gameId делают IsConfigured, а за ним и
    // BackPackService.IsAvailable, ложными: отправка молча выключается, и
    // «награды не приходят» приходится искать вслепую. Лучше сказать об этом
    // в момент переключения.
    private static void WarnIfIncomplete(BackPackConfigSO config)
    {
        if (config == null || config.Active.IsConfigured)
            return;

        Debug.LogWarning(
            $"BackPack: у среды {config.Environment} заполнено не всё ({config.Active}) — " +
            $"события не отправляются, ачивки сохраняются только локально. Заполните {ConfigAssetPath}.");
    }

    // Local — адрес на машине разработчика: из сборки, которую откроют где-то
    // ещё (площадка, чужой браузер), он недоступен в принципе. Уехавшая с ним
    // сборка выглядит рабочей, а награды не приходят ни у кого, кроме автора.
    private static void WarnIfLocal(BackPackEnvironment value)
    {
        if (value != BackPackEnvironment.Local)
            return;

        Debug.LogWarning("BackPack: выбран локальный сервер — события дойдут только с этой машины. " +
            "Перед сборкой на площадку переключите среду на Development или Production.");
    }

    private static string Describe(BackPackConfigSO config) =>
        config == null ? $"конфиг не найден ({ConfigAssetPath})" : config.ToString();

    // ---- дефайны ----

    private static bool IsEnabled() => CurrentDefines().Contains(Define);

    private static List<string> CurrentDefines()
    {
        string raw = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL) ?? string.Empty;

        return raw.Split(';')
            .Select(d => d.Trim())
            .Where(d => d.Length > 0)
            .ToList();
    }

    private static void SetDefine(bool enabled)
    {
        List<string> defines = CurrentDefines();

        if (enabled)
        {
            if (!defines.Contains(Define))
                defines.Add(Define);
        }
        else
        {
            defines.Remove(Define);
        }

        PlayerSettings.SetScriptingDefineSymbols(
            NamedBuildTarget.WebGL, string.Join(";", defines));
    }

    // Дефайн ставится ТОЛЬКО платформе WebGL — как и у Xsolla ID, потому что
    // игра уезжает именно туда. Но в редакторе действуют дефайны активной
    // цели сборки, и на другой платформе включённый пункт меню не даст ничего
    // в Play Mode — это ровно тот случай, когда человек решает, что «сервер не
    // работает».
    private static void WarnIfNotWebGL()
    {
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
            return;

        Debug.LogWarning(
            $"BackPack: дефайн задан платформе WebGL, а активная цель сборки — " +
            $"{EditorUserBuildSettings.activeBuildTarget}. В Play Mode отправки не будет, " +
            "пока цель не переключена на WebGL (File / Build Profiles).");
    }
}
