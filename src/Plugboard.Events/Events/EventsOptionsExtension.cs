using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Plugboard.Events;

/// <summary>Attaches the event system to a context's options.</summary>
internal static class EventsOptionsBuilderExtensions
{
    /// <summary>
    /// Hands the events the context saves to <paramref name="interceptor"/> and, unless the context owns the event
    /// tables, keeps them out of its migrations.
    /// </summary>
    public static TBuilder UsePlugboardEvents<TBuilder>(this TBuilder builder, EventContextInterceptor interceptor, bool owner)
        where TBuilder : DbContextOptionsBuilder
    {
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new EventsOptionsExtension(owner));
        builder.AddInterceptors(interceptor);
        return builder;
    }
}

/// <summary>Whether the context owns the event tables; every other context maps them without migrating them.</summary>
internal sealed class EventsOptionsExtension(bool owner) : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public bool Owner { get; } = owner;

    /// <inheritdoc/>
    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <inheritdoc/>
    public void ApplyServices(IServiceCollection services)
    {
        if (!Owner)
        {
            services.AddScoped<IConventionSetPlugin, ExcludeEventTablesFromMigrations>();
        }
    }

    /// <inheritdoc/>
    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(EventsOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        private new EventsOptionsExtension Extension => (EventsOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => Extension.Owner ? "PlugboardEvents(owner) " : "PlugboardEvents ";

        public override int GetServiceProviderHashCode() => Extension.Owner.GetHashCode();

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo info && info.Extension.Owner == Extension.Owner;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["PlugboardEvents:Owner"] = Extension.Owner.ToString();
    }

    /// <summary>Marks the event tables excluded from migrations once the model is complete.</summary>
    private sealed class ExcludeEventTablesFromMigrations : IConventionSetPlugin, IModelFinalizingConvention
    {
        private static readonly Type[] EventTypes = [typeof(EventEnvelope), typeof(EventDelivery), typeof(DeliveryHistory), typeof(DeadLetter), typeof(EventHost)];

        public ConventionSet ModifyConventions(ConventionSet conventionSet)
        {
            conventionSet.ModelFinalizingConventions.Add(this);
            return conventionSet;
        }

        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (IConventionEntityType entityType in modelBuilder.Metadata.GetEntityTypes())
            {
                if (EventTypes.Contains(entityType.ClrType))
                {
                    entityType.Builder.ExcludeTableFromMigrations(true);
                }
            }
        }
    }
}
