# Plugboard

Build your ASP.NET minimal API using one class per endpoint and leave the wiring up to us!

Structure your code however you want: standalone endpoints, groups, one project or several. Endpoints use the minimal
API methods on `IEndpointRouteBuilder` giving you the flexibility to use the existing API surface and community
extensions.

Just add the `[Endpoint]` attribute to a class and add a function with the signature:
`public static void MapEndpoint(IEndpointRouteBuilder app)` and all endpoints will be registered by simply calling
`app.RegisterEndpoints(); ` in your `program.cs`

## Requirements

- .net8.0 or higher

## Install

```shell
dotnet add package Plugboard.Endpoints
```

## Usage

Standalone endpoint:

```csharp
using Plugboard;

[Endpoint]
public static class HealthEndpoint
{
    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/health", () => Results.Ok());
}
```

Grouped endpoints share one `RouteGroupBuilder`:

```csharp
using Plugboard;

[EndpointGroup]
public static class TodoGroup
{
    public static void Configure(RouteGroupBuilder group) =>
        group.WithTags("Todos").RequireAuthorization();
}

[Endpoint(typeof(TodoGroup))]
public static class GetTodoEndpoint
{
    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/todos/{id}", (int id) => Results.Ok(id));
}
```

Register in `Program.cs`:

```csharp
var app = builder.Build();
app.RegisterEndpoints();   // everything
app.RegisterTodoGroup();   // or one group at a time
app.Run();
```

Generated into your project's `RootNamespace` as `internal static` extensions on `IEndpointRouteBuilder`:

- `RegisterEndpoints()` maps every `[Endpoint]` directly on the builder and calls every `Register{Group}()`.
- `Register{Group}()` per `[EndpointGroup]`: creates the group, calls `Configure`, maps every
  `[Endpoint(typeof(Group))]` on it.

Classes may be `static`, or any non-abstract, non-generic class.

## Diagnostics

| Id      | Source   | Message                                                                                                      |
|---------|----------|--------------------------------------------------------------------------------------------------------------|
| `PB001` | analyzer | `[Endpoint]` class must declare `public static void MapEndpoint(IEndpointRouteBuilder app)`                  |
| `PB002` | analyzer | `[EndpointGroup]` class must declare `public static void Configure(RouteGroupBuilder group)`                 |
| `PB003` | analyzer | Type passed to `[Endpoint(typeof(...))]` is not marked `[EndpointGroup]`                                     |
| `PB004` | analyzer | Endpoint or group type is generic or abstract                                                                |
| `PB005` | analyzer | Two endpoint groups share the same (simple) name inside the same assembly; `Register{Group}()` would collide |

## Build

```shell
dotnet build
dotnet test
dotnet pack src/Plugboard.Endpoints -c Release
```

## License

MIT, see [LICENSE](LICENSE).
