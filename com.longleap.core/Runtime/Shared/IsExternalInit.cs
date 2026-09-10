namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Compiler polyfill enabling C# 9 record types and init-only properties on Unity's runtime.
    /// The C# compiler emits init accessors that require IsExternalInit from this namespace.
    /// Unity's Mono/.NET Standard 2.1 runtime does not ship this type, so we provide it here.
    /// Without this, all record types (MatchResult, ObjectiveChange, etc.) would fail to compile.
    /// </summary>
    public static class IsExternalInit { }
}
