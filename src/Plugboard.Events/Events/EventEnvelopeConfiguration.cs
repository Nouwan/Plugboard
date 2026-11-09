using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Plugboard.Events;

/// <summary>Maps the stored events.</summary>
internal sealed class EventEnvelopeConfiguration : IEntityTypeConfiguration<EventEnvelope>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<EventEnvelope> builder)
    {
        builder.ToTable(EventTables.Events);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Payload); // Read-only properties are not mapped by convention.
        builder.HasIndex(x => x.OccurredAt);
    }
}
