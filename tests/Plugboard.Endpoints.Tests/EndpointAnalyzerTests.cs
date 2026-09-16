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
            public static void Configure(RouteGroupBuilder group) { }
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
    public Task Analyze_WithEndpointMissingMapEndpoint_ShouldReportPB001() => Analyze("""
        using Plugboard;

        [Endpoint]
        public static class {|PB001:BrokenEndpoint|} { }
        """);

    [Test]
    public Task Analyze_WithInstanceMapEndpoint_ShouldReportPB001() => Analyze("""
        using Plugboard;
        using Microsoft.AspNetCore.Routing;

        [Endpoint]
        public sealed class {|PB001:BrokenEndpoint|}
        {
            public void MapEndpoint(IEndpointRouteBuilder app) { }
        }
        """);

    [Test]
    public Task Analyze_WithWrongParameterType_ShouldReportPB001() => Analyze("""
        using Plugboard;
        using Microsoft.AspNetCore.Routing;

        [Endpoint]
        public static class {|PB001:BrokenEndpoint|}
        {
            public static void MapEndpoint(RouteGroupBuilder app) { }
        }
        """);

    [Test]
    public Task Analyze_WithGroupMissingConfigure_ShouldReportPB002() => Analyze("""
        using Plugboard;

        [EndpointGroup]
        public static class {|PB002:TodoGroup|} { }
        """);

    [Test]
    public Task Analyze_WithUnmarkedGroupType_ShouldReportPB003() => Analyze("""
        using Plugboard;
        using Microsoft.AspNetCore.Routing;

        public static class NotAGroup
        {
            public static void Configure(RouteGroupBuilder group) { }
        }

        [{|PB003:Endpoint(typeof(NotAGroup))|}]
        public static class OrphanEndpoint
        {
            public static void MapEndpoint(IEndpointRouteBuilder app) { }
        }
        """);

    [Test]
    public Task Analyze_WithGenericEndpoint_ShouldReportPB004() => Analyze("""
        using Plugboard;
        using Microsoft.AspNetCore.Routing;

        [Endpoint]
        public static class {|PB004:GenericEndpoint|}<T>
        {
            public static void MapEndpoint(IEndpointRouteBuilder app) { }
        }
        """);

    [Test]
    public Task Analyze_WithAbstractGroup_ShouldReportPB004() => Analyze("""
        using Plugboard;
        using Microsoft.AspNetCore.Routing;

        [EndpointGroup]
        public abstract class {|PB004:AbstractGroup|}
        {
            public static void Configure(RouteGroupBuilder group) { }
        }
        """);

    [Test]
    public Task Analyze_WithDuplicateGroupNames_ShouldReportPB005() => Analyze("""
        using Plugboard;
        using Microsoft.AspNetCore.Routing;

        namespace Features.Todos
        {
            [EndpointGroup]
            public static class {|PB005:TodoGroup|}
            {
                public static void Configure(RouteGroupBuilder group) { }
            }
        }

        namespace Features.Other
        {
            [EndpointGroup]
            public static class {|PB005:TodoGroup|}
            {
                public static void Configure(RouteGroupBuilder group) { }
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
