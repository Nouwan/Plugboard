using JetBrains.Annotations;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using Plugboard.Endpoints.Generator;

namespace Plugboard.Endpoints.Tests;

[TestFixture]
internal sealed class EndpointAnalyzerTests
{
    [Test]
    public Task Analyze_WithValidEndpointAndGroup_ShouldReportNothing() => Analyze("""
        using Plugboard;
        using Microsoft.AspNetCore.Routing;

        [EndpointGroup]
        public static class TodoGroup
        {
            public static RouteGroupBuilder MapEndpointGroup(IEndpointRouteBuilder app) => null!;
        }

        [Endpoint(typeof(TodoGroup))]
        public sealed class GetTodoEndpoint
        {
            public static void MapEndpoint(IEndpointRouteBuilder app) { }
        }

        [Endpoint]
        public static class HealthEndpoint
        {
            internal static void MapEndpoint(IEndpointRouteBuilder app) { }
        }
        """);

    [Test]
    public Task Analyze_WithEndpointMissingMapEndpoint_ShouldReportPB101() => Analyze("""
        using Plugboard;

        [Endpoint]
        public static class {|PB101:BrokenEndpoint|} { }
        """);

    [Test]
    public Task Analyze_WithInstanceMapEndpoint_ShouldReportPB101() => Analyze("""
        using Plugboard;
        using Microsoft.AspNetCore.Routing;

        [Endpoint]
        public sealed class {|PB101:BrokenEndpoint|}
        {
            public void MapEndpoint(IEndpointRouteBuilder app) { }
        }
        """);

    [Test]
    public Task Analyze_WithWrongParameterType_ShouldReportPB101() => Analyze("""
        using Plugboard;
        using Microsoft.AspNetCore.Routing;

        [Endpoint]
        public static class {|PB101:BrokenEndpoint|}
        {
            public static void MapEndpoint(RouteGroupBuilder app) { }
        }
        """);

    [Test]
    public Task Analyze_WithGroupMissingMapEndpointGroup_ShouldReportPB102() => Analyze("""
        using Plugboard;

        [EndpointGroup]
        public static class {|PB102:TodoGroup|} { }
        """);

    [Test]
    public Task Analyze_WithVoidMapEndpointGroup_ShouldReportPB102() => Analyze("""
        using Plugboard;
        using Microsoft.AspNetCore.Routing;

        [EndpointGroup]
        public static class {|PB102:TodoGroup|}
        {
            public static void MapEndpointGroup(IEndpointRouteBuilder app) { }
        }
        """);

    [Test]
    public Task Analyze_WithLegacyConfigure_ShouldReportPB102() => Analyze("""
        using Plugboard;
        using Microsoft.AspNetCore.Routing;

        [EndpointGroup]
        public static class {|PB102:TodoGroup|}
        {
            public static void Configure(RouteGroupBuilder group) { }
        }
        """);

    [Test]
    public Task Analyze_WithUnmarkedGroupType_ShouldReportPB103() => Analyze("""
        using Plugboard;
        using Microsoft.AspNetCore.Routing;

        public static class NotAGroup
        {
            public static RouteGroupBuilder MapEndpointGroup(IEndpointRouteBuilder app) => null!;
        }

        [{|PB103:Endpoint(typeof(NotAGroup))|}]
        public static class OrphanEndpoint
        {
            public static void MapEndpoint(IEndpointRouteBuilder app) { }
        }
        """);

    [Test]
    public Task Analyze_WithGenericEndpoint_ShouldReportPB104() => Analyze("""
        using Plugboard;
        using Microsoft.AspNetCore.Routing;

        [Endpoint]
        public static class {|PB104:GenericEndpoint|}<T>
        {
            public static void MapEndpoint(IEndpointRouteBuilder app) { }
        }
        """);

    [Test]
    public Task Analyze_WithAbstractGroup_ShouldReportPB104() => Analyze("""
        using Plugboard;
        using Microsoft.AspNetCore.Routing;

        [EndpointGroup]
        public abstract class {|PB104:AbstractGroup|}
        {
            public static RouteGroupBuilder MapEndpointGroup(IEndpointRouteBuilder app) => null!;
        }
        """);

    [Test]
    public Task Analyze_WithDuplicateGroupNames_ShouldReportPB105() => Analyze("""
        using Plugboard;
        using Microsoft.AspNetCore.Routing;

        namespace Features.Todos
        {
            [EndpointGroup]
            public static class {|PB105:TodoGroup|}
            {
                public static RouteGroupBuilder MapEndpointGroup(IEndpointRouteBuilder app) => null!;
            }
        }

        namespace Features.Other
        {
            [EndpointGroup]
            public static class {|PB105:TodoGroup|}
            {
                public static RouteGroupBuilder MapEndpointGroup(IEndpointRouteBuilder app) => null!;
            }
        }
        """);

    [AssertionMethod]
    private static Task Analyze(string source)
    {
        var test = new CSharpAnalyzerTest<EndpointAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = TestReferences.Net80AspNetCore
        };
        test.TestState.AdditionalReferences.Add(typeof(EndpointAttribute).Assembly);
        return test.RunAsync();
    }
}
