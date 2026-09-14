using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SimpleEndpoints.Generator.Utils;
using static SimpleEndpoints.Generator.Diagnostics;
using static SimpleEndpoints.Generator.EndpointContracts;

namespace SimpleEndpoints.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EndpointAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [MissingMapEndpoint, MissingConfigure, GroupNotMarked, UnusableType, DuplicateGroupName];

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(static startContext =>
        {
            INamedTypeSymbol? endpointAttribute = startContext.Compilation.GetTypeByMetadataName(EndpointAttributeName);
            INamedTypeSymbol? groupAttribute = startContext.Compilation.GetTypeByMetadataName(EndpointGroupAttributeName);

            if (endpointAttribute is null || groupAttribute is null)
            {
                return;
            }

            var groups = new ConcurrentBag<INamedTypeSymbol>();

            startContext.RegisterSymbolAction(ctx => AnalyzeType(ctx, endpointAttribute, groupAttribute, groups),
                SymbolKind.NamedType);

            startContext.RegisterCompilationEndAction(ctx => ReportDuplicateGroupNames(ctx, groups));
        });
    }

    private static bool IsEndpoint(INamedTypeSymbol type, INamedTypeSymbol endpointAttribute,
        [NotNullWhen(true)] out AttributeData? endpoint)
    {
        endpoint = type.GetAttributes()
            .FirstOrDefault(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass,
                endpointAttribute));

        return endpoint is not null;
    }

    private static bool IsGroup(INamedTypeSymbol type, INamedTypeSymbol groupAttribute) => type.GetAttributes()
        .Any(attributeData => SymbolEqualityComparer.Default.Equals(attributeData.AttributeClass, groupAttribute));

    private static void AnalyzeEndpoint(SymbolAnalysisContext context, INamedTypeSymbol type, AttributeData endpoint)
    {
        if (!IsValidType(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(UnusableType, type.Location, type.Name));
            return;
        }

        if (!HasMapEndpoint(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(MissingMapEndpoint, type.Location, type.Name));
        }

        INamedTypeSymbol? group = GetGroupType(endpoint);

        if (group is not null && !HasAttribute(group, EndpointGroupAttributeName))
        {
            Location attributeLocation = endpoint.ApplicationSyntaxReference
                ?.GetSyntax(context.CancellationToken).GetLocation() ?? type.Location;
            context.ReportDiagnostic(Diagnostic.Create(GroupNotMarked, attributeLocation, group.Name));
        }
    }

    private static void AnalyzeGroup(SymbolAnalysisContext context, INamedTypeSymbol type)
    {
        if (!IsValidType(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(UnusableType, type.Location, type.Name));
        }

        if (!HasConfigure(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(MissingConfigure, type.Location, type.Name));
        }
    }

    private static void AnalyzeType(
        SymbolAnalysisContext context,
        INamedTypeSymbol endpointAttribute,
        INamedTypeSymbol groupAttribute,
        ConcurrentBag<INamedTypeSymbol> groups)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (IsEndpoint(type, endpointAttribute, out AttributeData? endpoint))
        {
            AnalyzeEndpoint(context, type, endpoint);
        }

        if (IsGroup(type, groupAttribute))
        {
            groups.Add(type);
            AnalyzeGroup(context, type);
        }
    }

    private static void ReportDuplicateGroupNames(CompilationAnalysisContext context, ConcurrentBag<INamedTypeSymbol> groups)
    {
        IEnumerable<INamedTypeSymbol> colliding = groups
            .GroupBy(static group => group.Name, StringComparer.Ordinal)
            .Where(static byName => byName.Count() > 1)
            .SelectMany(static byName => byName);

        foreach (INamedTypeSymbol group in colliding)
        {
            context.ReportDiagnostic(Diagnostic.Create(DuplicateGroupName, group.Location, group.FullyQualifiedName, group.Name));
        }
    }
}
