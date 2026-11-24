using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Plugboard.Events;

/// <summary>A node running the processor</summary>
[EntityTypeConfiguration(typeof(DeliveryNodeConfiguration))]
public sealed class DeliveryNode
{
    public NodeId Id { get; internal init; }
    public DateTimeOffset LastSeen { get; internal init; }
}

/// <summary>The name of a node.</summary>
public readonly record struct NodeId
{
    private NodeId(string Value)
    {
        this.Value = Value;
    }

    public string Value { get; init; }
    internal static readonly ValueConverter<NodeId, string> Converter = new(static node => node.Value, static value => new NodeId(value));


    /// <inheritdoc/>
    public override string ToString() => Value;


    internal static readonly NodeId Current = new($"{Environment.MachineName}_{Guid.CreateVersion7():N}");
}

internal sealed class DeliveryNodeConfiguration : IEntityTypeConfiguration<DeliveryNode>
{
    public void Configure(EntityTypeBuilder<DeliveryNode> builder)
    {
        builder.ToTable(EventTables.Nodes);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(NodeId.Converter).HasMaxLength(200);
    }
}
