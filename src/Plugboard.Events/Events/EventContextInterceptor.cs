using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Plugboard.Events;

/// <summary>
/// Stores what the tracked entities raised as event rows, adds one delivery per subscription for every event the save
/// inserts, and hands the deliveries to this host's channel once they are committed: at the end of a plain save, or
/// when the transaction the save ran in is committed.
/// </summary>
internal sealed class EventContextInterceptor(Channel<EventDelivery> channel, IEnumerable<Subscription> subscriptions, IOptions<EventProcessorOptions> options)
    : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly Subscription[] _subscriptions = [.. subscriptions];
    private readonly HostId _host = new(options.Value.Host);
    private readonly ConditionalWeakTable<DbContext, Batch> _batches = [];

    /// <inheritdoc/>
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Store(eventData.Context);
        return result;
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Store(eventData.Context);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc/>
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        SavedAsync(eventData.Context).GetAwaiter().GetResult();
        return result;
    }

    /// <inheritdoc/>
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await SavedAsync(eventData.Context).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc/>
    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        CommittedAsync(eventData).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
        CommittedAsync(eventData);

    /// <inheritdoc/>
    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => RolledBack(eventData);

    /// <inheritdoc/>
    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        RolledBack(eventData);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds the events the tracked entities published, then the deliveries of every event this save inserts. Rows of
    /// a failed save go out with this one as long as the context still tracks them; a batch parked for a transaction
    /// that is gone is dropped.
    /// </summary>
    private void Store(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        Batch batch = _batches.GetValue(context, static _ => new Batch());

        if (batch.TransactionId is { } parkedFor && parkedFor != context.Database.CurrentTransaction?.TransactionId)
        {
            batch.Parked.Clear();
            batch.TransactionId = null;
        }

        DbSet<EventDelivery> queue = context.Set<EventDelivery>();
        batch.Saving.RemoveAll(delivery => !queue.Local.Contains(delivery));
        HashSet<Guid> delivered = [.. batch.Saving.Select(static delivery => delivery.EventId)];

        DbSet<EventEnvelope> events = context.Set<EventEnvelope>();
        List<EventOutbox> outboxes = [.. context.ChangeTracker.Entries().Select(static entry => entry.Entity).OfType<IHaveEventOutbox>().Select(static entity => entity.Events)];

        foreach (EventOutbox outbox in outboxes)
        {
            events.AddRange(outbox.Drain());
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<EventEnvelope> inserted = [.. context.ChangeTracker.Entries<EventEnvelope>()
            .Where(entry => entry.State == EntityState.Added && !delivered.Contains(entry.Entity.Id))
            .Select(static entry => entry.Entity)];

        foreach (EventEnvelope envelope in inserted)
        {
            foreach (Subscription subscription in _subscriptions.Where(s => s.EventName is null || s.EventName == envelope.Name))
            {
                // Born reserved for this host's workers: the first attempt counts from here.
                var delivery = new EventDelivery { EventId = envelope.Id, Subscription = subscription.Name, Event = envelope, UpdatedAt = now }.Reserve(_host);
                queue.Add(delivery);
                batch.Saving.Add(delivery);
            }
        }
    }

    /// <summary>Publishes the saved rows, or parks them until the open transaction commits.</summary>
    private async Task SavedAsync(DbContext? context)
    {
        if (context is null || !_batches.TryGetValue(context, out Batch? batch))
        {
            return;
        }

        if (context.Database.CurrentTransaction is { } transaction)
        {
            batch.TransactionId = transaction.TransactionId;
            batch.Parked.AddRange(batch.Saving);
            batch.Saving.Clear();
            return;
        }

        List<EventDelivery> inserted = [.. batch.Saving];
        _batches.Remove(context);
        await PublishAsync(inserted).ConfigureAwait(false);
    }

    private async Task CommittedAsync(TransactionEndEventData eventData)
    {
        if (eventData.Context is not { } context || !_batches.TryGetValue(context, out Batch? batch) || batch.TransactionId != eventData.TransactionId)
        {
            return;
        }

        List<EventDelivery> parked = [.. batch.Parked];
        _batches.Remove(context);
        await PublishAsync(parked).ConfigureAwait(false);
    }

    private void RolledBack(TransactionEndEventData eventData)
    {
        if (eventData.Context is { } context && _batches.TryGetValue(context, out Batch? batch) && batch.TransactionId == eventData.TransactionId)
        {
            _batches.Remove(context);
        }
    }

    private async Task PublishAsync(List<EventDelivery> deliveries)
    {
        foreach (EventDelivery delivery in deliveries)
        {
            await channel.Writer.WriteAsync(delivery, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>The rows of one context: those of the save under way, and those parked for an open transaction.</summary>
    private sealed class Batch
    {
        public List<EventDelivery> Saving { get; } = [];

        public Guid? TransactionId { get; set; }

        public List<EventDelivery> Parked { get; } = [];
    }
}