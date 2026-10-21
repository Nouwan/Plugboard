using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Plugboard.Events.Generator.Utils;
using static Plugboard.Events.Generator.EventContracts;

namespace Plugboard.Events.Generator;

[Generator]
public sealed class EventGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<EquatableArray<EventInfo>> events = context.SyntaxProvider
            .ForAttributeWithMetadataName(EventAttributeName, IsTypeDeclaration, ToEvent)
            .WhereNotNull()
            .Collect()
            .Select(static (items, _) => items.AsEquatableArray());

        IncrementalValueProvider<RegistrationModel> registration = context.SyntaxProvider
            .ForAttributeWithMetadataName(SubscribeAttributeName, IsMethodDeclaration, ToSubscribeMethod)
            .WhereNotNull()
            .Collect()
            .Select(static (items, _) => items.AsEquatableArray())
            .Combine(context.GetRootNamespace())
            .Select(static (input, _) => new RegistrationModel(input.Left, input.Right));

        IncrementalValueProvider<EquatableArray<InterceptorInfo>> interceptors = context.SyntaxProvider
            .CreateSyntaxProvider(IsSubscribeInvocation, ToInterceptor)
            .WhereNotNull()
            .Collect()
            .Select(static (items, _) => items.AsEquatableArray());

        context.RegisterSourceOutput(events, EmitEvents);
        context.RegisterSourceOutput(registration, EmitRegistration);
        context.RegisterSourceOutput(interceptors, EmitInterceptors);
    }

    private static bool IsTypeDeclaration(SyntaxNode node, CancellationToken _) => node is TypeDeclarationSyntax;

    private static bool IsMethodDeclaration(SyntaxNode node, CancellationToken _) => node is MethodDeclarationSyntax;

    private static bool IsSubscribeInvocation(SyntaxNode node, CancellationToken _) =>
        node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: SubscribeMethodName or SubscribeAllMethodName } };

    private static EventInfo? ToEvent(GeneratorAttributeSyntaxContext context, CancellationToken _)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type
            || context.TargetNode is not TypeDeclarationSyntax declaration
            || !IsValidEvent(type))
        {
            return null;
        }

        string keyword = declaration switch
        {
            RecordDeclarationSyntax { ClassOrStructKeyword.ValueText: "struct" } => "record struct",
            RecordDeclarationSyntax => "record",
            StructDeclarationSyntax => "struct",
            _ => "class",
        };

        return new EventInfo(
            Namespace: type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            Name: type.Name,
            Keyword: keyword,
            EventName: SymbolDisplay.FormatLiteral(GetEventName(type)!, quote: true));
    }

    private static string? ToSubscribeMethod(GeneratorAttributeSyntaxContext context, CancellationToken _) =>
        context.TargetSymbol is IMethodSymbol method && IsValidSubscribeMethod(method)
            ? $"{method.ContainingType.FullyQualifiedName}.{method.Name}"
            : null;

    private static InterceptorInfo? ToInterceptor(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (GetSubscribeCall(invocation, context.SemanticModel, cancellationToken) is not { Handler: { } handler } call
            || DescribeHandler(handler, call.All) is not { } signature
            || context.SemanticModel.GetInterceptableLocation(invocation, cancellationToken) is not { } location)
        {
            return null;
        }

        string eventArgument = signature.EventType is null ? "envelope" : "e";
        IEnumerable<string> arguments = signature.Extras.Select(static extra => extra.Kind switch
        {
            ExtraKind.Context => "ctx",
            ExtraKind.CancellationToken => "ct",
            ExtraKind.Service => $"sp.GetRequiredService<{extra.ServiceType}>()",
            _ => throw new InvalidOperationException($"Unknown handler parameter '{extra.Kind}'"),
        });
        string delegateType = "global::System.Func<"
            + string.Join(", ", handler.Parameters.Select(static p => p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
            + ", global::System.Threading.Tasks.Task>";

        return new InterceptorInfo(
            Version: location.Version,
            Data: location.Data,
            Method: call.All ? SubscribeAllMethodName : SubscribeMethodName,
            HasMaxDuration: call.HasMaxDuration,
            DelegateType: delegateType,
            Register: signature.EventType is null ? "RegisterAll" : $"Register<{signature.EventType.FullyQualifiedName}>",
            Call: $"typed({string.Join(", ", [eventArgument, .. arguments])})");
    }

    private static void EmitEvents(SourceProductionContext context, EquatableArray<EventInfo> events)
    {
        var ordered = events
            .Distinct()
            .OrderBy(static e => e.Namespace, StringComparer.Ordinal)
            .ThenBy(static e => e.Name, StringComparer.Ordinal)
            .ToList();

        if (ordered.Count == 0)
        {
            return;
        }

        context.AddSource("Events.g.cs", TemplateTools.RenderTemplate("Events", new { Events = ordered }));
    }

    private static void EmitRegistration(SourceProductionContext context, RegistrationModel model)
    {
        var methods = model.SubscribeMethods.Distinct().OrderBy(static m => m, StringComparer.Ordinal).ToList();

        if (methods.Count == 0)
        {
            return;
        }

        context.AddSource("Subscriptions.g.cs", TemplateTools.RenderTemplate("Subscriptions",
            new { model.RootNamespace, SubscribeMethods = methods }));
    }

    private static void EmitInterceptors(SourceProductionContext context, EquatableArray<InterceptorInfo> interceptors)
    {
        var ordered = interceptors
            .Distinct()
            .OrderBy(static i => i.Data, StringComparer.Ordinal)
            .Select((interceptor, index) => new { Index = index, interceptor.Version, interceptor.Data, interceptor.Method, interceptor.HasMaxDuration, interceptor.DelegateType, interceptor.Register, interceptor.Call })
            .ToList();

        if (ordered.Count == 0)
        {
            return;
        }

        context.AddSource("SubscriptionInterceptors.g.cs", TemplateTools.RenderTemplate("SubscriptionInterceptors",
            new { Interceptors = ordered }));
    }

    /// <param name="Namespace">Containing namespace, or null for the global namespace.</param>
    /// <param name="EventName">C# literal of the event name.</param>
    private sealed record EventInfo(string? Namespace, string Name, string Keyword, string EventName);

    private sealed record RegistrationModel(EquatableArray<string> SubscribeMethods, string RootNamespace);

    /// <param name="Version">Interceptable location version, as the compiler wants it in [InterceptsLocation].</param>
    /// <param name="Data">Opaque interceptable location data.</param>
    /// <param name="DelegateType">The handler's natural delegate type, used to cast the Delegate argument.</param>
    /// <param name="Method">The intercepted method, Subscribe or SubscribeAll; the interceptor must carry the same name.</param>
    /// <param name="Register">Generic Register call for the event, or RegisterAll.</param>
    /// <param name="Call">C# expression invoking the typed handler with <c>e</c>, <c>envelope</c>, <c>ctx</c>, <c>sp</c> and <c>ct</c>.</param>
    private sealed record InterceptorInfo(int Version, string Data, string Method, bool HasMaxDuration, string DelegateType, string Register, string Call);
}
