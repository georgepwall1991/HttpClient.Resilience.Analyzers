using System;
using System.Linq;

namespace HttpClient.Resilience.Analyzers.Models;

internal static class HttpMethodSafety
{
    public static readonly string[] UnsafeHttpMethodPrefixes =
    {
        "Connect",
        "Delete",
        "Patch",
        "Post",
        "Put"
    };

    public static readonly string[] UnsafeHttpMethodNames =
    {
        "Connect",
        "Delete",
        "Patch",
        "Post",
        "Put"
    };

    public static readonly string[] SafeHttpMethodNames =
    {
        "Get",
        "Head",
        "Options",
        "Trace"
    };

    public static bool MethodNameStartsWithUnsafePrefix(string methodName)
    {
        return UnsafeHttpMethodPrefixes.Any(prefix => methodName.StartsWith(prefix, StringComparison.Ordinal));
    }

    public static bool IsUnsafeHttpMethodName(string methodName, bool ignoreCase)
    {
        return UnsafeHttpMethodNames.Contains(
            methodName,
            ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    public static bool IsSafeHttpMethodName(string methodName)
    {
        return SafeHttpMethodNames.Contains(methodName, StringComparer.Ordinal);
    }
}
