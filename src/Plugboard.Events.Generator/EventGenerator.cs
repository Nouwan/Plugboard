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
        IncrementalValueProvider<EquatableArray<string>> subscribeMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(SubscribeAttributeName, IsMethodDeclaration, ToSubscribeMethod)
            .WhereNotNull()
            .Collect()
            .Select(static (items, _) => items.AsEquatableArray());

        IncrementalValueProvider<EquatableArray<string>> eventContexts = context.SyntaxProvider
            .CreateSyntaxProvider(IsClassWithBaseList, ToEventContext)
            .WhereNotNull()
            .Collect()
            .Select(static (items, _) => items.AsEquatableArray());

        IncrementalValueProvider<RegistrationModel> registration = subscribeMethods
            .Combine(eventContexts)
            .Combine(context.GetRootNamespace())
            .Select(static (input, _) => new RegistrationModel(input.Left.Left, input.Left.Right, input.Right));

        IncrementalValueProvider<EquatableArray<InterceptorInfo>> interceptors = context.SyntaxProvider
            .CreateSyntaxProvider(IsSubscribeInvocation, ToInterceptor)
            .WhereNotNull()
            .Collect()
            .Select(static (items, _) => items.AsEquatableArray());

        context.RegisterSourceOutput(registration, EmitRegistration);
        context.RegisterSourceOutput(interceptors, EmitInterceptors);
    }

    private static bool IsMethodDeclaration(SyntaxNode node, CancellationToken _) => node is MethodDeclarationSyntax;

    private static bool IsClassWithBaseList(SyntaxNode node, CancellationToken _) => node is ClassDeclarationSyntax { BaseList: not null };

    private static string? ToEventContext(GeneratorSyntaxContext context, CancellationToken cancellationToken) =>
        context.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)context.Node, cancellationToken) is { } type && IsEventContext(type)
            ? type.FullyQualifiedName
            : null;

    private static bool IsSubscribeInvocation(SyntaxNode node, CancellationToken _) =>
        node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: SubscribeMethodName or SubscribeAllMethodName } };

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

        IEnumerable<string> arguments = signature.Extras.Select(static extra => extra.Kind switch
        {
            ExtraKind.Context => "ctx",
            ExtraKind.CancellationToken => "ct",
            ExtraKind.Service => $"sp.GetRequiredService<{extra.ServiceType}>()",
            _ => throw new InvalidOperationException($"Unknown handler parameter '{extra.Kind}'"),
        });
        string delegateType = "global::System.Func<"
            + string.Join(", ", handler.Parameters.Select(static p => p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
            + ", " + handler.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ">";
        string typedCall = $"typed({string.Join(", ", ["e", .. arguments])})";

        return new InterceptorInfo(
            Version: location.Version,
            Data: location.Data,
            Method: call.All ? SubscribeAllMethodName : SubscribeMethodName,
            DelegateType: delegateType,
            Register: signature.EventType is null ? "RegisterAll" : $"Register<{signature.EventType.FullyQualifiedName}>",
            Call: typedCall);
    }

    private static void EmitRegistration(SourceProductionContext context, RegistrationModel model)
    {
        var methods = model.SubscribeMethods.Distinct().OrderBy(static m => m, StringComparer.Ordinal).ToList();
        var contexts = model.EventContexts.Distinct().OrderBy(static c => c, StringComparer.Ordinal).ToList();

        if (methods.Count == 0 && contexts.Count == 0)
        {
            return;
        }

        // One public method per module, named after it, so a host can call every module's without ambiguity.
        string module = model.RootNamespace.Replace(".", "");

        context.AddSource("Subscriptions.g.cs", TemplateTools.RenderTemplate("Subscriptions",
            new { model.RootNamespace, Module = module, SubscribeMethods = methods, EventContexts = contexts }));
    }

    private static void EmitInterceptors(SourceProductionContext context, EquatableArray<InterceptorInfo> interceptors)
    {
        var ordered = interceptors
            .Distinct()
            .OrderBy(static i => i.Data, StringComparer.Ordinal)
            .Select((interceptor, index) => new { Index = index, interceptor.Version, interceptor.Data, interceptor.Method, interceptor.DelegateType, interceptor.Register, interceptor.Call })
            .ToList();

        if (ordered.Count == 0)
        {
            return;
        }

        context.AddSource("SubscriptionInterceptors.g.cs", TemplateTools.RenderTemplate("SubscriptionInterceptors",
            new { Interceptors = ordered }));
    }

    /// <param name="SubscribeMethods">Fully qualified [Subscribe] methods.</param>
    /// <param name="EventContexts">Fully qualified types implementing IEventContext.</param>
    private sealed record RegistrationModel(EquatableArray<string> SubscribeMethods, EquatableArray<string> EventContexts, string RootNamespace);

    /// <param name="Version">Interceptable location version, as the compiler wants it in [InterceptsLocation].</param>
    /// <param name="Data">Opaque interceptable location data.</param>
    /// <param name="DelegateType">The handler's natural delegate type, used to cast the Delegate argument.</param>
    /// <param name="Method">The intercepted method, Subscribe or SubscribeAll; the interceptor must carry the same name.</param>
    /// <param name="Register">SubscriptionServices method to call: generic Register for the event, or RegisterAll.</param>
    /// <param name="Call">C# expression invoking the typed handler with <c>e</c>, <c>envelope</c>, <c>ctx</c>, <c>sp</c> and <c>ct</c>.</param>
    private sealed record InterceptorInfo(int Version, string Data, string Method, string DelegateType, string Register, string Call);
}
