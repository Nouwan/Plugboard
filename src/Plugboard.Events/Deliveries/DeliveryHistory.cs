using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Plugboard.Events;

/// <summary>A delivery whose handler completed.</summary>
[EntityTypeConfiguration(typeof(DeliveryHistoryConfiguration))]
public sealed record DeliveryHistory
{
    /// <summary>The id the delivery had in the queue.</summary>
    public required Guid Id { get; init; }

    /// <summary>The stored event that was delivered.</summary>
    public required Guid EventId { get; init; }

    /// <summary>The subscription name.</summary>
    public required string Subscription { get; init; }

    /// <summary>The event that was delivered.</summary>
    public EventEnvelope Event { get; init; } = null!;

    /// <summary>How many times the delivery was handed to a worker.</summary>
    public int Attempts { get; init; }

    /// <summary>When the handler completed.</summary>
    public DateTimeOffset FinishedAt { get; init; }
}

/// <summary>Maps the finished deliveries.</summary>
internal sealed class DeliveryHistoryConfiguration : IEntityTypeConfiguration<DeliveryHistory>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<DeliveryHistory> builder)
    {
        builder.ToTable(EventTables.DeliveryHistory);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Subscription).HasMaxLength(200);
        builder.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.FinishedAt);
    }
}
