using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Plugboard.Events.Tests;

/// <summary>The processor, sweeper and heartbeat against the real tables, on SQLite.</summary>
[TestFixture]
internal sealed class EventProcessorTests
{
    private static readonly HostId Host1 = new("host-1");
    private static readonly HostId Host2 = new("host-2");
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Event("order.placed")]
    private sealed record OrderPlaced(int OrderId);

    private sealed class Mailer
    {
        public List<int> Sent { get; } = [];
    }

    private sealed class Harness : IDisposable
    {
        public SqliteFixture Db { get; } = new(channelCapacity: EventSweeper<ShopContext>.Limit * 3);

        public Mailer Mailer { get; } = new();

        public EventProcessorOptions Options { get; } = new() { Host = Host1.Value };

        public FixedTime Time { get; } = new(Now);

        public EventEnvelope Envelope { get; } = EventEnvelope.Create(new OrderPlaced(42));

        public EventProcessor<ShopContext> Build(Action<SubscriptionBuilder> subscribe)
        {
            var builder = new SubscriptionBuilder();
            subscribe(builder);
            ServiceProvider services = new ServiceCollection().AddSingleton(Mailer).BuildServiceProvider();
            return new EventProcessor<ShopContext>(Db, Db.Channel, builder.Build(), services.GetRequiredService<IServiceScopeFactory>(),
                Time, Microsoft.Extensions.Options.Options.Create(Options), NullLogger<EventProcessor<ShopContext>>.Instance);
        }

        public EventSweeper<ShopContext> BuildSweeper(HostId? host = null) =>
            new(Db, Db.Channel, Microsoft.Extensions.Options.Options.Create(host is null ? Options : new EventProcessorOptions { Host = host.Value.Value }), Time, NullLogger<EventSweeper<ShopContext>>.Instance);

        public EventHeartbeat<ShopContext> BuildHeartbeat() =>
            new(Db, Microsoft.Extensions.Options.Options.Create(Options), Time, NullLogger<EventHeartbeat<ShopContext>>.Instance);

        /// <summary>Seeds one delivery of <see cref="Envelope"/> for send-confirmation, reserved for this host unless given.</summary>
        public async Task<EventDelivery> Seed(EventDelivery? delivery = null) =>
            await Seed(Envelope, delivery ?? Free().Reserve(Host1));

        /// <summary>Seeds the event once and the delivery in the queue; returns the row as the processor would hold it.</summary>
        public async Task<EventDelivery> Seed(EventEnvelope envelope, EventDelivery delivery)
        {
            await using ShopContext context = Db.CreateSeedContext();

            if (!await context.Events.AnyAsync(e => e.Id == envelope.Id))
            {
                context.Events.Add(envelope);
            }

            context.Deliveries.Add(delivery with { EventId = envelope.Id, Event = null! });
            await context.SaveChangesAsync();
            return delivery with { EventId = envelope.Id, Event = envelope };
        }

        public async Task Seed(params EventHost[] hosts)
        {
            await using ShopContext context = Db.CreateDbContext();
            context.EventHosts.AddRange(hosts);
            await context.SaveChangesAsync();
        }

        /// <summary>A delivery nobody owns, never handed to a worker.</summary>
        public EventDelivery Free(string handler = "send-confirmation") => new() { EventId = Envelope.Id, Subscription = handler };

        public static EventEnvelope AnotherEvent() => EventEnvelope.Create(new OrderPlaced(0));

        public async Task<List<EventDelivery>> Queue()
        {
            await using ShopContext context = Db.CreateDbContext();
            return await context.Deliveries.AsNoTracking().Include(d => d.Event).OrderBy(d => d.EventId).ToListAsync();
        }

        public async Task<EventDelivery> Row(EventDelivery delivery) => (await Queue()).Single(d => d.Id == delivery.Id);

        public async Task<List<DeliveryHistory>> History()
        {
            await using ShopContext context = Db.CreateDbContext();
            return await context.DeliveryHistory.AsNoTracking().Include(d => d.Event).ToListAsync();
        }

        public async Task<List<DeadLetter>> DeadLetters()
        {
            await using ShopContext context = Db.CreateDbContext();
            return await context.DeadLetters.AsNoTracking().Include(d => d.Event).ToListAsync();
        }

        /// <summary>The rows without their stamp, for comparing against a transition made at another time.</summary>
        public static List<EventDelivery> Unstamped(IEnumerable<EventDelivery> rows) => [.. rows.Select(Unstamped)];

        public static EventDelivery Unstamped(EventDelivery row) => row with { UpdatedAt = default };

        public async Task<List<EventHost>> Hosts()
        {
            await using ShopContext context = Db.CreateDbContext();
            return await context.EventHosts.AsNoTracking().OrderBy(h => h.Id).ToListAsync();
        }

        public void Dispose() => Db.Dispose();
    }

    [Test]
    public async Task Process_WithSucceedingHandler_ShouldMoveToHistory()
    {
        using var h = new Harness();
        HandlerContext? seen = null;
        EventProcessor<ShopContext> processor = h.Build(b => SubscriptionServices.Register<OrderPlaced>(b, "send-confirmation", (e, ctx, sp, ct) =>
        {
            seen = ctx;
            sp.GetRequiredService<Mailer>().Sent.Add(e.OrderId);
            return Task.CompletedTask;
        }));

        EventDelivery reserved = await h.Seed();
        bool processed = await processor.ProcessAsync(reserved, CancellationToken.None);

        Assert.That(processed, Is.True);
        Assert.That(h.Mailer.Sent, Is.EqualTo([42]));
        Assert.That(seen, Is.EqualTo(new HandlerContext(h.Envelope.Id, h.Envelope.OccurredAt, 1)));
        Assert.That(await h.Queue(), Is.Empty);
        DeliveryHistory row = (await h.History()).Single();
        Assert.That(row, Is.EqualTo(new DeliveryHistory
        {
            Id = reserved.Id,
            EventId = h.Envelope.Id,
            Subscription = "send-confirmation",
            Event = h.Envelope,
            Attempts = 1,
            FinishedAt = row.FinishedAt,
        }));
        Assert.That(row.FinishedAt, Is.EqualTo(DateTimeOffset.UtcNow).Within(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task Process_WithPostponingHandler_ShouldStayQueuedUntilTheDelayPasses()
    {
        using var h = new Harness();
        EventProcessor<ShopContext> processor = h.Build(b => SubscriptionServices.Register<OrderPlaced>(b, "send-confirmation",
            (e, ctx, sp, ct) =>
            {
                ctx.Postpone(TimeSpan.FromMinutes(5));
                return Task.CompletedTask;
            }));
        EventDelivery reserved = await h.Seed();

        await processor.ProcessAsync(reserved, CancellationToken.None);

        Assert.That(Harness.Unstamped((await h.Queue()).Single()), Is.EqualTo(Harness.Unstamped(reserved.Defer(Now.AddMinutes(5)))));
    }

    [Test]
    public async Task Process_WithThrowingHandler_ShouldScheduleRetryWithBackoff()
    {
        using var h = new Harness();
        EventProcessor<ShopContext> processor = h.Build(b => SubscriptionServices.Register<OrderPlaced>(b, "send-confirmation",
            (e, ctx, sp, ct) => throw new InvalidOperationException("mail server down")));
        EventDelivery reserved = await h.Seed();

        await processor.ProcessAsync(reserved, CancellationToken.None);

        Assert.That(Harness.Unstamped((await h.Queue()).Single()), Is.EqualTo(Harness.Unstamped(reserved.Defer(Now + h.Options.Backoff(1)))));
    }

    [Test]
    public async Task Process_WithThrowingHandlerOnLastAttempt_ShouldDeadLetter()
    {
        using var h = new Harness();
        EventProcessor<ShopContext> processor = h.Build(b => SubscriptionServices.Register<OrderPlaced>(b, "send-confirmation",
            (e, ctx, sp, ct) => throw new InvalidOperationException("still down")).WithMaxAttempts(3));
        EventDelivery reserved = await h.Seed((h.Free() with { Attempts = 2 }).Reserve(Host1));

        await processor.ProcessAsync(reserved, CancellationToken.None);

        Assert.That(await h.Queue(), Is.Empty);
        DeadLetter row = (await h.DeadLetters()).Single();
        Assert.That((row.Id, row.Attempts, row.Error), Is.EqualTo((reserved.Id, 3, "System.InvalidOperationException: still down")));
    }

    [Test]
    public async Task Process_PastMaxAttempts_ShouldDeadLetterWithoutRunning()
    {
        using var h = new Harness();
        var invoked = false;
        EventProcessor<ShopContext> processor = h.Build(b => SubscriptionServices.Register<OrderPlaced>(b, "send-confirmation", (e, ctx, sp, ct) =>
        {
            invoked = true;
            return Task.CompletedTask;
        }).WithMaxAttempts(3));
        // Three earlier hand-overs never reported back: the hosts died mid-run and the rows were freed each time.
        EventDelivery reserved = await h.Seed((h.Free() with { Attempts = 3 }).Reserve(Host1));

        await processor.ProcessAsync(reserved, CancellationToken.None);

        DeadLetter row = (await h.DeadLetters()).Single();
        Assert.That(invoked, Is.False);
        Assert.That(row.Error, Is.EqualTo("System.InvalidOperationException: Attempt 4 is past the limit of 3."));
    }

    [Test]
    public async Task Process_WithUnknownSubscription_ShouldDeadLetter()
    {
        using var h = new Harness();
        EventProcessor<ShopContext> processor = h.Build(_ => { });

        await processor.ProcessAsync(await h.Seed(), CancellationToken.None);

        DeadLetter row = (await h.DeadLetters()).Single();
        Assert.That(row.Error, Does.Contain("No subscription named 'send-confirmation'"));
    }

    [Test]
    public async Task Process_WhenAnotherHostOwnsTheRow_ShouldNotRecordTheOutcome()
    {
        using var h = new Harness();
        var invoked = false;
        EventProcessor<ShopContext> processor = h.Build(b => SubscriptionServices.Register<OrderPlaced>(b, "send-confirmation", (e, ctx, sp, ct) =>
        {
            invoked = true;
            return Task.CompletedTask;
        }));
        EventDelivery theirs = await h.Seed(h.Free().Reserve(Host2));

        bool processed = await processor.ProcessAsync(theirs with { LockedBy = Host1 }, CancellationToken.None);

        Assert.That((processed, invoked), Is.EqualTo((false, true)), "at least once: the handler ran, the row says another host owns the outcome");
        Assert.That((await h.Queue()).Single(), Is.EqualTo(theirs));
    }

    [Test]
    public async Task Process_WithStaleCopyOfARowWrittenSince_ShouldNotRecordTheOutcome()
    {
        using var h = new Harness();
        var invoked = 0;
        EventProcessor<ShopContext> processor = h.Build(b => SubscriptionServices.Register<OrderPlaced>(b, "send-confirmation", (e, ctx, sp, ct) =>
        {
            invoked++;
            return Task.CompletedTask;
        }));
        // Another worker of this host ran the row and deferred it; the channel still holds the older copy.
        EventDelivery reserved = h.Free().Reserve(Host1);
        EventDelivery deferred = await h.Seed(reserved.Defer(Now.AddMinutes(1)));

        bool processed = await processor.ProcessAsync(reserved with { Event = h.Envelope }, CancellationToken.None);

        Assert.That((processed, invoked), Is.EqualTo((false, 1)));
        Assert.That((await h.Queue()).Single(), Is.EqualTo(deferred));
    }

    [Test]
    public async Task Process_WhenHandlerExceedsMaxDuration_ShouldCancelItAndRetryWithTimeout()
    {
        using var h = new Harness();
        h.Time.UseSystemTimers = true;
        var cancelled = new TaskCompletionSource();
        EventProcessor<ShopContext> processor = h.Build(b => SubscriptionServices.Register<OrderPlaced>(b, "send-confirmation",
            async (e, ctx, sp, ct) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                catch (OperationCanceledException)
                {
                    cancelled.TrySetResult();
                    throw;
                }
            }).WithMaxDuration(TimeSpan.FromMilliseconds(50)));
        EventDelivery reserved = await h.Seed();

        await processor.ProcessAsync(reserved, CancellationToken.None);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(Harness.Unstamped((await h.Queue()).Single()), Is.EqualTo(Harness.Unstamped(reserved.Defer(Now + h.Options.Backoff(1)))));
    }

    [Test]
    public async Task Process_WhenHandlerIgnoresCancellation_ShouldStillMoveOn()
    {
        using var h = new Harness();
        h.Time.UseSystemTimers = true;
        var release = new TaskCompletionSource();
        EventProcessor<ShopContext> processor = h.Build(b => SubscriptionServices.Register<OrderPlaced>(b, "send-confirmation",
            (e, ctx, sp, ct) => release.Task).WithMaxDuration(TimeSpan.FromMilliseconds(50)));

        Task<bool> processing = processor.ProcessAsync(await h.Seed(), CancellationToken.None);

        Assert.That(await processing.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
        Assert.That((await h.Queue()).Single().NotBefore, Is.EqualTo(Now + h.Options.Backoff(1)));
        release.SetResult();
    }

    [Test]
    public async Task Process_WhenTheRowWasTakenOverDuringTheRun_ShouldNotRecordTheOutcome()
    {
        using var h = new Harness();
        EventProcessor<ShopContext> processor = h.Build(b => SubscriptionServices.Register<OrderPlaced>(b, "send-confirmation", async (e, ctx, sp, ct) =>
        {
            // This host was taken for dead meanwhile: its row was freed and another host took it.
            await using ShopContext context = h.Db.CreateDbContext();
            await context.Deliveries
                .Where(d => d.EventId == ctx.EventId && d.Subscription == "send-confirmation")
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.LockedBy, Host2).SetProperty(d => d.Version, d => d.Version + 2), ct);
        }));

        await processor.ProcessAsync(await h.Seed(), CancellationToken.None);

        Assert.That((await h.Queue()).Single().LockedBy, Is.EqualTo(Host2));
        Assert.That(await h.History(), Is.Empty);
    }

    [Test]
    public async Task Process_WhenTheDatabaseIsUnavailable_ShouldKeepTheOutcomeAndWriteItWhenItAnswers()
    {
        using var h = new Harness();
        h.Time.UseSystemTimers = true;
        var invoked = 0;
        EventProcessor<ShopContext> processor = h.Build(b => SubscriptionServices.Register<OrderPlaced>(b, "send-confirmation", (e, ctx, sp, ct) =>
        {
            invoked++;
            h.Db.Outage.Unavailable = true; // The database goes away while the handler runs; the outcome write has to wait for it.
            return Task.CompletedTask;
        }));
        processor.StoreRetryDelay = TimeSpan.FromMilliseconds(10);
        EventDelivery reserved = await h.Seed();

        Task<bool> processing = processor.ProcessAsync(reserved, CancellationToken.None);
        await Task.Delay(50);
        Assert.That((processing.IsCompleted, invoked), Is.EqualTo((false, 1)), "the outcome waits for the database");

        h.Db.Outage.Unavailable = false;
        Assert.That(await processing.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
        Assert.That((await h.History()).Single().Id, Is.EqualTo(reserved.Id));
    }

    [Test]
    public async Task Run_ShouldProcessDeliveriesPublishedOnTheChannel()
    {
        using var h = new Harness();
        h.Time.UseSystemTimers = true;
        var handled = new TaskCompletionSource();
        EventProcessor<ShopContext> processor = h.Build(b => SubscriptionServices.Register<OrderPlaced>(b, "send-confirmation", (e, ctx, sp, ct) =>
        {
            handled.TrySetResult();
            return Task.CompletedTask;
        }));
        EventDelivery reserved = await h.Seed();
        h.Db.Channel.Writer.TryWrite(reserved);

        await processor.StartAsync(CancellationToken.None);
        await handled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        List<DeliveryHistory> history = await Eventually(h.History, rows => rows.Count > 0);
        await processor.StopAsync(CancellationToken.None);

        Assert.That(history.Single().Id, Is.EqualTo(reserved.Id));
    }

    [Test]
    public async Task Sweep_ShouldPublishDueFreeAndOwnDeliveriesOldestFirstWithTheirEvent()
    {
        using var h = new Harness();
        EventDelivery fresh = await h.Seed(h.Free());
        EventDelivery freed = await h.Seed(Harness.AnotherEvent(), h.Free() with { Attempts = 1, NotBefore = Now.AddSeconds(-5) });
        EventDelivery ownRetry = await h.Seed(Harness.AnotherEvent(), h.Free().Reserve(Host1).Defer(Now.AddSeconds(-10)));
        EventDelivery ownLater = await h.Seed(Harness.AnotherEvent(), h.Free().Reserve(Host1).Defer(Now.AddMinutes(10)));
        EventDelivery ownWithWorker = await h.Seed(Harness.AnotherEvent(), h.Free().Reserve(Host1));
        EventDelivery theirRetry = await h.Seed(Harness.AnotherEvent(), h.Free().Reserve(Host2).Defer(Now.AddSeconds(-5)));
        EventDelivery theirs = await h.Seed(Harness.AnotherEvent(), h.Free().Reserve(Host2));
        EventDelivery notDue = await h.Seed(Harness.AnotherEvent(), h.Free() with { NotBefore = Now.AddMinutes(10) });

        await h.BuildSweeper().SweepAsync(CancellationToken.None);

        Assert.That(Harness.Unstamped(h.Db.Published()), Is.EqualTo(Harness.Unstamped([fresh.Reserve(Host1), ownRetry.Reserve(Host1), freed.Reserve(Host1)])));
        Assert.That(Harness.Unstamped(await h.Row(ownRetry)), Is.EqualTo(Harness.Unstamped(ownRetry.Reserve(Host1))));
        Assert.That(Harness.Unstamped(await h.Row(freed)), Is.EqualTo(Harness.Unstamped(freed.Reserve(Host1))));
        Assert.That(Harness.Unstamped(await h.Row(fresh)), Is.EqualTo(Harness.Unstamped(fresh.Reserve(Host1))));
        Assert.That(await h.Row(ownLater), Is.EqualTo(ownLater));
        Assert.That(await h.Row(ownWithWorker), Is.EqualTo(ownWithWorker));
        Assert.That(await h.Row(theirRetry), Is.EqualTo(theirRetry));
        Assert.That(await h.Row(theirs), Is.EqualTo(theirs));
        Assert.That(await h.Row(notDue), Is.EqualTo(notDue));
    }

    [Test]
    public async Task Sweep_Twice_ShouldPublishAnOwnRetryOnce()
    {
        using var h = new Harness();
        EventDelivery ownRetry = await h.Seed(h.Free().Reserve(Host1).Defer(Now.AddSeconds(-5)));

        await h.BuildSweeper().SweepAsync(CancellationToken.None);
        await h.BuildSweeper().SweepAsync(CancellationToken.None);

        Assert.That(Harness.Unstamped(h.Db.Published()), Is.EqualTo(Harness.Unstamped([ownRetry.Reserve(Host1)])));
    }

    [Test]
    public async Task Sweep_ShouldGiveARowToOneHostOnly()
    {
        using var h = new Harness();
        EventDelivery free = await h.Seed(h.Free());

        await h.BuildSweeper().SweepAsync(CancellationToken.None);
        await h.BuildSweeper(Host2).SweepAsync(CancellationToken.None);

        Assert.That(Harness.Unstamped(h.Db.Published()), Is.EqualTo(Harness.Unstamped([free.Reserve(Host1)])));
        Assert.That((await h.Row(free)).LockedBy, Is.EqualTo(Host1));
    }

    [Test]
    public async Task Sweep_WhenARowChangedSinceItWasRead_ShouldLeaveItAlone()
    {
        using var h = new Harness();
        EventDelivery snapshot = await h.Seed(h.Free());
        // Another host takes and defers the row between this host reading the page and claiming it.
        EventDelivery deferred = snapshot.Reserve(Host2).Defer(Now.AddMinutes(1));
        h.Db.AfterRead.Once = async () =>
        {
            await using ShopContext context = h.Db.CreateDbContext();
            context.Deliveries.Update(deferred with { Event = null! });
            await context.SaveChangesAsync();
        };

        await h.BuildSweeper().SweepAsync(CancellationToken.None);

        Assert.That(h.Db.Published(), Is.Empty);
        Assert.That(await h.Row(snapshot), Is.EqualTo(deferred));
    }

    [Test]
    public async Task Sweeper_ShouldKeepSweepingOnItsTimer()
    {
        using var h = new Harness();
        h.Time.UseSystemTimers = true;
        h.Options.SweepInterval = TimeSpan.FromMilliseconds(20);
        EventSweeper<ShopContext> sweeper = h.BuildSweeper();

        await sweeper.StartAsync(CancellationToken.None);
        await Task.Delay(50); // The sweep at start found nothing; only a later sweep can find this one.
        EventDelivery due = await h.Seed(h.Free());
        EventDelivery published = await h.Db.Channel.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await sweeper.StopAsync(CancellationToken.None);

        Assert.That(Harness.Unstamped(published), Is.EqualTo(Harness.Unstamped(due.Reserve(Host1))));
    }

    [Test]
    public async Task Sweep_ShouldPageUntilNothingIsLeftToClaim()
    {
        using var h = new Harness();

        for (var i = 0; i < EventSweeper<ShopContext>.Limit * 2 + 1; i++)
        {
            await h.Seed(Harness.AnotherEvent(), h.Free());
        }

        await h.BuildSweeper().SweepAsync(CancellationToken.None);

        Assert.That(h.Db.Channel.Reader.Count, Is.EqualTo(EventSweeper<ShopContext>.Limit * 2 + 1));
        Assert.That((await h.Queue()).All(r => r.LockedBy == Host1 && r.Attempts == 1), Is.True);
    }

    [Test]
    public async Task HeartbeatThenSweep_ShouldRecoverADeadHostsRetryWhenItIsDue()
    {
        using var h = new Harness();
        h.Options.HostTimeout = TimeSpan.FromMinutes(5);
        var dead = new HostId("dead");
        await h.Seed(new EventHost(dead, Now.AddMinutes(-10)));
        EventDelivery retry = await h.Seed(h.Free().Reserve(dead).Defer(Now.AddMinutes(1)));

        await h.BuildHeartbeat().StartAsync(CancellationToken.None);
        await h.BuildSweeper().SweepAsync(CancellationToken.None);
        Assert.That(h.Db.Published(), Is.Empty);
        Assert.That(Harness.Unstamped(await h.Row(retry)), Is.EqualTo(Harness.Unstamped(retry.Free())));

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.BuildSweeper().SweepAsync(CancellationToken.None);

        Assert.That(Harness.Unstamped(h.Db.Published()), Is.EqualTo(Harness.Unstamped([retry.Free().Reserve(Host1)])));
    }

    [Test]
    public async Task HeartbeatStart_ShouldWriteTheHostFreeRowsOfStaleAndUnknownHostsAndForgetThem()
    {
        using var h = new Harness();
        h.Options.HostTimeout = TimeSpan.FromMinutes(5);
        var alive = new HostId("alive");
        var stale = new HostId("stale");
        var unknown = new HostId("unknown");
        await h.Seed(new EventHost(alive, Now.AddMinutes(-1)), new EventHost(stale, Now.AddMinutes(-10)));
        EventDelivery ofAlive = await h.Seed(h.Free().Reserve(alive));
        EventDelivery ofStale = await h.Seed(Harness.AnotherEvent(), h.Free().Reserve(stale));
        EventDelivery ofUnknown = await h.Seed(Harness.AnotherEvent(), h.Free().Reserve(unknown));
        EventDelivery free = await h.Seed(Harness.AnotherEvent(), h.Free());

        await h.BuildHeartbeat().StartAsync(CancellationToken.None);

        Assert.That(await h.Row(ofAlive), Is.EqualTo(ofAlive));
        Assert.That(Harness.Unstamped(await h.Row(ofStale)), Is.EqualTo(Harness.Unstamped(ofStale.Free())), "freed, the attempt it was handed out for still counts");
        Assert.That((await h.Row(ofUnknown)).LockedBy, Is.Null, "an owner without a heartbeat row is as dead as a stale one");
        Assert.That(await h.Row(free), Is.EqualTo(free));
        Assert.That(await h.Hosts(), Is.EqualTo([new EventHost(alive, Now.AddMinutes(-1)), new EventHost(Host1, Now)]));
    }

    [Test]
    public async Task Heartbeat_ShouldUpdateTheHostOnEveryBeat()
    {
        using var h = new Harness();
        h.Time.UseSystemTimers = true;
        h.Options.HostTimeout = TimeSpan.FromMilliseconds(60); // Beats every 10 ms.
        EventHeartbeat<ShopContext> heartbeat = h.BuildHeartbeat();

        await heartbeat.StartAsync(CancellationToken.None);
        Assert.That(await h.Hosts(), Is.EqualTo([new EventHost(Host1, Now)]), "written before anything else starts");

        // host-2 died with a delivery at a worker; its heartbeat is two minutes old, so only a later beat can free it.
        EventDelivery dead = await h.Seed(h.Free().Reserve(Host2));
        await h.Seed(new EventHost(Host2, Now.AddMinutes(-2)));

        EventDelivery freed = await Eventually(() => h.Row(dead), row => row.LockedBy is null);

        Assert.That(Harness.Unstamped(freed), Is.EqualTo(Harness.Unstamped(dead.Free())));
        Assert.That((await h.Hosts()).Select(x => x.Id), Is.EqualTo([Host1]));

        await heartbeat.StopAsync(CancellationToken.None);
        Assert.That(await h.Hosts(), Is.Empty);
    }

    [Test]
    public async Task HeartbeatStop_ShouldReleaseTheRowsThisHostOwnsAndForgetTheHost()
    {
        using var h = new Harness();
        await h.Seed(new EventHost(Host2, Now));
        EventDelivery withWorker = await h.Seed(h.Free().Reserve(Host1));
        EventDelivery retry = await h.Seed(Harness.AnotherEvent(), h.Free().Reserve(Host1).Defer(Now.AddMinutes(1)));
        EventDelivery other = await h.Seed(Harness.AnotherEvent(), h.Free().Reserve(Host2));
        EventHeartbeat<ShopContext> heartbeat = h.BuildHeartbeat();

        await heartbeat.StartAsync(CancellationToken.None);
        await heartbeat.StopAsync(CancellationToken.None);

        Assert.That(Harness.Unstamped(await h.Row(withWorker)), Is.EqualTo(Harness.Unstamped(withWorker.Free())));
        Assert.That(Harness.Unstamped(await h.Row(retry)), Is.EqualTo(Harness.Unstamped(retry.Free())), "a retry keeps its time, any host may run it then");
        Assert.That(await h.Row(other), Is.EqualTo(other));
        Assert.That((await h.Hosts()).Select(x => x.Id), Is.EqualTo([Host2]));
    }

    [Test]
    public async Task HeartbeatStop_WhileAHandlerIsRunning_ShouldStopTheProcessorFirstAndFreeItsRow()
    {
        using var h = new Harness();
        h.Time.UseSystemTimers = true;
        var started = new TaskCompletionSource();
        EventProcessor<ShopContext> processor = h.Build(b => SubscriptionServices.Register<OrderPlaced>(b, "send-confirmation", async (e, ctx, sp, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }));
        h.Db.Channel.Writer.TryWrite(await h.Seed());
        EventDelivery queued = await h.Seed(Harness.AnotherEvent(), h.Free().Reserve(Host1));

        EventHeartbeat<ShopContext> heartbeat = h.BuildHeartbeat();
        await heartbeat.StartAsync(CancellationToken.None);
        await processor.StartAsync(CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await processor.StopAsync(CancellationToken.None);
        await heartbeat.StopAsync(CancellationToken.None);

        Assert.That((await h.Queue()).Select(r => (r.EventId, r.LockedBy, r.Attempts)), Is.EquivalentTo(new[]
        {
            (h.Envelope.Id, (HostId?)null, 1),
            (queued.EventId, (HostId?)null, 1),
        }));
        Assert.That(await h.Hosts(), Is.Empty);
    }

    /// <summary>Polls until the background service got there, or five seconds passed; returns the last reading.</summary>
    private static async Task<T> Eventually<T>(Func<Task<T>> read, Func<T, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        T value = await read();

        while (!done(value) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
            value = await read();
        }

        return value;
    }

    /// <summary>Fixed clock for deterministic timestamps; timers run on the system clock when a test needs real waiting.</summary>
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public bool UseSystemTimers { get; set; }

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            UseSystemTimers ? System.CreateTimer(callback, state, dueTime, period) : System.CreateTimer(callback, state, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }
}
