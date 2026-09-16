using Microsoft.CodeAnalysis;

namespace Plugboard.Endpoints.Generator;

/// <summary>
/// Contract checks shared by the generator and the analyzer. The analyzer reports violations;
/// the generator silently skips the same types so generated code never carries follow-up errors.
/// </summary>
internal static class EndpointContracts
{
    public const string EndpointAttributeName = "Plugboard.EndpointAttribute";
    public const string EndpointGroupAttributeName = "Plugboard.EndpointGroupAttribute";
    private const string RouteBuilderTypeName = "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder";
    private const string RouteGroupBuilderTypeName = "Microsoft.AspNetCore.Routing.RouteGroupBuilder";
    private const string MapEndpointMethodName = "MapEndpoint";
    private const string ConfigureMethodName = "Configure";

    public static bool IsValidType(INamedTypeSymbol type) =>
        !type.IsGenericType && (type.IsStatic || !type.IsAbstract);

    public static bool HasMapEndpoint(INamedTypeSymbol type) =>
        HasStaticVoidMethod(type, MapEndpointMethodName, RouteBuilderTypeName);

    public static bool HasConfigure(INamedTypeSymbol type) =>
        HasStaticVoidMethod(type, ConfigureMethodName, RouteGroupBuilderTypeName);

    public static bool HasAttribute(INamedTypeSymbol type, string attributeMetadataName) =>
        type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == attributeMetadataName);

    public static INamedTypeSymbol? GetGroupType(AttributeData endpointAttribute) =>
        endpointAttribute.ConstructorArguments is [{ Kind: TypedConstantKind.Type, Value: INamedTypeSymbol group }]
            ? group
            : null;

    private static bool HasStaticVoidMethod(INamedTypeSymbol type, string name, string parameterTypeName) =>
        type.GetMembers(name).OfType<IMethodSymbol>().Any(method =>
            method.IsStatic &&
            method is
            {
                ReturnsVoid: true,
                IsGenericMethod: false,
                DeclaredAccessibility: Accessibility.Public or Accessibility.Internal,
                Parameters: [{ Type: var parameterType }]
            } &&
            (parameterType.ToDisplayString() == parameterTypeName));
}
