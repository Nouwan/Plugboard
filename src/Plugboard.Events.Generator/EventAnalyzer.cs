using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Plugboard.Events.Generator.Utils;
using static Plugboard.Events.Generator.Diagnostics;
using static Plugboard.Events.Generator.EventContracts;

namespace Plugboard.Events.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EventAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [InvalidSubscribeMethod, EventMustBePartial, DuplicateEventName, UnusableType, InvalidHandler, HandWrittenEvent];

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(static startContext =>
        {
            INamedTypeSymbol? eventAttribute = startContext.Compilation.GetTypeByMetadataName(EventAttributeName);
            INamedTypeSymbol? mapAttribute = startContext.Compilation.GetTypeByMetadataName(SubscribeAttributeName);

            if (eventAttribute is null || mapAttribute is null)
            {
                return;
            }

            var events = new ConcurrentBag<INamedTypeSymbol>();

            startContext.RegisterSymbolAction(ctx => AnalyzeMethod(ctx, mapAttribute), SymbolKind.Method);
            startContext.RegisterSymbolAction(ctx => AnalyzeType(ctx, eventAttribute, events), SymbolKind.NamedType);
            startContext.RegisterSyntaxNodeAction(AnalyzeSubscribeCall, SyntaxKind.InvocationExpression);
            startContext.RegisterCompilationEndAction(ctx => ReportDuplicateEventNames(ctx, events));
        });
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attribute) => symbol.GetAttributes()
        .Any(data => SymbolEqualityComparer.Default.Equals(data.AttributeClass, attribute));

    private static void AnalyzeMethod(SymbolAnalysisContext context, INamedTypeSymbol mapAttribute)
    {
        var method = (IMethodSymbol)context.Symbol;

        if (HasAttribute(method, mapAttribute) && !IsValidSubscribeMethod(method))
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidSubscribeMethod, method.Locations[0], method.Name));
        }
    }

    private static void AnalyzeType(SymbolAnalysisContext context, INamedTypeSymbol eventAttribute, ConcurrentBag<INamedTypeSymbol> events)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (HasAttribute(type, eventAttribute))
        {
            events.Add(type);
            AnalyzeEvent(context, type);
        }
        else if (ImplementsIEvent(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(HandWrittenEvent, type.Location, type.Name));
        }
    }

    private static void AnalyzeEvent(SymbolAnalysisContext context, INamedTypeSymbol type)
    {
        if (!IsValidType(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(UnusableType, type.Location, type.Name));
        }

        if (!CanGenerateEventPart(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(EventMustBePartial, type.Location, type.Name));
        }
    }

    private static void AnalyzeSubscribeCall(SyntaxNodeAnalysisContext context)
    {
        if (GetSubscribeCall((InvocationExpressionSyntax)context.Node, context.SemanticModel, context.CancellationToken) is not { } call)
        {
            return;
        }

        if (call.Handler is null || DescribeHandler(call.Handler, call.All) is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidHandler, call.HandlerArgument.GetLocation()));
        }
    }

    private static void ReportDuplicateEventNames(CompilationAnalysisContext context, ConcurrentBag<INamedTypeSymbol> events)
    {
        IEnumerable<(INamedTypeSymbol Type, string Name)> colliding = events
            .Select(static type => (Type: type, Name: GetEventName(type)))
            .Where(static e => e.Name is not null)
            .Select(static e => (e.Type, Name: e.Name!))
            .GroupBy(static e => e.Name, StringComparer.Ordinal)
            .Where(static byName => byName.Count() > 1)
            .SelectMany(static byName => byName);

        foreach ((INamedTypeSymbol type, string name) in colliding)
        {
            context.ReportDiagnostic(Diagnostic.Create(DuplicateEventName, type.Location, type.FullyQualifiedName, name));
        }
    }
}
