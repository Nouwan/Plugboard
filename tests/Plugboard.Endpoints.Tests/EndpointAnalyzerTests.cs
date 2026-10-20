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
    public Task Analyze_WithValidEndpointsAndGroup_ShouldReportNothing() => Analyze("""
        using Plugboard.Endpoints;
        using Microsoft.AspNetCore.Routing;

        [EndpointGroup]
        public static class TodoGroup
        {
            public static RouteGroupBuilder CreateGroup(IEndpointRouteBuilder app) => null!;

            [MapEndpoint(typeof(TodoGroup))]
            public static void MapList(IEndpointRouteBuilder group) { }
        }

        public sealed class GetTodoEndpoint
        {
            [MapEndpoint(typeof(TodoGroup))]
            public static void Map(IEndpointRouteBuilder app) { }
        }

        public abstract class HealthEndpoint
        {
            [MapEndpoint]
            internal static void Map(IEndpointRouteBuilder app) { }
        }
        """);

    [Test]
    public Task Analyze_WithInstanceMapEndpoint_ShouldReportPB101() => Analyze("""
        using Plugboard.Endpoints;
        using Microsoft.AspNetCore.Routing;

        public sealed class BrokenEndpoint
        {
            [MapEndpoint]
            public void {|PB101:Map|}(IEndpointRouteBuilder app) { }
        }
        """);

    [Test]
    public Task Analyze_WithPrivateMapEndpoint_ShouldReportPB101() => Analyze("""
        using Plugboard.Endpoints;
        using Microsoft.AspNetCore.Routing;

        public static class BrokenEndpoint
        {
            [MapEndpoint]
            private static void {|PB101:Map|}(IEndpointRouteBuilder app) { }
        }
        """);

    [Test]
    public Task Analyze_WithWrongParameterType_ShouldReportPB101() => Analyze("""
        using Plugboard.Endpoints;
        using Microsoft.AspNetCore.Routing;

        public static class BrokenEndpoint
        {
            [MapEndpoint]
            public static void {|PB101:Map|}(RouteGroupBuilder app) { }
        }
        """);

    [Test]
    public Task Analyze_WithNonVoidMapEndpoint_ShouldReportPB101() => Analyze("""
        using Plugboard.Endpoints;
        using Microsoft.AspNetCore.Routing;

        public static class BrokenEndpoint
        {
            [MapEndpoint]
            public static IEndpointRouteBuilder {|PB101:Map|}(IEndpointRouteBuilder app) => app;
        }
        """);

    [Test]
    public Task Analyze_WithGenericMapEndpoint_ShouldReportPB101() => Analyze("""
        using Plugboard.Endpoints;
        using Microsoft.AspNetCore.Routing;

        public static class BrokenEndpoint
        {
            [MapEndpoint]
            public static void {|PB101:Map|}<T>(IEndpointRouteBuilder app) { }
        }
        """);

    [Test]
    public Task Analyze_WithGroupMissingCreateGroup_ShouldReportPB102() => Analyze("""
        using Plugboard.Endpoints;

        [EndpointGroup]
        public static class {|PB102:TodoGroup|} { }
        """);

    [Test]
    public Task Analyze_WithVoidCreateGroup_ShouldReportPB102() => Analyze("""
        using Plugboard.Endpoints;
        using Microsoft.AspNetCore.Routing;

        [EndpointGroup]
        public static class {|PB102:TodoGroup|}
        {
            public static void CreateGroup(IEndpointRouteBuilder app) { }
        }
        """);

    [Test]
    public Task Analyze_WithLegacyConfigure_ShouldReportPB102() => Analyze("""
        using Plugboard.Endpoints;
        using Microsoft.AspNetCore.Routing;

        [EndpointGroup]
        public static class {|PB102:TodoGroup|}
        {
            public static void Configure(RouteGroupBuilder group) { }
        }
        """);

    [Test]
    public Task Analyze_WithUnmarkedGroupType_ShouldReportPB103() => Analyze("""
        using Plugboard.Endpoints;
        using Microsoft.AspNetCore.Routing;

        public static class NotAGroup
        {
            public static RouteGroupBuilder CreateGroup(IEndpointRouteBuilder app) => null!;
        }

        public static class OrphanEndpoint
        {
            [{|PB103:MapEndpoint(typeof(NotAGroup))|}]
            public static void Map(IEndpointRouteBuilder app) { }
        }
        """);

    [Test]
    public Task Analyze_WithMapEndpointInGenericType_ShouldReportPB104() => Analyze("""
        using Plugboard.Endpoints;
        using Microsoft.AspNetCore.Routing;

        public static class GenericEndpoint<T>
        {
            [MapEndpoint]
            public static void {|PB104:Map|}(IEndpointRouteBuilder app) { }
        }
        """);

    [Test]
    public Task Analyze_WithMapEndpointNestedInGenericType_ShouldReportPB104() => Analyze("""
        using Plugboard.Endpoints;
        using Microsoft.AspNetCore.Routing;

        public static class Outer<T>
        {
            public static class Inner
            {
                [MapEndpoint]
                public static void {|PB104:Map|}(IEndpointRouteBuilder app) { }
            }
        }
        """);

    [Test]
    public Task Analyze_WithAbstractGroup_ShouldReportNothing() => Analyze("""
        using Plugboard.Endpoints;
        using Microsoft.AspNetCore.Routing;

        [EndpointGroup]
        public abstract class AbstractGroup
        {
            public static RouteGroupBuilder CreateGroup(IEndpointRouteBuilder app) => null!;
        }
        """);

    [Test]
    public Task Analyze_WithGenericGroup_ShouldReportPB104() => Analyze("""
        using Plugboard.Endpoints;
        using Microsoft.AspNetCore.Routing;

        [EndpointGroup]
        public static class {|PB104:GenericGroup|}<T>
        {
            public static RouteGroupBuilder CreateGroup(IEndpointRouteBuilder app) => null!;
        }
        """);

    [Test]
    public Task Analyze_WithGroupNestedInGenericType_ShouldReportPB104() => Analyze("""
        using Plugboard.Endpoints;
        using Microsoft.AspNetCore.Routing;

        public static class Outer<T>
        {
            [EndpointGroup]
            public static class {|PB104:NestedGroup|}
            {
                public static RouteGroupBuilder CreateGroup(IEndpointRouteBuilder app) => null!;
            }
        }
        """);

    [Test]
    public Task Analyze_WithDuplicateGroupNames_ShouldReportPB105() => Analyze("""
        using Plugboard.Endpoints;
        using Microsoft.AspNetCore.Routing;

        namespace Features.Todos
        {
            [EndpointGroup]
            public static class {|PB105:TodoGroup|}
            {
                public static RouteGroupBuilder CreateGroup(IEndpointRouteBuilder app) => null!;
            }
        }

        namespace Features.Other
        {
            [EndpointGroup]
            public static class {|PB105:TodoGroup|}
            {
                public static RouteGroupBuilder CreateGroup(IEndpointRouteBuilder app) => null!;
            }
        }
        """);

    [AssertionMethod]
    private static Task Analyze(string source)
    {
        var test = new CSharpAnalyzerTest<EndpointAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = TestReferences.Net100AspNetCore
        };
        test.TestState.AdditionalReferences.Add(typeof(MapEndpointAttribute).Assembly);
        return test.RunAsync();
    }
}