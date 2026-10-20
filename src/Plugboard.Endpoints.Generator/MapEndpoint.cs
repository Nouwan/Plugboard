using Microsoft.CodeAnalysis;

namespace Plugboard.Endpoints.Generator;

internal static class MapEndpoint
{
    public static INamedTypeSymbol? GroupOf(AttributeData mapEndpointAttribute) =>
        mapEndpointAttribute.ConstructorArguments is [{ Kind: TypedConstantKind.Type, Value: INamedTypeSymbol group }]
            ? group
            : null;

    public static bool IsDeclaredInGenericType(IMethodSymbol method) => method.ContainingType.IsGenericType;

    public static bool HasExpectedSignature(IMethodSymbol method, WellKnownTypes types) =>
        method.IsStatic &&
        method is { IsGenericMethod: false, DeclaredAccessibility: Accessibility.Public or Accessibility.Internal, ReturnsVoid: true, Parameters: [{ Type: var parameterType }] } &&
        SymbolEqualityComparer.Default.Equals(parameterType, types.EndpointRouteBuilder);

    public static bool IsValid(IMethodSymbol method, WellKnownTypes types) =>
        !IsDeclaredInGenericType(method) && HasExpectedSignature(method, types);
}