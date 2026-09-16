using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Plugboard.Endpoints.Generator.Utils;
using static Plugboard.Endpoints.Generator.EndpointContracts;

namespace Plugboard.Endpoints.Generator;

[Generator]
public sealed class EndpointGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<EquatableArray<EndpointInfo>> endpoints = context.SyntaxProvider
            .ForAttributeWithMetadataName(EndpointAttributeName, IsTypeDeclaration, ToEndpoint)
            .WhereNotNull()
            .Collect()
            .Select(static (items, _) => items.AsEquatableArray());

        IncrementalValueProvider<EquatableArray<GroupInfo>> groups = context.SyntaxProvider
            .ForAttributeWithMetadataName(EndpointGroupAttributeName, IsTypeDeclaration, ToGroup)
            .WhereNotNull()
            .Collect()
            .Select(static (items, _) => items.AsEquatableArray());

        IncrementalValueProvider<Model> model = endpoints
            .Combine(groups)
            .Combine(context.GetRootNamespace())
            .Select(BuildModel);

        context.RegisterSourceOutput(model, Emit);
    }

    private static Model BuildModel(
        ((EquatableArray<EndpointInfo> endpoints, EquatableArray<GroupInfo> groups) source, string rootNameSpace) input,
        CancellationToken _)
    {
        return new Model(input.source.endpoints, input.source.groups, input.rootNameSpace);
    }

    private static bool IsTypeDeclaration(SyntaxNode node, CancellationToken _) => node is TypeDeclarationSyntax;

    private static bool IsValidEndpoint(INamedTypeSymbol type) => IsValidType(type) && HasMapEndpoint(type);

    private static bool IsValidGroup(INamedTypeSymbol type) =>
        HasAttribute(type, EndpointGroupAttributeName) && IsValidType(type) && HasConfigure(type);

    private static EndpointInfo? ToEndpoint(GeneratorAttributeSyntaxContext context, CancellationToken _)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type || !IsValidEndpoint(type))
        {
            return null;
        }

        INamedTypeSymbol? group = GetGroupType(context.Attributes[0]);

        if (group is not null && !IsValidGroup(group))
        {
            return null;
        }

        return new EndpointInfo(type.FullyQualifiedName, group?.FullyQualifiedName);
    }

    private static GroupInfo? ToGroup(GeneratorAttributeSyntaxContext context, CancellationToken _) =>
        context.TargetSymbol is INamedTypeSymbol type && IsValidGroup(type)
            ? new GroupInfo(type.FullyQualifiedName, type.Name)
            : null;

    private static void Emit(SourceProductionContext context, Model model)
    {
        (EquatableArray<EndpointInfo> endpoints, EquatableArray<GroupInfo> groups, string rootNamespace) = model;

        var uniqueGroups = groups.Distinct().OrderBy(static g => g.Fqn, StringComparer.Ordinal).ToList();
        HashSet<string> collidingNames = CollidingNames(uniqueGroups);

        var validGroups = uniqueGroups
            .Where(g => !collidingNames.Contains(g.Name))
            .Select(g => new { g.Fqn, g.Name, Endpoints = EndpointsOf(endpoints, g.Fqn) })
            .ToList();

        List<string> standalone = EndpointsOf(endpoints, groupFqn: null);

        if ((standalone.Count == 0) && (validGroups.Count == 0))
        {
            return;
        }

        SourceText rendered = TemplateTools.RenderTemplate("EndpointRegistration",
            new { RootNamespace = rootNamespace, Endpoints = standalone, Groups = validGroups });
        context.AddSource("EndpointRegistration.g.cs", rendered);
    }

    private static HashSet<string> CollidingNames(List<GroupInfo> groups) =>
        new(groups.GroupBy(static g => g.Name, StringComparer.Ordinal)
                .Where(static g => g.Count() > 1)
                .Select(static g => g.Key),
            StringComparer.Ordinal);

    private static List<string> EndpointsOf(EquatableArray<EndpointInfo> endpoints, string? groupFqn) =>
        endpoints
            .Where(e => e.GroupFqn == groupFqn)
            .Select(static e => e.Fqn)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static e => e, StringComparer.Ordinal)
            .ToList();

    private sealed record Model(
        EquatableArray<EndpointInfo> Endpoints,
        EquatableArray<GroupInfo> Groups,
        string RootNamespace);

    private sealed record EndpointInfo(string Fqn, string? GroupFqn);

    private sealed record GroupInfo(string Fqn, string Name);
}
