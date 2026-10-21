using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Plugboard.Events.Generator;

/// <summary>Validation shared by the analyzer (reports) and the generator (skips silently).</summary>
internal static class EventContracts
{
    public const string EventAttributeName = "Plugboard.Events.EventAttribute";
    public const string SubscribeAttributeName = "Plugboard.Events.SubscribeAttribute";
    public const string BuilderTypeName = "Plugboard.Events.ISubscriptionBuilder";
    public const string SubscribeMethodName = "Subscribe";
    public const string SubscribeAllMethodName = "SubscribeAll";
    private const string IEventName = "Plugboard.Events.IEvent";
    private const string EventEnvelopeName = "Plugboard.Events.EventEnvelope";
    private const string HandlerContextName = "Plugboard.Events.HandlerContext";
    private const string CancellationTokenName = "System.Threading.CancellationToken";
    private const string TaskName = "System.Threading.Tasks.Task";

    public static bool IsValidType(INamedTypeSymbol type) =>
        !type.IsGenericType && (type.IsStatic || !type.IsAbstract);

    public static AttributeData? GetAttribute(ISymbol symbol, string attributeMetadataName) =>
        symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == attributeMetadataName);

    /// <summary>The name given by <c>[Event("name")]</c>, or null when the type is not marked.</summary>
    public static string? GetEventName(INamedTypeSymbol type) =>
        GetAttribute(type, EventAttributeName)?.ConstructorArguments is [{ Value: string name }] ? name : null;

    /// <summary>The generator adds the IEvent implementation as another part, so the event must be partial and not nested.</summary>
    public static bool CanGenerateEventPart(INamedTypeSymbol type) =>
        type.ContainingType is null && type.DeclaringSyntaxReferences.Any(static reference =>
            reference.GetSyntax() is TypeDeclarationSyntax declaration
            && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

    public static bool ImplementsIEvent(INamedTypeSymbol type) =>
        type.AllInterfaces.Any(static i => i.ToDisplayString() == IEventName);

    public static bool IsValidEvent(INamedTypeSymbol type) =>
        GetEventName(type) is not null && CanGenerateEventPart(type) && IsValidType(type);

    /// <summary>A <c>[Subscribe]</c> method: <c>static void</c>, public or internal, non-generic, one ISubscriptionBuilder parameter.</summary>
    public static bool IsValidSubscribeMethod(IMethodSymbol method) =>
        method is
        {
            IsStatic: true,
            ReturnsVoid: true,
            IsGenericMethod: false,
            DeclaredAccessibility: Accessibility.Public or Accessibility.Internal,
            Parameters: [{ Type: var parameterType }],
        }
        && parameterType.ToDisplayString() == BuilderTypeName
        && !method.ContainingType.IsGenericType;

    /// <summary>
    /// Describes an <c>events.Subscribe(name, handler)</c> call, or returns null when the invocation is not one. The handler
    /// argument is the lambda or method group the call passes; its signature is validated separately.
    /// </summary>
    public static SubscribeCall? GetSubscribeCall(InvocationExpressionSyntax invocation, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: SubscribeMethodName or SubscribeAllMethodName }
            || semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method
            || method.ContainingType.ToDisplayString() != BuilderTypeName
            || method.Name is not (SubscribeMethodName or SubscribeAllMethodName)
            || invocation.ArgumentList.Arguments.Count != method.Parameters.Length)
        {
            return null;
        }

        ExpressionSyntax handlerArgument = invocation.ArgumentList.Arguments[^1].Expression;
        SymbolInfo handlerInfo = semanticModel.GetSymbolInfo(handlerArgument, cancellationToken);

        // A lambda binds to its anonymous method; a method group converted to Delegate shows up as a single candidate.
        IMethodSymbol? handler = handlerInfo.Symbol as IMethodSymbol
            ?? (handlerInfo.CandidateSymbols is [IMethodSymbol candidate] ? candidate : null);

        return new SubscribeCall(
            All: method.Name == SubscribeAllMethodName,
            HasMaxDuration: method.Parameters.Length == 3,
            HandlerArgument: handlerArgument,
            Handler: handler);
    }

    /// <summary>
    /// Describes a handler delegate, or returns null when it is not <c>Task (TEvent, ...)</c> with TEvent marked
    /// [Event] for Subscribe or EventEnvelope for SubscribeAll, followed by at most one HandlerContext, at most one
    /// CancellationToken and any number of services to resolve from the delivery scope.
    /// </summary>
    public static HandlerSignature? DescribeHandler(IMethodSymbol handler, bool all)
    {
        if (handler.ReturnType.ToDisplayString() != TaskName
            || handler.Parameters is not [{ Type: INamedTypeSymbol eventType }, .. var rest])
        {
            return null;
        }

        bool isEnvelope = eventType.ToDisplayString() == EventEnvelopeName;

        if (all ? !isEnvelope : (isEnvelope || !IsValidEvent(eventType)))
        {
            return null;
        }

        var extras = new List<HandlerParameter>(rest.Length);

        foreach (IParameterSymbol parameter in rest)
        {
            HandlerParameter? extra = DescribeExtra(parameter.Type);

            if (extra is null || (extra.Kind != ExtraKind.Service && extras.Any(e => e.Kind == extra.Kind)))
            {
                return null;
            }

            extras.Add(extra);
        }

        return new HandlerSignature(isEnvelope ? null : eventType, extras);
    }

    /// <summary>Primitives, strings and other value types cannot be services, so they are rejected rather than resolved.</summary>
    private static HandlerParameter? DescribeExtra(ITypeSymbol type) => type.ToDisplayString() switch
    {
        HandlerContextName => new HandlerParameter(ExtraKind.Context, null),
        CancellationTokenName => new HandlerParameter(ExtraKind.CancellationToken, null),
        _ when type.SpecialType == SpecialType.None && type.IsReferenceType && type.TypeKind != TypeKind.Error =>
            new HandlerParameter(ExtraKind.Service, type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
        _ => null,
    };
}

internal enum ExtraKind
{
    Context,
    CancellationToken,
    Service,
}

/// <param name="ServiceType">Fully qualified service type for <see cref="ExtraKind.Service"/>, otherwise null.</param>
internal sealed record HandlerParameter(ExtraKind Kind, string? ServiceType);

/// <param name="EventType">The handled event type, or null for a handler receiving every event.</param>
/// <param name="Extras">Parameters following the event, in declaration order.</param>
internal sealed record HandlerSignature(INamedTypeSymbol? EventType, IReadOnlyList<HandlerParameter> Extras);

/// <param name="All">True for SubscribeAll, whose handler receives every event as an EventEnvelope.</param>
/// <param name="HasMaxDuration">Whether the overload with a TimeSpan was called.</param>
/// <param name="HandlerArgument">The lambda or method group expression passed as handler.</param>
/// <param name="Handler">The bound delegate method, or null when the argument could not be bound.</param>
internal sealed record SubscribeCall(bool All, bool HasMaxDuration, ExpressionSyntax HandlerArgument, IMethodSymbol? Handler);
