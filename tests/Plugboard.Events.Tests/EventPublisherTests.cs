using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Threading.Channels;

namespace Plugboard.Events.Tests;

/// <summary>Publishing through an IEventContext or an entity's outbox: stored with SaveChanges, handed to the channel once committed.</summary>
[TestFixture]
internal sealed class EventPublisherTests
{
    private static readonly HostId Host1 = new("host-1");

    private static readonly Action<SubscriptionBuilder> TwoSubscriptions = b =>
    {
        SubscriptionServices.Register<OrderPlaced>(b, "send-confirmation", (e, ctx, sp, ct) => Task.CompletedTask);
        SubscriptionServices.RegisterAll(b, "audit", (e, ctx, sp, ct) => Task.CompletedTask);
    };

    [Test]
    public async Task SaveChanges_WithAnEntityThatPublishedAnEvent_ShouldStoreEventAndOneDeliveryPerSubscriptionAndPublishThem()
    {
        using var db = new SqliteFixture(TwoSubscriptions);
        var order = new Order { Id = 1 };

        await using (ShopContext context = db.CreateScopedContext())
        {
            order.Place();
            context.Orders.Add(order);
            await context.SaveChangesAsync();
        }

        await using ShopContext check = db.CreateDbContext();
        EventEnvelope envelope = await check.Events.SingleAsync();
        List<EventDelivery> queue = await check.Deliveries.OrderBy(d => d.Subscription).ToListAsync();

        Assert.That((envelope.Name, envelope.Payload), Is.EqualTo(("order.placed", """{"OrderId":1}""")));
        Assert.That(queue.Select(d => (d.EventId, d.Subscription, d.LockedBy, d.Attempts)), Is.EqualTo(new[]
        {
            (envelope.Id, "audit", (HostId?)Host1, 1),
            (envelope.Id, "send-confirmation", (HostId?)Host1, 1),
        }), "born reserved for this host's workers, which counts as the first attempt");
        Assert.That(db.Published().Select(d => (d.Subscription, d.Event.Id)), Is.EquivalentTo([("audit", envelope.Id), ("send-confirmation", envelope.Id)]));
    }

    [Test]
    public async Task SaveChanges_WithOnlyAPublishedEvent_ShouldStoreAndPublishIt()
    {
        using var db = new SqliteFixture(TwoSubscriptions);

        await using (ShopContext context = db.CreateScopedContext())
        {
            // A service that changed something elsewhere publishes and saves the context itself.
            context.Publish(new OrderPlaced(1));
            await context.SaveChangesAsync();
        }

        await using ShopContext check = db.CreateDbContext();
        Assert.That(await check.Deliveries.CountAsync(), Is.EqualTo(2));
        Assert.That(db.Published().Select(d => d.Event.Payload), Is.EquivalentTo(["""{"OrderId":1}""", """{"OrderId":1}"""]));
    }

    [Test]
    public void Registration_ShouldResolveTheHostedServicesAndMapAndAttachEveryEventContextOnce()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        using ServiceProvider services = new ServiceCollection()
            .AddSingleton(NullLoggerFactory.Instance).AddLogging()
            .AddDbContextFactory<ShopContext>(o => o.UseSqlite(connection))
            .AddDbContextFactory<BillingContext>(o => o.UseSqlite(connection))
            .AddEventContext<ShopContext>()    // what the generated registration does for every IEventContext in a module, in any order
            .AddPlugboardEvents<ShopContext>()
            .AddEventContext<BillingContext>()
            .BuildServiceProvider(validateScopes: true);

        using ShopContext shop = services.GetRequiredService<IDbContextFactory<ShopContext>>().CreateDbContext();
        using BillingContext billing = services.GetRequiredService<IDbContextFactory<BillingContext>>().CreateDbContext();

