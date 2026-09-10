// Polyfill for C# 9 init-only setters on runtimes that don't define IsExternalInit
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
