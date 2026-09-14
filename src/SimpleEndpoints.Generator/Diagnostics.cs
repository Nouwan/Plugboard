using Microsoft.CodeAnalysis;

namespace SimpleEndpoints.Generator;

internal static class Diagnostics
{
    private const string Category = "SimpleEndpoints";

    public static readonly DiagnosticDescriptor MissingMapEndpoint = new(
        id: "SE001",
        title: "Endpoint must declare MapEndpoint",
        messageFormat: "Endpoint '{0}' must declare 'public static void MapEndpoint(IEndpointRouteBuilder app)'",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingConfigure = new(
        id: "SE002",
        title: "Endpoint group must declare Configure",
        messageFormat: "Endpoint group '{0}' must declare 'public static void Configure(RouteGroupBuilder group)'",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GroupNotMarked = new(
        id: "SE003",
        title: "Endpoint group type must be marked with [EndpointGroup]",
        messageFormat: "Type '{0}' is used as an endpoint group but is not marked with [EndpointGroup]",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnusableType = new(
        id: "SE004",
        title: "Endpoint types must be non-generic and non-abstract",
        messageFormat: "Type '{0}' cannot be an endpoint or endpoint group because it is generic or abstract; generated code must be able to call its static members",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateGroupName = new(
        id: "SE005",
        title: "Endpoint group names must be unique",
        messageFormat: "Endpoint group '{0}' shares its name with another group; the generated Register{1}() method would collide. Rename one of the groups.",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);
}
