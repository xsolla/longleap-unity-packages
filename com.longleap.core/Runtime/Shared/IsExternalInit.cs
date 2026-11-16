namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Compiler polyfill enabling C# 9 record types and init-only properties on Unity's runtime.
    /// The C# compiler emits init accessors that require IsExternalInit from this namespace.
    /// Unity's Mono/.NET Standard 2.1 runtime does not ship this type, so we provide it here.
    /// It is public on purpose: assemblies that reference LongLeap.Core (achievements, daily quests)
    /// use it for their own records. Do not declare a second copy in those assemblies.
    /// </summary>
    public static class IsExternalInit { }
}
