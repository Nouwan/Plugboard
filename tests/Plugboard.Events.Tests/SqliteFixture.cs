using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Options;

namespace Plugboard.Events.Tests;

/// <summary>One shared in-memory SQLite database per test, with every context wired the way a host wires it; the context factory the background services get.</summary>
internal sealed class SqliteFixture : IDisposable, IDbContextFactory<ShopContext>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly EventContextInterceptor _interceptor;

    public SqliteFixture(Action<SubscriptionBuilder>? subscribe = null, int channelCapacity = 100)
    {
        Channel = System.Threading.Channels.Channel.CreateBounded<EventDelivery>(channelCapacity);
        _connection.Open();
        var builder = new SubscriptionBuilder();
        subscribe?.Invoke(builder);
        Subscriptions = builder.Build();
        _interceptor = new EventContextInterceptor(Channel, Subscriptions, Options);

        using ShopContext context = CreateDbContext();
        context.Database.EnsureCreated();

        // A second module's context sharing the database: it maps the event tables without migrating them, so
        // creating its tables must only add its own.
        using BillingContext billing = CreateBillingContext();
        billing.GetService<IRelationalDatabaseCreator>().CreateTables();
    }

    public Channel<EventDelivery> Channel { get; }

    public Subscription[] Subscriptions { get; }

    public IOptions<EventProcessorOptions> Options { get; } = Microsoft.Extensions.Options.Options.Create(new EventProcessorOptions { Host = "host-1" });

    /// <summary>A context as the request scope would get it.</summary>
    public ShopContext CreateScopedContext() => new(ContextOptions().Options);

    /// <summary>Runs once, right after the next read of the delivery queue: to change rows under the sweeper's feet.</summary>
    public AfterReadHook AfterRead { get; } = new();

    /// <summary>Makes every command on every context fail while <see cref="Outage.Unavailable"/>, as if the database went away.</summary>
    public Outage Outage { get; } = new();

    /// <summary>A context as the background services get it, from the same registration as the requests'.</summary>
    public ShopContext CreateDbContext() => new(ContextOptions().AddInterceptors(AfterRead).Options);

    /// <summary>A context for seeding rows in a test, which no host would do, so without the hand-over to the channel.</summary>
    public ShopContext CreateSeedContext() => new(new DbContextOptionsBuilder<ShopContext>().UseSqlite(_connection).Options);

    /// <summary>Another module's context on the same database: it maps the event tables without owning them.</summary>
    public BillingContext CreateBillingContext() => new(new DbContextOptionsBuilder<BillingContext>().UseSqlite(_connection).UsePlugboardEvents(_interceptor, owner: false).Options);

    public List<EventDelivery> Published()
    {
        var offered = new List<EventDelivery>();

        while (Channel.Reader.TryRead(out EventDelivery? delivery))
        {
            offered.Add(delivery);
        }

        return offered;
    }

    public void Dispose() => _connection.Dispose();

    private DbContextOptionsBuilder<ShopContext> ContextOptions() => new DbContextOptionsBuilder<ShopContext>().UseSqlite(_connection).UsePlugboardEvents(_interceptor, owner: true).AddInterceptors(Outage);
}

/// <summary>The context that owns the event tables.</summary>
internal sealed class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options), IEventStoreContext
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<EventEnvelope> Events => Set<EventEnvelope>();

    public DbSet<EventDelivery> Deliveries => Set<EventDelivery>();

    public DbSet<DeliveryHistory> DeliveryHistory => Set<DeliveryHistory>();

    public DbSet<DeadLetter> DeadLetters => Set<DeadLetter>();

    public DbSet<EventHost> EventHosts => Set<EventHost>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasKey(o => o.Id);
        modelBuilder.StoreDateTimeOffsetAsNumber();
    }
}

/// <summary>Another module's context: it publishes, so it maps the events and the queue.</summary>
internal sealed class BillingContext(DbContextOptions<BillingContext> options) : DbContext(options), IEventContext
{
    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<EventEnvelope> Events => Set<EventEnvelope>();

    public DbSet<EventDelivery> Deliveries => Set<EventDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>().HasKey(i => i.Id);
        modelBuilder.StoreDateTimeOffsetAsNumber();
    }
}

internal static class SqliteModelExtensions
{
    private static readonly ValueConverter<DateTimeOffset, long> AsNumber = new(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));

    /// <summary>
    /// SQLite has no DateTimeOffset; stored as a UTC number so the store's comparisons translate and stamps round-trip
    /// exactly. Test-only; SQL Server and PostgreSQL compare the type natively.
    /// </summary>
    public static void StoreDateTimeOffsetAsNumber(this ModelBuilder modelBuilder)
    {
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
        {
            if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
            {
                property.SetValueConverter(AsNumber);
            }
        }
    }
}

internal sealed class Invoice
{
    public int Id { get; set; }
}

[Event("invoice.issued")]
internal sealed record InvoiceIssued(int InvoiceId);

/// <summary>An entity raising its own events; stored with whichever save includes it.</summary>
internal sealed class Order : IHaveEventOutbox
{
    public int Id { get; set; }

    public string Customer { get; set; } = "";

    public EventOutbox Events { get; } = new();

    public void Place()
    {
        Customer = "placed";
        Events.Publish(new OrderPlaced(Id));
    }
}

[Event("order.placed")]
internal sealed record OrderPlaced(int OrderId);

internal sealed class Outage : DbCommandInterceptor
{
    public bool Unavailable { get; set; }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) => Check(result);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) => ValueTask.FromResult(Check(result));

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) => Check(result);

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) => ValueTask.FromResult(Check(result));

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result) => Check(result);

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) => ValueTask.FromResult(Check(result));

    private T Check<T>(T result) => Unavailable ? throw new InvalidOperationException("The database is unavailable.") : result;
}

internal sealed class AfterReadHook : DbCommandInterceptor
{
    public Func<Task>? Once { get; set; }

    public override async ValueTask<InterceptionResult> DataReaderClosingAsync(DbCommand command, DataReaderClosingEventData eventData, InterceptionResult result)
    {
        if (Once is { } once && command.CommandText.Contains(EventTables.DeliveryQueue, StringComparison.Ordinal))
        {
            Once = null;
            await once();
        }

        return result;
    }
}