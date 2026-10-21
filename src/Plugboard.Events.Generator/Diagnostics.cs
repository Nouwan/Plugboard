using Microsoft.CodeAnalysis;

namespace Plugboard.Events.Generator;

internal static class Diagnostics
{
    private const string Category = "Plugboard";

    public static readonly DiagnosticDescriptor InvalidSubscribeMethod = new(
        id: "PB201",
        title: "Subscribe method must have a valid signature",
        messageFormat: "[Subscribe] method '{0}' must be 'static void {0}(ISubscriptionBuilder events)', public or internal, non-generic, in a non-generic type",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EventMustBePartial = new(
        id: "PB202",
        title: "Event must be partial",
        messageFormat: "Event '{0}' must be a partial, non-nested type so its IEvent implementation can be generated",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateEventName = new(
        id: "PB203",
        title: "Event names must be unique",
        messageFormat: "Event '{0}' shares its name '{1}' with another event in this assembly; handlers and stored events are matched on the name",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor UnusableType = new(
        id: "PB204",
        title: "Event types must be non-generic and non-abstract",
        messageFormat: "Type '{0}' cannot be an event because it is generic or abstract; generated code must be able to name and construct it",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidHandler = new(
        id: "PB205",
        title: "Event handler must have a valid signature",
        messageFormat: "Handler must be a lambda or method returning Task whose first parameter is an [Event] type for Subscribe or EventEnvelope for SubscribeAll, followed by at most one HandlerContext, at most one CancellationToken and services to resolve; primitives and value types cannot be resolved",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HandWrittenEvent = new(
        id: "PB206",
        title: "IEvent is implemented by the generator",
        messageFormat: "Type '{0}' implements IEvent by hand; mark a partial type with [Event(\"name\")] instead and the implementation is generated",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
