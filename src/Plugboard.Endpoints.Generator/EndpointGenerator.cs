using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Plugboard.Endpoints.Generator.Utils;

namespace Plugboard.Endpoints.Generator;

[Generator]
public sealed class EndpointGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<EndpointInfo> endpoints = context.SyntaxProvider
            .ForAttributeWithMetadataName(WellKnownTypes.MapEndpointAttributeName,
                static (node, _) => node is MethodDeclarationSyntax,
                ToEndpoint)
            .WhereNotNull();

        IncrementalValuesProvider<GroupInfo> groupDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(WellKnownTypes.EndpointGroupAttributeName,
                static (node, _) => node is TypeDeclarationSyntax,
                ToGroup)
            .WhereNotNull();

        IncrementalValueProvider<Model> model = endpoints.Collect()
            .Combine(groupDeclarations.Collect())
            .Combine(context.GetRootNamespace())
            .Select(ToModel);

        context.RegisterSourceOutput(model, Emit);
    }

    private static EndpointInfo? ToEndpoint(GeneratorAttributeSyntaxContext context, CancellationToken _)
    {
        var method = (IMethodSymbol)context.TargetSymbol;
        AttributeData mapEndpointAttribute = context.Attributes[0];
        var types = WellKnownTypes.FindWellKnownTypes(context.SemanticModel.Compilation);

        if (types is null || !MapEndpoint.IsValid(method, types))
        {
            return null;
        }

        INamedTypeSymbol? group = MapEndpoint.GroupOf(mapEndpointAttribute);

        if (group is not null && !EndpointGroup.IsValid(group, types))
        {
            return null;
        }

        return new EndpointInfo(method.FullyQualifiedName, group?.FullyQualifiedName);
    }

    private static GroupInfo? ToGroup(GeneratorAttributeSyntaxContext context, CancellationToken _)
    {
        var type = (INamedTypeSymbol)context.TargetSymbol;
        var types = WellKnownTypes.FindWellKnownTypes(context.SemanticModel.Compilation);

        if (types is null || !EndpointGroup.IsValid(type, types))
        {
            return null;
        }

        return new GroupInfo(type.FullyQualifiedName, type.Name);
    }

    private static Model ToModel(
        ((ImmutableArray<EndpointInfo> endpoints, ImmutableArray<GroupInfo> groupDeclarations) found, string rootNamespace) source,
        CancellationToken _)
    {
        return new Model(
            source.found.endpoints.AsEquatableArray(),
            source.found.groupDeclarations.AsEquatableArray(),
            source.rootNamespace);
    }

    private static void Emit(SourceProductionContext context, Model model)
    {
        List<string> standaloneEndpoints = StandaloneEndpoints(model.Endpoints);

        var groups = GroupsWithUniqueNames(model.GroupDeclarations)
            .Select(group => new TemplateGroup(group.Type, group.Name, EndpointsInGroup(group.Type, model.Endpoints)))
            .ToList();

        if ((standaloneEndpoints.Count == 0) && (groups.Count == 0))
        {
            return;
        }

        var template = new TemplateModel(model.RootNamespace, standaloneEndpoints, groups);
        context.AddSource("EndpointRegistration.g.cs", TemplateTools.RenderTemplate("EndpointRegistration", template));
    }

    private static List<string> StandaloneEndpoints(IEnumerable<EndpointInfo> endpoints) =>
        endpoints
            .Where(static endpoint => endpoint.Group is null)
            .Select(static endpoint => endpoint.Method)
            .OrderBy(static method => method, StringComparer.Ordinal)
            .ToList();

    private static List<string> EndpointsInGroup(string group, IEnumerable<EndpointInfo> endpoints) =>
        endpoints
            .Where(endpoint => endpoint.Group == group)
            .Select(static endpoint => endpoint.Method)
            .OrderBy(static method => method, StringComparer.Ordinal)
            .ToList();

    private static List<GroupInfo> GroupsWithUniqueNames(IEnumerable<GroupInfo> groups) =>
        groups
            .GroupBy(static grp => grp.Name, StringComparer.Ordinal)
            .Where(static grouping => grouping.Count() == 1)
            .Select(static grouping => grouping.First())
            .OrderBy(static group => group.Type, StringComparer.Ordinal)
            .ToList();

    private sealed record EndpointInfo(string Method, string? Group);

    private sealed record GroupInfo(string Type, string Name);

    private sealed record Model(EquatableArray<EndpointInfo> Endpoints, EquatableArray<GroupInfo> GroupDeclarations, string RootNamespace);

    private sealed record TemplateModel(string RootNamespace, List<string> Endpoints, List<TemplateGroup> Groups);
    private sealed record TemplateGroup(string Type, string Name, List<string> Endpoints);
}