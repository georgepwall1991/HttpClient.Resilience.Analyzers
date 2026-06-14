using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HttpClient.Resilience.Analyzers.Models;

internal static class TypeNameUtilities
{
    public static IEnumerable<string> GetComparableNames(string typeName)
    {
        yield return typeName;

        var simpleName = ToSimpleName(typeName);
        if (simpleName != typeName)
        {
            yield return simpleName;
        }
    }

    public static string ToSimpleName(string typeName)
    {
        typeName = typeName.Trim();
        if (typeName.StartsWith("global::", System.StringComparison.Ordinal))
        {
            typeName = typeName.Substring("global::".Length);
        }

        var genericStart = typeName.IndexOf('<');
        if (genericStart < 0)
        {
            return LastNameSegment(typeName);
        }

        var genericTypeName = LastNameSegment(typeName.Substring(0, genericStart));
        var genericEnd = typeName.LastIndexOf('>');
        if (genericEnd < genericStart)
        {
            return genericTypeName;
        }

        var genericArguments = typeName.Substring(genericStart + 1, genericEnd - genericStart - 1);
        var simpleArguments = SplitGenericArguments(genericArguments)
            .Select(ToSimpleName);

        return genericTypeName + "<" + string.Join(", ", simpleArguments) + ">";
    }

    private static string LastNameSegment(string typeName)
    {
        var lastDot = typeName.LastIndexOf('.');
        return lastDot < 0 ? typeName : typeName.Substring(lastDot + 1);
    }

    private static IEnumerable<string> SplitGenericArguments(string genericArguments)
    {
        var depth = 0;
        var current = new StringBuilder();

        foreach (var character in genericArguments)
        {
            switch (character)
            {
                case '<':
                    depth++;
                    current.Append(character);
                    break;
                case '>':
                    depth--;
                    current.Append(character);
                    break;
                case ',' when depth == 0:
                    yield return current.ToString().Trim();
                    current.Clear();
                    break;
                default:
                    current.Append(character);
                    break;
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString().Trim();
        }
    }
}
