using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Plugboard.Events;

/// <summary>Maps the deliveries waiting to be processed.</summary>
internal sealed class EventDeliveryConfiguration : IEntityTypeConfiguration<EventDelivery>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<EventDelivery> builder)
    {
        builder.ToTable(EventTables.DeliveryQueue);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Subscription).HasMaxLength(200);
        builder.Property(x => x.LockedBy).HasConversion(HostId.Converter).HasMaxLength(200);
        builder.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EventId, x.Subscription }).IsUnique();
        builder.HasIndex(x => new { x.LockedBy, x.NotBefore });
    }
}
