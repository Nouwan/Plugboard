using Microsoft.CodeAnalysis;

namespace Plugboard.Endpoints.Generator;

internal static class EndpointContracts
{
    public const string EndpointAttributeName = "Plugboard.EndpointAttribute";
    public const string EndpointGroupAttributeName = "Plugboard.EndpointGroupAttribute";
    private const string RouteBuilderTypeName = "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder";
    private const string RouteGroupBuilderTypeName = "Microsoft.AspNetCore.Routing.RouteGroupBuilder";
    private const string MapEndpointMethodName = "MapEndpoint";
    private const string MapEndpointGroupMethodName = "MapEndpointGroup";

    public static bool IsValidType(INamedTypeSymbol type) =>
        !type.IsGenericType && (type.IsStatic || !type.IsAbstract);

    public static bool HasMapEndpoint(INamedTypeSymbol type) =>
        HasStaticMethod(type, MapEndpointMethodName, returnTypeName: null, RouteBuilderTypeName);

    public static bool HasMapEndpointGroup(INamedTypeSymbol type) =>
        HasStaticMethod(type, MapEndpointGroupMethodName, RouteGroupBuilderTypeName, RouteBuilderTypeName);

    public static bool HasAttribute(INamedTypeSymbol type, string attributeMetadataName) =>
        type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == attributeMetadataName);

    public static INamedTypeSymbol? GetGroupType(AttributeData endpointAttribute) =>
        endpointAttribute.ConstructorArguments is [{ Kind: TypedConstantKind.Type, Value: INamedTypeSymbol group }]
            ? group
            : null;

    private static bool HasStaticMethod(INamedTypeSymbol type, string name, string? returnTypeName,
        string parameterTypeName) =>
        type.GetMembers(name).OfType<IMethodSymbol>().Any(method =>
            method.IsStatic &&
            (returnTypeName is null ? method.ReturnsVoid : method.ReturnType.ToDisplayString() == returnTypeName) &&
            method is
            {
                IsGenericMethod: false,
                DeclaredAccessibility: Accessibility.Public or Accessibility.Internal,
                Parameters: [{ Type: var parameterType }]
            } &&
            (parameterType.ToDisplayString() == parameterTypeName));
}
