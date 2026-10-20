namespace Plugboard.Endpoints;

/// <summary>
/// Marks a method that maps an endpoint. The method must be
/// <c>static void</c>, <c>public</c> or <c>internal</c>, and take a single <c>IEndpointRouteBuilder</c> parameter.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class MapEndpointAttribute : Attribute
{
    /// <summary>Maps the endpoint on the normal ASP.NET route builder.</summary>
    public MapEndpointAttribute()
    {
    }

    /// <summary>Maps the endpoint on the route group configured by <paramref name="group"/>.</summary>
    /// <param name="group">A type marked with <see cref="EndpointGroupAttribute"/>.</param>
    public MapEndpointAttribute(Type group) => Group = group;

    /// <summary>The group the endpoint maps onto.</summary>
    public Type? Group { get; }
}