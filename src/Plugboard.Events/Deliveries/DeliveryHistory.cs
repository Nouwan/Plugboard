using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Plugboard.Events;

/// <summary>A successful handled delivery.</summary>
[EntityTypeConfiguration(typeof(DeliveryHistoryConfiguration))]
public sealed record DeliveryHistory
{
    private DeliveryHistory(Guid id, Guid eventId, string subscription, int attempts, DateTimeOffset finishedAt)
    {
        Id = id;
        EventId = eventId;
        Subscription = subscription;
        Attempts = attempts;
        FinishedAt = finishedAt;
    }

    public  Guid Id { get; }
    public  Guid EventId { get; }
    public  string Subscription { get; }
    public int Attempts { get;  }
    public DateTimeOffset FinishedAt { get; }
    public required EventEnvelope Event { get; init; }

    public static DeliveryHistory Create(DeliveryQueueItem item, DateTimeOffset now)
    {
        return new DeliveryHistory(item.Id, item.EventId, item.Subscription, item.Attempts, now)
        {
            Event = item.Event,
        };
    }
}

internal sealed class DeliveryHistoryConfiguration : IEntityTypeConfiguration<DeliveryHistory>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<DeliveryHistory> builder)
    {
        builder.ToTable(EventTables.DeliveryHistory);
        builder.HasKey(history => history.Id);
        builder.Property(history => history.Id).ValueGeneratedNever();
        builder.Property(history => history.Subscription).HasMaxLength(200);
        builder.HasOne(history => history.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(history => history.FinishedAt);
    }
}