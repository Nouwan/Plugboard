namespace Plugboard;

/// <summary>
/// Marks a type as a endpoint group. The type must declare
/// <c>public static RouteGroupBuilder MapEndpointGroup(IEndpointRouteBuilder app)</c> function, which creates and configures the minimal api route group
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class EndpointGroupAttribute : Attribute;
