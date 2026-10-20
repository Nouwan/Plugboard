# Plugboard
Plugboard is pre-1.0. The public API may change in any minor release until 1.0.0.

Plugboard takes care of the wiring of services, endpoints and much more for you, 
this allows you to focus on implementing code that actually matters.

We expose as much of the original api as feasible while only providing you with a way to connect the bits together. 

Plugboard is made up of the following packages:

| Package               | Description                              |
|-----------------------|------------------------------------------|
| `Plugboard.Endpoints` | Easily map asp.net minimal api endpoints |

# Endpoints
Plugboard.Endpoints allows you to set up a asp.net minimal api endpoint in the location that makes sense to you.
Just add the MapEndpoint attribute and we will do the wiring for you

Just add the `[MapEndpoint]` attribute to a `static void` method that takes an `IEndpointRouteBuilder`, and all
endpoints will be mapped by simply calling `app.MapEndpoints();` in your `program.cs`

## Requirements

- .NET 10 or higher

## Install

```shell
dotnet add package Plugboard.Endpoints
```

## Usage

Standalone endpoint:

```csharp
using Plugboard.Endpoints;

public static class HealthEndpoint
{
    [MapEndpoint]
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/health", () => Results.Ok());
}
```

The method name is free. It must be `static void`, `public` or `internal`, non-generic, and take a single
`IEndpointRouteBuilder` parameter. A class may hold any number of `[MapEndpoint]` methods.

Grouped endpoints share one `RouteGroupBuilder`, created and configured by the group's `CreateGroup` function:

```csharp
using Plugboard.Endpoints;

[EndpointGroup]
public static class TodoGroup
{
    public static RouteGroupBuilder CreateGroup(IEndpointRouteBuilder app) =>
        app.MapGroup("/todos").WithTags("Todos").RequireAuthorization();
}

public static class GetTodoEndpoint
{
    [MapEndpoint(typeof(TodoGroup))]
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/{id}", (int id) => Results.Ok(id));
}
```

A group may map its own endpoints too, so a feature can live in one class:

```csharp
[EndpointGroup]
public static class TodoModule
{
    public static RouteGroupBuilder CreateGroup(IEndpointRouteBuilder app) =>
        app.MapGroup("/todos").WithTags("Todos");

    [MapEndpoint(typeof(TodoModule))]
    public static void MapGet(IEndpointRouteBuilder group) =>
        group.MapGet("/{id}", (int id) => Results.Ok(id));

    [MapEndpoint(typeof(TodoModule))]
    public static void MapList(IEndpointRouteBuilder group) =>
        group.MapGet("/", () => Results.Ok());
}
```

Map in `Program.cs`:

```csharp
var app = builder.Build();
app.MapEndpoints();   // map all endpoints in the assembly
app.MapTodoGroup();   // map a specific group of endpoints in the assembly
app.Run();
```

Generated into your project's `RootNamespace` as `internal static` extensions on `IEndpointRouteBuilder`:

- `MapEndpoints()` calls every `[MapEndpoint]` method directly with the builder and calls every
  `Map{Group}()`.
- `Map{Group}()` per `[EndpointGroup]`: calls `CreateGroup`, then calls every
  `[MapEndpoint(typeof(Group))]` method with the returned group.

`[MapEndpoint]` methods and `[EndpointGroup]` types may live in any class, record or struct, as long as neither it
nor a type it is nested in is generic.

### Contracts

Mark the types your endpoints consume and return with `[Contract]` to show they are part of your contract to the
outside world:

```csharp
[Contract]
public sealed record TodoResponse(int Id, string Title);
```

Members of a contract are only used through serialization, so JetBrains Rider and ReSharper would report them as
unused. `[Contract]` carries `[MeansImplicitUse]` for the type and all its members, so those warnings are not reported.

## Diagnostics

| Id      | Source   | Message                                                                                                       |
|---------|----------|---------------------------------------------------------------------------------------------------------------|
| `PB101` | analyzer | `[MapEndpoint]` method is not a `public`/`internal`, non-generic `static void` taking `IEndpointRouteBuilder` |
| `PB102` | analyzer | `[EndpointGroup]` class must declare `public static RouteGroupBuilder CreateGroup(IEndpointRouteBuilder app)` |
| `PB103` | analyzer | Type passed to `[MapEndpoint(typeof(...))]` is not marked `[EndpointGroup]`                                   |
| `PB104` | analyzer | `[MapEndpoint]` method or `[EndpointGroup]` type is generic or declared in a generic type                     |
| `PB105` | analyzer | Two endpoint groups share the same (simple) name inside the same assembly; `Map{Group}()` would collide       |

## Build

```shell
dotnet build
dotnet test
dotnet pack src/Plugboard.Endpoints -c Release
```

## License

MIT, see [LICENSE](LICENSE).