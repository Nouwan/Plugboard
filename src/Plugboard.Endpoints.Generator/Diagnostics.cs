using Microsoft.CodeAnalysis;

namespace Plugboard.Endpoints.Generator;

internal static class Diagnostics
{
    private const string Category = "Plugboard";

    public static readonly DiagnosticDescriptor InvalidMapEndpoint = new(
        id: "PB101",
        title: "[MapEndpoint] method must be a static void method taking IEndpointRouteBuilder",
        messageFormat: "Method '{0}' is marked [MapEndpoint] but is not a public or internal, non-generic 'static void {0}(IEndpointRouteBuilder app)'",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingCreateGroup = new(
        id: "PB102",
        title: "Endpoint group must declare CreateGroup",
        messageFormat: "Endpoint group '{0}' must declare 'public static RouteGroupBuilder CreateGroup(IEndpointRouteBuilder app)'",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GroupNotMarked = new(
        id: "PB103",
        title: "Endpoint group type must be marked with [EndpointGroup]",
        messageFormat: "Type '{0}' is used as an endpoint group but is not marked with [EndpointGroup]",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DeclaredInGenericType = new(
        id: "PB104",
        title: "Endpoints and endpoint groups must not be declared in generic types",
        messageFormat: "'{0}' is declared in a generic type; generated code cannot call it without type arguments",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateGroupName = new(
        id: "PB105",
        title: "Endpoint group names must be unique",
        messageFormat: "Endpoint group '{0}' shares its name with another group; the generated Map{1}() method would collide. Rename one of the groups.",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);
}