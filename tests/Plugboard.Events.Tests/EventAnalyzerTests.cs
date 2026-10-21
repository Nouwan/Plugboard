using JetBrains.Annotations;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using Plugboard.Events.Generator;

namespace Plugboard.Events.Tests;

[TestFixture]
internal sealed class EventAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Plugboard.Events;

        """;

    private const string OrderPlacedEvent = """

        [Event("order.placed")]
        public sealed partial record OrderPlaced(int OrderId);

        """;

    [Test]
    public Task Analyze_WithValidEventsAndHandlers_ShouldReportNothing() => Analyze(Usings + OrderPlacedEvent + """
        public interface IMailer;

        public static class OrderHandlers
        {
            [Subscribe]
            public static void Subscriptions(ISubscriptionBuilder events)
            {
                events.Subscribe("send-confirmation", (OrderPlaced e, IMailer mailer, CancellationToken ct) => Task.CompletedTask);
                events.Subscribe("update-stock", TimeSpan.FromSeconds(90), (OrderPlaced e, HandlerContext ctx) => Task.CompletedTask);
                events.SubscribeAll("audit", Audit);
            }

            private static Task Audit(EventEnvelope envelope, CancellationToken ct) => Task.CompletedTask;
        }

        public static class Other
        {
            [Subscribe]
            internal static void MapHandlers(ISubscriptionBuilder events) { }
        }
        """);

    [Test]
    public Task Analyze_WithNonVoidMapMethod_ShouldReportPB201() => Analyze(Usings + """
        public static class OrderHandlers
        {
            [Subscribe]
            public static int {|PB201:Subscriptions|}(ISubscriptionBuilder events) => 0;
        }
        """);

    [Test]
    public Task Analyze_WithInstanceMapMethod_ShouldReportPB201() => Analyze(Usings + """
        public sealed class OrderHandlers
        {
            [Subscribe]
            public void {|PB201:Subscriptions|}(ISubscriptionBuilder events) { }
        }
        """);

    [Test]
    public Task Analyze_WithWrongMapParameter_ShouldReportPB201() => Analyze(Usings + """
        public static class OrderHandlers
        {
            [Subscribe]
            public static void {|PB201:Map|}(IServiceProvider services) { }
        }
        """);

    [Test]
    public Task Analyze_WithNonPartialEvent_ShouldReportPB202() => Analyze(Usings + """
        [Event("order.placed")]
        public sealed record {|PB202:OrderPlaced|}(int OrderId);
        """);

    [Test]
    public Task Analyze_WithNestedEvent_ShouldReportPB202() => Analyze(Usings + """
        public static class Orders
        {
            [Event("order.placed")]
            public sealed partial record {|PB202:OrderPlaced|}(int OrderId);
        }
        """);

    [Test]
    public Task Analyze_WithDuplicateEventNames_ShouldReportPB203() => Analyze(Usings + """
        namespace Orders
        {
            [Event("order.placed")]
            public sealed partial record {|PB203:OrderPlaced|}(int OrderId);
        }

        namespace Legacy
        {
            [Event("order.placed")]
            public sealed partial record {|PB203:OrderPlaced|}(int OrderId);
        }
        """);

    [Test]
    public Task Analyze_WithGenericEvent_ShouldReportPB204() => Analyze(Usings + """
        [Event("thing.changed")]
        public sealed partial record {|PB204:ThingChanged|}<T>(T Value);
        """);

    [Test]
    public Task Analyze_WithPrimitiveHandlerParameter_ShouldReportPB205() => Analyze(Usings + OrderPlacedEvent + """
        public static class OrderHandlers
        {
            [Subscribe]
            public static void Subscriptions(ISubscriptionBuilder events) =>
                events.Subscribe("broken", {|PB205:(OrderPlaced e, string reason) => Task.CompletedTask|});
        }
        """);

    [Test]
    public Task Analyze_WithEnvelopeInSubscribe_ShouldReportPB205() => Analyze(Usings + """
        public static class OrderHandlers
        {
            [Subscribe]
            public static void Subscriptions(ISubscriptionBuilder events) =>
                events.Subscribe("broken", {|PB205:(EventEnvelope e) => Task.CompletedTask|});
        }
        """);

    [Test]
    public Task Analyze_WithEventInSubscribeAll_ShouldReportPB205() => Analyze(Usings + OrderPlacedEvent + """
        public static class OrderHandlers
        {
            [Subscribe]
            public static void Subscriptions(ISubscriptionBuilder events) =>
                events.SubscribeAll("broken", {|PB205:(OrderPlaced e) => Task.CompletedTask|});
        }
        """);

    [Test]
    public Task Analyze_WithUnmarkedEventParameter_ShouldReportPB205() => Analyze(Usings + """
        public sealed record NotAnEvent(int Id);

        public static class OrderHandlers
        {
            [Subscribe]
            public static void Subscriptions(ISubscriptionBuilder events) =>
                events.Subscribe("broken", {|PB205:(NotAnEvent e) => Task.CompletedTask|});
        }
        """);

    [Test]
    public Task Analyze_WithVoidHandler_ShouldReportPB205() => Analyze(Usings + OrderPlacedEvent + """
        public static class OrderHandlers
        {
            [Subscribe]
            public static void Subscriptions(ISubscriptionBuilder events) =>
                events.Subscribe("broken", {|PB205:(OrderPlaced e) => { }|});
        }
        """);

    [Test]
    public Task Analyze_WithTwoContexts_ShouldReportPB205() => Analyze(Usings + OrderPlacedEvent + """
        public static class OrderHandlers
        {
            [Subscribe]
            public static void Subscriptions(ISubscriptionBuilder events) =>
                events.Subscribe("broken", {|PB205:(OrderPlaced e, HandlerContext a, HandlerContext b) => Task.CompletedTask|});
        }
        """);

    [Test]
    public Task Analyze_WithHandWrittenIEvent_ShouldReportPB206() => Analyze(Usings + """
        public sealed record {|PB206:OrderPlaced|}(int OrderId) : IEvent
        {
            public static string EventName => "order.placed";
        }
        """);

    [AssertionMethod]
    private static Task Analyze(string source)
    {
        var test = new CSharpAnalyzerTest<EventAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = TestReferences.Net100
        };
        test.TestState.AddPlugboardEvents();
        return test.RunAsync();
    }
}
