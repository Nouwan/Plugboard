namespace Plugboard;

/// <summary>
/// Marks a class as a route group. The class must declare
/// <c>public static void Configure(RouteGroupBuilder group)</c>. The generator emits
/// <c>Register{Name}()</c>, which creates the group, calls <c>Configure</c>, and maps every
/// endpoint declared with <c>[Endpoint(typeof(Name))]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class EndpointGroupAttribute : Attribute;
