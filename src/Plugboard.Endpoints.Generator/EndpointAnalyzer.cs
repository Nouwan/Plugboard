using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Plugboard.Endpoints.Generator.Utils;
using static Plugboard.Endpoints.Generator.Diagnostics;

namespace Plugboard.Endpoints.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EndpointAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [InvalidMapEndpoint, MissingCreateGroup, GroupNotMarked, DeclaredInGenericType, DuplicateGroupName];

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(static startContext =>
        {
            var types = WellKnownTypes.FindWellKnownTypes(startContext.Compilation);

            //Stop analysis if not all required types are in the compilation
            if (types is null)
            {
                return;
            }

            var groups = new ConcurrentBag<INamedTypeSymbol>();

            startContext.RegisterSymbolAction(ctx => AnalyzeMethod(ctx, types), SymbolKind.Method);
            startContext.RegisterSymbolAction(ctx => AnalyzeType(ctx, types, groups), SymbolKind.NamedType);
            startContext.RegisterCompilationEndAction(ctx => ReportDuplicateGroupNames(ctx, groups));
        });
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context, WellKnownTypes types)
    {
        var method = (IMethodSymbol)context.Symbol;
        AttributeData? mapEndpointAttribute = method.FindAttribute(types.MapEndpointAttribute);

        if (mapEndpointAttribute is null)
        {
            return;
        }

        if (MapEndpoint.IsDeclaredInGenericType(method))
        {
            context.ReportDiagnostic(Diagnostic.Create(DeclaredInGenericType, method.Location, method.Name));
        }

        if (!MapEndpoint.HasExpectedSignature(method, types))
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidMapEndpoint, method.Location, method.Name));
        }

        INamedTypeSymbol? group = MapEndpoint.GroupOf(mapEndpointAttribute);

        if (group is not null && !EndpointGroup.IsMarked(group, types))
        {
            Location attributeLocation = mapEndpointAttribute.ApplicationSyntaxReference
                ?.GetSyntax(context.CancellationToken).GetLocation() ?? method.Location;
            context.ReportDiagnostic(Diagnostic.Create(GroupNotMarked, attributeLocation, group.Name));
        }
    }

    private static void AnalyzeType(
        SymbolAnalysisContext context,
        WellKnownTypes types,
        ConcurrentBag<INamedTypeSymbol> groups)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (!EndpointGroup.IsMarked(type, types))
        {
            return;
        }

        groups.Add(type);

        if (EndpointGroup.IsGeneric(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(DeclaredInGenericType, type.Location, type.Name));
        }

        if (!EndpointGroup.HasCreateGroup(type, types))
        {
            context.ReportDiagnostic(Diagnostic.Create(MissingCreateGroup, type.Location, type.Name));
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