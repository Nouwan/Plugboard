using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Plugboard.Events;

/// <summary>A delivery that failed for the last time and waits for a person.</summary>
[EntityTypeConfiguration(typeof(DeadLetterConfiguration))]
public sealed record DeadLetter
{
    /// <summary>The id the delivery had in the queue.</summary>
    public required Guid Id { get; init; }

    /// <summary>The stored event that could not be delivered.</summary>
    public required Guid EventId { get; init; }

    /// <summary>The subscription name.</summary>
    public required string Subscription { get; init; }

    /// <summary>The event that could not be delivered.</summary>
    public EventEnvelope Event { get; init; } = null!;

    /// <summary>How many times the delivery was handed to a worker.</summary>
    public int Attempts { get; init; }

    /// <summary>Type and message of the exception that failed the last attempt.</summary>
    public required string Error { get; init; }

    /// <summary>When the delivery was given up on.</summary>
    public DateTimeOffset FailedAt { get; init; }
}

/// <summary>Maps the dead letters.</summary>
internal sealed class DeadLetterConfiguration : IEntityTypeConfiguration<DeadLetter>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<DeadLetter> builder)
    {
        builder.ToTable(EventTables.DeliveryDeadLetters);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Subscription).HasMaxLength(200);
        builder.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}
