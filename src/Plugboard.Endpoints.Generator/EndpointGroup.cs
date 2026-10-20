using Microsoft.CodeAnalysis;
using Plugboard.Endpoints.Generator.Utils;

namespace Plugboard.Endpoints.Generator;

internal static class EndpointGroup
{
    public static bool IsMarked(INamedTypeSymbol type, WellKnownTypes types) =>
        type.FindAttribute(types.EndpointGroupAttribute) is not null;

    public static bool IsGeneric(INamedTypeSymbol type) => type.IsGenericType;

    public static bool HasCreateGroup(INamedTypeSymbol type, WellKnownTypes types) =>
        type.GetMembers("CreateGroup").OfType<IMethodSymbol>().Any(method =>
            method.IsStatic &&
            !method.IsGenericMethod &&
            method.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal &&
            SymbolEqualityComparer.Default.Equals(method.ReturnType, types.RouteGroupBuilder) &&
            method.Parameters is [{ Type: var parameterType }] &&
            SymbolEqualityComparer.Default.Equals(parameterType, types.EndpointRouteBuilder));

    public static bool IsValid(INamedTypeSymbol type, WellKnownTypes types) =>
        IsMarked(type, types) && !IsGeneric(type) && HasCreateGroup(type, types);
}