        Assert.That(services.GetServices<IHostedService>().Select(s => s.GetType().GetGenericTypeDefinition()),
            Is.EqualTo([typeof(EventHeartbeat<>), typeof(EventProcessor<>), typeof(EventSweeper<>)]));
        Assert.That(Interceptors(shop).OfType<EventContextInterceptor>().Count(), Is.EqualTo(1));
        Assert.That(Interceptors(billing).OfType<EventContextInterceptor>().Count(), Is.EqualTo(1));
        Assert.That(shop.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(EventDelivery))!.IsTableExcludedFromMigrations(), Is.False, "the context that owns the tables migrates them");
        Assert.That(billing.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(EventDelivery))!.IsTableExcludedFromMigrations(), Is.True, "every other context maps them without migrating");
    }

    private static IEnumerable<IInterceptor> Interceptors(DbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.Interceptors ?? [];

    [Test]
    public async Task SaveChanges_InsideYourOwnTransaction_ShouldPublishWhenItCommits()
    {
        using var db = new SqliteFixture(TwoSubscriptions);

        await using ShopContext context = db.CreateScopedContext();
        await using (IDbContextTransaction transaction = await context.Database.BeginTransactionAsync())
        {
            context.Publish(new OrderPlaced(1));
            await context.SaveChangesAsync();
            context.Publish(new OrderPlaced(2));
            await context.SaveChangesAsync();
            Assert.That(db.Published(), Is.Empty, "nothing is committed yet");

            await transaction.CommitAsync();
        }

        Assert.That(db.Published().Select(d => d.Event.Payload), Is.EquivalentTo(["""{"OrderId":1}""", """{"OrderId":1}""", """{"OrderId":2}""", """{"OrderId":2}"""]));
    }

    [Test]
    public async Task SaveChanges_InsideYourOwnTransactionThatRollsBack_ShouldPublishNothingOfIt()
    {
        using var db = new SqliteFixture(TwoSubscriptions);

        await using ShopContext context = db.CreateScopedContext();
        await using (IDbContextTransaction transaction = await context.Database.BeginTransactionAsync())
        {
            context.Publish(new OrderPlaced(1));
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        context.ChangeTracker.Clear();
        context.Publish(new OrderPlaced(2));
        await context.SaveChangesAsync();

        Assert.That(db.Published().Select(d => d.Event.Payload), Is.EquivalentTo(["""{"OrderId":2}""", """{"OrderId":2}"""]));
    }

    [Test]
    public void SaveChanges_InsideYourOwnTransaction_Synchronously_ShouldPublishWhenItCommits()
    {
        using var db = new SqliteFixture(TwoSubscriptions);

        using ShopContext context = db.CreateScopedContext();
        using (IDbContextTransaction transaction = context.Database.BeginTransaction())
        {
            context.Publish(new OrderPlaced(1));
            context.SaveChanges();
            Assert.That(db.Published(), Is.Empty);
            transaction.Commit();
        }

        Assert.That(db.Published(), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task SaveChanges_InsideATransactionLeftOpenOnAPooledContext_ShouldNotPublishOnTheNextLease()
    {
        using var db = new SqliteFixture(TwoSubscriptions);

        await using ShopContext context = db.CreateScopedContext();
        await using (await context.Database.BeginTransactionAsync())
        {
            context.Publish(new OrderPlaced(1));
            await context.SaveChangesAsync();
        } // disposed without commit: rolled back, and the pool would reset the context now

        context.ChangeTracker.Clear();
        await using (IDbContextTransaction next = await context.Database.BeginTransactionAsync())
        {
            context.Publish(new OrderPlaced(2));
            await context.SaveChangesAsync();
            await next.CommitAsync();
        }

        Assert.That(db.Published().Select(d => d.Event.Payload), Is.EquivalentTo(["""{"OrderId":2}""", """{"OrderId":2}"""]));
    }

    [Test]
    public async Task SaveChanges_OnAPooledContext_ShouldPublishWhatEachLeaseSaved()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        await using ServiceProvider services = new ServiceCollection()
            .AddDbContextPool<ShopContext>(o => o.UseSqlite(connection))
            .AddPlugboardEvents<ShopContext>(o => o.Host = "host-1")
            .AddSubscriptions(b => SubscriptionServices.Register<OrderPlaced>(b, "send-confirmation", (e, ctx, sp, ct) => Task.CompletedTask))
            .BuildServiceProvider(validateScopes: true);
        var channel = services.GetRequiredService<Channel<EventDelivery>>();

        using (IServiceScope scope = services.CreateScope())
        {
            ShopContext context = scope.ServiceProvider.GetRequiredService<ShopContext>();
            await context.Database.EnsureCreatedAsync();
            context.Publish(new OrderPlaced(1));
            await context.SaveChangesAsync();
        }

        using (IServiceScope scope = services.CreateScope())
        {
            ShopContext context = scope.ServiceProvider.GetRequiredService<ShopContext>();
            context.Publish(new OrderPlaced(2));
            await context.SaveChangesAsync();
        }

        Assert.That((await channel.Reader.ReadAsync()).Event.Payload, Is.EqualTo("""{"OrderId":1}"""));
        Assert.That((await channel.Reader.ReadAsync()).Event.Payload, Is.EqualTo("""{"OrderId":2}"""));
        Assert.That(channel.Reader.TryRead(out _), Is.False);
    }

    [Test]
    public void Publish_WithTypeNotMarkedAsEvent_ShouldThrow()
    {
        using var db = new SqliteFixture(TwoSubscriptions);
        using ShopContext context = db.CreateScopedContext();

        Assert.That(() => context.Publish(new Order()), Throws.ArgumentException.With.Message.Contains("[Event]"));
    }

    [Test]
    public void Publish_OnAnOutboxWithTypeNotMarkedAsEvent_ShouldThrow()
    {
        var order = new Order();

        Assert.That(() => order.Events.Publish(new Order()), Throws.ArgumentException.With.Message.Contains("[Event]"));
    }

    [Test]
    public async Task SaveChanges_ThatFailsAndIsRetried_WithAnEntityOutbox_ShouldStoreAndPublishOnce()
    {
        using var db = new SqliteFixture(TwoSubscriptions);
        var order = new Order { Id = 1 };
        order.Place();

        await using ShopContext context = db.CreateScopedContext();
        context.Orders.Add(order);
        db.Outage.Unavailable = true;
        Assert.That(() => context.SaveChangesAsync(), Throws.Exception);

        db.Outage.Unavailable = false;
        await context.SaveChangesAsync();

        await using ShopContext check = db.CreateDbContext();
        Assert.That(await check.Events.CountAsync(), Is.EqualTo(1));
        Assert.That(await check.Deliveries.CountAsync(), Is.EqualTo(2));
        Assert.That(db.Published(), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task SaveChanges_WithAnEntityRaisingAfterItWasSaved_ShouldStoreTheNewEventsOnly()
    {
        using var db = new SqliteFixture(TwoSubscriptions);
        var order = new Order { Id = 1 };

        await using ShopContext context = db.CreateScopedContext();
        order.Place();
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        order.Place();
        await context.SaveChangesAsync();

        await using ShopContext check = db.CreateDbContext();
        Assert.That(await check.Events.CountAsync(), Is.EqualTo(2));
        Assert.That(db.Published(), Has.Count.EqualTo(4));
    }

    [Test]
    public async Task SaveChanges_Twice_ShouldPublishEachDeliveryOnce()
    {
        using var db = new SqliteFixture(TwoSubscriptions);

        await using ShopContext context = db.CreateScopedContext();
        context.Publish(new OrderPlaced(1));
        await context.SaveChangesAsync();
        context.Publish(new OrderPlaced(2));
        await context.SaveChangesAsync();

        Assert.That(db.Published().Select(d => d.Event.Payload), Is.EquivalentTo(["""{"OrderId":1}""", """{"OrderId":1}""", """{"OrderId":2}""", """{"OrderId":2}"""]));
    }

    [Test]
    public async Task SaveChanges_ThatFailsAndIsRetried_ShouldStoreAndPublishOnce()
    {
        using var db = new SqliteFixture(TwoSubscriptions);

        await using ShopContext context = db.CreateScopedContext();
        context.Orders.Add(new Order { Id = 1 });
        context.Publish(new OrderPlaced(1));
        db.Outage.Unavailable = true;
        Assert.That(() => context.SaveChangesAsync(), Throws.Exception);
        Assert.That(db.Published(), Is.Empty);

        db.Outage.Unavailable = false;
        await context.SaveChangesAsync();

        await using ShopContext check = db.CreateDbContext();
        Assert.That(await check.Deliveries.CountAsync(), Is.EqualTo(2));
        Assert.That(db.Published(), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task SaveChanges_AfterAFailedSaveWasDiscarded_ShouldPublishNothingOfIt()
    {
        using var db = new SqliteFixture(TwoSubscriptions);

        await using ShopContext context = db.CreateScopedContext();
        context.Publish(new OrderPlaced(1));
        db.Outage.Unavailable = true;
        Assert.That(() => context.SaveChangesAsync(), Throws.Exception);
        db.Outage.Unavailable = false;

        context.ChangeTracker.Clear(); // what a pooled context does on its way back to the pool
        context.Orders.Add(new Order { Id = 2 });
        await context.SaveChangesAsync();

        Assert.That(db.Published(), Is.Empty);
    }

    [Test]
    public void SaveChanges_Synchronously_ShouldPublishToo()
    {
        using var db = new SqliteFixture(TwoSubscriptions);

        using ShopContext context = db.CreateScopedContext();
        context.Publish(new OrderPlaced(1));
        context.SaveChanges();

        Assert.That(db.Published(), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task SaveChanges_WithoutSubscriptions_ShouldStoreTheEventWithoutDeliveries()
    {
        using var db = new SqliteFixture();

        await using (ShopContext context = db.CreateScopedContext())
        {
            context.Publish(new OrderPlaced(1));
            await context.SaveChangesAsync();
        }

        await using ShopContext check = db.CreateDbContext();
        Assert.That(await check.Events.CountAsync(), Is.EqualTo(1));
        Assert.That(await check.Deliveries.CountAsync(), Is.Zero);
        Assert.That(db.Published(), Is.Empty);
    }

    [Test]
    public async Task SaveChanges_OnAnotherModulesContext_ShouldStoreTheEventWithThatModulesChanges()
    {
        using var db = new SqliteFixture(b => SubscriptionServices.Register<InvoiceIssued>(b, "book-invoice", (e, ctx, sp, ct) => Task.CompletedTask));
        var invoice = new Invoice { Id = 1 };

        await using (BillingContext billing = db.CreateBillingContext())
        {
            Assert.That(billing.GetService<IDesignTimeModel>().Model.GetEntityTypes().Where(e => e.ClrType != typeof(Invoice)).All(e => e.IsTableExcludedFromMigrations()), Is.True);
            billing.Publish(new InvoiceIssued(invoice.Id));
            billing.Invoices.Add(invoice);
            await billing.SaveChangesAsync();
        }

        await using ShopContext check = db.CreateDbContext();
        EventDelivery row = await check.Deliveries.SingleAsync();
        Assert.That((row.Subscription, row.LockedBy), Is.EqualTo(("book-invoice", (HostId?)Host1)));
        Assert.That(db.Published().Single().Event.Name, Is.EqualTo("invoice.issued"));
    }

    [Test]
    public async Task SaveChanges_WhenTheChannelIsFull_ShouldWaitForRoomInsteadOfDropping()
    {
        using var db = new SqliteFixture(TwoSubscriptions, channelCapacity: 1);
        db.Channel.Writer.TryWrite(new EventDelivery { EventId = Guid.NewGuid(), Subscription = "filler" });

        await using ShopContext context = db.CreateScopedContext();
        context.Publish(new OrderPlaced(1));
        Task saving = context.SaveChangesAsync();

        await Task.Delay(100);
        Assert.That(saving.IsCompleted, Is.False, "two deliveries wait behind the filler");
        Assert.That((await db.Channel.Reader.ReadAsync()).Subscription, Is.EqualTo("filler"));
        await Task.Delay(100);
        Assert.That(saving.IsCompleted, Is.False, "the second delivery still waits");
        await db.Channel.Reader.ReadAsync();
        await saving.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(db.Published(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SaveChanges_OfRowsTheProcessorWrites_ShouldHandNothingOver()
    {
        using var db = new SqliteFixture(TwoSubscriptions);
        var envelope = EventEnvelope.Create(new OrderPlaced(1));

        await using (ShopContext seed = db.CreateSeedContext())
        {
            seed.Events.Add(envelope);
            await seed.SaveChangesAsync();
        }

        await using ShopContext context = db.CreateDbContext();
        context.Events.Attach(envelope);
        // A finished row moving to history and a failed one to the dead letters, both tracked with their stored event.
        context.DeliveryHistory.Add(new EventDelivery { EventId = envelope.Id, Subscription = "audit", Event = envelope }.Reserve(Host1).Finished() with { Event = envelope });
        context.DeadLetters.Add(new EventDelivery { EventId = envelope.Id, Subscription = "send-confirmation", Event = envelope }.Reserve(Host1).DeadLettered(new TimeoutException()) with { Event = envelope });
        await context.SaveChangesAsync();

        Assert.That(db.Published(), Is.Empty);
    }

    [Test]
    public async Task SaveChanges_WithQueueRowsAttachedOrLoaded_ShouldHandNothingOver()
    {
        using var db = new SqliteFixture(TwoSubscriptions);

        await using (ShopContext publishing = db.CreateScopedContext())
        {
            publishing.Publish(new OrderPlaced(1));
            await publishing.SaveChangesAsync();
            db.Published();
        }

        await using ShopContext context = db.CreateDbContext();
        List<EventDelivery> loaded = await context.Deliveries.Include(d => d.Event).ToListAsync();
        context.Deliveries.Attach(new EventDelivery { EventId = loaded[0].EventId, Subscription = "attached", Event = loaded[0].Event }.Reserve(Host1));
        context.Orders.Add(new Order { Id = 2 });
        await context.SaveChangesAsync();

        Assert.That(db.Published(), Is.Empty);
    }
}