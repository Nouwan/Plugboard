namespace Plugboard;

/// <summary>
/// Marks a type that maps an endpoint. The class must declare
/// <c>public static void MapEndpoint(IEndpointRouteBuilder app)</c>;
/// At startup the <c>RegisterEndpoints()</c> extension can be called to register all endpoints
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class EndpointAttribute : Attribute
{
    /// <summary>Endpoint that maps on the normal ASP.net route builder</summary>
    public EndpointAttribute()
    {
    }

    /// <summary>Endpoint that maps on the route group configured by <paramref name="group"/>.</summary>
    /// <param name="group">A type registering a <see cref="EndpointGroupAttribute"/>.</param>
    public EndpointAttribute(Type group) => Group = group;

    /// <summary>The group this endpoint belongs to</summary>
    public Type? Group { get; }
}
