using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Plugboard.Events;

/// <summary>An event delivery that failed delivery and/or automatic retry</summary>
[EntityTypeConfiguration(typeof(DeadLetterConfiguration))]
public sealed class DeadLetter
{
    private DeadLetter(Guid id, Guid eventId, string subscription, int attempts, string error, DateTimeOffset failedAt)
    {
        Id = id;
        EventId = eventId;
        Subscription = subscription;
        Attempts = attempts;
        Error = error;
        FailedAt = failedAt;
    }

    public Guid Id { get; }
    public Guid EventId { get; }
    public string Subscription { get; }
    public required EventEnvelope Event { get; init; }
    public int Attempts { get; }
    public string Error { get; }
    public DateTimeOffset FailedAt { get; }

    internal static DeadLetter Create(DeliveryQueueItem item, string error, DateTimeOffset now)
    {
        return new DeadLetter(item.Id, item.EventId, item.Subscription, item.Attempts, error, now)
        {
            Event = item.Event,
        };
    }
}

internal sealed class DeadLetterConfiguration : IEntityTypeConfiguration<DeadLetter>
{
    public void Configure(EntityTypeBuilder<DeadLetter> builder)
    {
        builder.ToTable(EventTables.DeliveryDeadLetters);
        builder.HasKey(deadLetter => deadLetter.Id);
        builder.Property(deadLetter => deadLetter.Id).ValueGeneratedNever();
        builder.Property(deadLetter => deadLetter.Subscription).HasMaxLength(200);
        builder.HasOne(deadLetter => deadLetter.Event).WithMany().HasForeignKey(x => x.EventId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}