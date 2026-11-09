using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Plugboard.Events;

/// <summary>A host running the processor and its last heartbeat.</summary>
[EntityTypeConfiguration(typeof(EventHostConfiguration))]
public sealed record EventHost(HostId Id, DateTimeOffset LastSeen);

/// <summary>The name of a host.</summary>
public readonly record struct HostId(string Value)
{
    /// <summary>Stores the name as a string.</summary>
    internal static readonly ValueConverter<HostId, string> Converter = new(static host => host.Value, static value => new HostId(value));

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>Maps the hosts and their heartbeats.</summary>
internal sealed class EventHostConfiguration : IEntityTypeConfiguration<EventHost>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<EventHost> builder)
    {
        builder.ToTable(EventTables.Hosts);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(HostId.Converter).HasMaxLength(200);
    }
}
