using System.Collections.Generic;
using UnityEditor;

namespace LongLeap.BuildManager.Options
{
    // A snapshot of the project for reading options. The define set is read
    // ONCE and SerializedObjects are cached one per asset, so querying every
    // option costs the same as querying one. Without it, options that read
    // files from disk would do so on every window repaint.
    public interface IProjectReadContext
    {
        ISet<string> Defines { get; }

        // null if the asset does not exist. Callers must check: the asset may
        // be missing, renamed or deleted.
        SerializedObject GetSerializedObject(string assetPath);
    }

    // The view of the state that availability is evaluated against.
    //
    // There are two implementations, and that is the point of the design: the
    // menu passes the saved state, the window passes the staged one (saved
    // plus unapplied edits), so a row is disabled as soon as the option above
    // it is unticked, before Apply. One interface, two views: the menu and the
    // window cannot disagree on what "available" means.
    public interface IProjectOptionState
    {
        bool IsDefineOn(string symbol);

        // null if the option is not registered.
        string GetValue(string optionId);

        bool TryGetOption(string optionId, out ProjectOption option);
    }

    // Staged state that can be written without touching the project. Used by
    // presets: they spread values over options, and Apply writes them later.
    public interface IStagedOptionState : IProjectOptionState
    {
        void Set(string optionId, string value);

        // Reset one option to its saved value. This, not "turn off", is what
        // preset reconciliation does: an unavailable option must stay as it
        // was, otherwise the row would look modified and Apply would write it.
        void Clear(string optionId);
    }

    // Change sink. Writes nothing itself: it ACCUMULATES changes and applies
    // them all in Flush. Five toggles in one Apply must cost one
    // SetScriptingDefineSymbols call and one domain reload, not five.
    public interface IProjectWriteContext
    {
        void SetDefine(string symbol, bool on);

        // null if the asset does not exist. The returned object is applied and
        // saved in Flush; do not call ApplyModifiedProperties yourself.
        SerializedObject GetSerializedObject(string assetPath);

        // Request AssetDatabase.Refresh at the end. For writers that change
        // files bypassing AssetDatabase (e.g. an asmdef repair).
        void RequestAssetRefresh();

        void Log(string message);

        void LogError(string message);
    }
}
