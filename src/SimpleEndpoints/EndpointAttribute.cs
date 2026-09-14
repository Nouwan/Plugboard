namespace SimpleEndpoints;

/// <summary>
/// Marks a class as an endpoint. The class must declare
/// <c>public static void MapEndpoint(IEndpointRouteBuilder app)</c>;
/// At startup the <c>RegisterEndpoints()</c> extension can be called to register all endpoints
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]

public sealed class EndpointAttribute : Attribute
{
    /// <summary>A standalone endpoint, mapped directly on the application.</summary>
    public EndpointAttribute()
    {
    }

    /// <summary>An endpoint mapped on the route group configured by <paramref name="group"/>.</summary>
    /// <param name="group">A class marked with <see cref="EndpointGroupAttribute"/>.</param>
    public EndpointAttribute(Type group) => Group = group;

    /// <summary>The group this endpoint belongs to, or <see langword="null"/> for a standalone endpoint.</summary>
    public Type? Group { get; }
}
