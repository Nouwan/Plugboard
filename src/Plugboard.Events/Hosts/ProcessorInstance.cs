using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Plugboard.Events;

/// <summary>A host running the processor</summary>
[EntityTypeConfiguration(typeof(EventHostConfiguration))]
public sealed class ProcessorInstance
{
    public InstanceId Id { get; internal init; }
    public DateTimeOffset LastSeen { get; internal init; }
}

/// <summary>The name of a host.</summary>
public readonly record struct InstanceId
{
    private InstanceId(string Value)
    {
        this.Value = Value;
    }

    public string Value { get; init; }
    internal static readonly ValueConverter<InstanceId, string> Converter = new(static host => host.Value, static value => new InstanceId(value));


    /// <inheritdoc/>
    public override string ToString() => Value;


    internal static readonly InstanceId Current = new($"{Environment.MachineName}_{Guid.CreateVersion7():N}");
}

internal sealed class EventHostConfiguration : IEntityTypeConfiguration<ProcessorInstance>
{
    public void Configure(EntityTypeBuilder<ProcessorInstance> builder)
    {
        builder.ToTable(EventTables.Hosts);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(InstanceId.Converter).HasMaxLength(200);
    }
}
