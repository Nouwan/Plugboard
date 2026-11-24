using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Plugboard.Events;

/// <summary>
/// Queued delivery of an event to one specific subscriber
/// </summary>
[EntityTypeConfiguration(typeof(DeliveryQueueItemConfiguration))]
public sealed class DeliveryQueueItem
{
    private DeliveryQueueItem(Guid id, Guid eventId, int attempts, NodeId? lockedBy, DateTimeOffset? notBefore,
        DateTimeOffset updatedAt)
    {
        Id = id;
        EventId = eventId;
        Attempts = attempts;
        LockedBy = lockedBy;
        NotBefore = notBefore;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }
    public Guid EventId { get; }
    public required string Subscription { get; init; }
    public required EventEnvelope Event { get; init; }
    public int Attempts { get; private set; }
    public NodeId? LockedBy { get; private set; }
    public DateTimeOffset? NotBefore { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    internal static DeliveryQueueItem RetryDeadLetter(DeadLetter deadLetter) =>
        new(deadLetter.Id, deadLetter.EventId, attempts: 0, null, notBefore: null, DateTimeOffset.UtcNow)
        {
            Subscription = deadLetter.Subscription,
            Event = deadLetter.Event,
        };

    internal static DeliveryQueueItem Create(EventEnvelope envelope, string subscription, NodeId lockedBy, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), envelope.Id, attempts: 1, lockedBy, notBefore: null, now)
        {
            Subscription = subscription,
            Event = envelope,
        };

    internal void Retry(DateTimeOffset notBefore, DateTimeOffset now)
    {
        NotBefore = notBefore;
        UpdatedAt = now;
    }

    internal void Reserve(NodeId node, DateTimeOffset now)
    {
        LockedBy = node;
        NotBefore = null;
        Attempts++;
        UpdatedAt = now;
    }
}

internal sealed class DeliveryQueueItemConfiguration : IEntityTypeConfiguration<DeliveryQueueItem>
{
    public void Configure(EntityTypeBuilder<DeliveryQueueItem> builder)
    {
        builder.ToTable(EventTables.DeliveryQueue);
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.Subscription).HasMaxLength(200);
        builder.Property(item => item.LockedBy).HasConversion(NodeId.Converter).HasMaxLength(200).IsConcurrencyToken();
        builder.Property(item => item.Attempts).IsConcurrencyToken();
        builder.Property(item => item.UpdatedAt).IsRequired();
        builder.HasOne(item => item.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(item => new { item.EventId, item.Subscription }).IsUnique();
        builder.HasIndex(item => new { item.LockedBy, item.NotBefore });
    }
}