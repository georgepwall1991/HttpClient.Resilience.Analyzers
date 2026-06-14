using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HttpClient.Resilience.Analyzers.KnownSymbols;

internal static class HttpClientSymbols
{
    public const string HttpClientMetadataName = "System.Net.Http.HttpClient";
    public const string SocketsHttpHandlerMetadataName = "System.Net.Http.SocketsHttpHandler";

    public static bool IsHttpClient(ITypeSymbol? type)
    {
        return type is not null &&
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Net.Http.HttpClient";
    }

    public static bool IsSocketsHttpHandler(ITypeSymbol? type)
    {
        return type is not null &&
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Net.Http.SocketsHttpHandler";
    }

    public static bool IsHttpClientFactory(ITypeSymbol? type)
    {
        return type is not null &&
            type.Name == "IHttpClientFactory";
    }

    public static bool IsHttpClientName(TypeSyntax type)
    {
        return IsTypeName(type, "HttpClient");
    }

    public static bool IsHttpClientFactoryName(TypeSyntax type)
    {
        return IsTypeName(type, "IHttpClientFactory");
    }

    public static bool IsSocketsHttpHandlerName(TypeSyntax type)
    {
        return IsTypeName(type, "SocketsHttpHandler");
    }

    private static bool IsTypeName(TypeSyntax type, string name)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == name,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText == name,
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText == name,
            _ => false
        };
    }
}
