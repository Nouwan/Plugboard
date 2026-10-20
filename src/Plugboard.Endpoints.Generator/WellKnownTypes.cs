using Microsoft.CodeAnalysis;

namespace Plugboard.Endpoints.Generator;

internal sealed class WellKnownTypes
{
    public const string MapEndpointAttributeName = "Plugboard.Endpoints.MapEndpointAttribute";
    public const string EndpointGroupAttributeName = "Plugboard.Endpoints.EndpointGroupAttribute";

    public required INamedTypeSymbol MapEndpointAttribute { get; init; }
    public required INamedTypeSymbol EndpointGroupAttribute { get; init; }
    public required INamedTypeSymbol EndpointRouteBuilder { get; init; }
    public required INamedTypeSymbol RouteGroupBuilder { get; init; }

    public static WellKnownTypes? FindWellKnownTypes(Compilation compilation)
    {
        INamedTypeSymbol? mapEndpointAttribute = compilation.GetTypeByMetadataName(MapEndpointAttributeName);
        INamedTypeSymbol? endpointGroupAttribute = compilation.GetTypeByMetadataName(EndpointGroupAttributeName);
        INamedTypeSymbol? endpointRouteBuilder = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Routing.IEndpointRouteBuilder");
        INamedTypeSymbol? routeGroupBuilder = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Routing.RouteGroupBuilder");

        if (mapEndpointAttribute is null || endpointGroupAttribute is null || endpointRouteBuilder is null ||
            routeGroupBuilder is null)
        {
            return null;
        }

        return new WellKnownTypes
        {
            MapEndpointAttribute = mapEndpointAttribute,
            EndpointGroupAttribute = endpointGroupAttribute,
            EndpointRouteBuilder = endpointRouteBuilder,
            RouteGroupBuilder = routeGroupBuilder
        };
    }
}