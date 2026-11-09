using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Plugboard.Events;

/// <summary>Registers the event system on a context.</summary>
public static class PlugboardEventsServiceCollectionExtensions
{
    /// <summary>Adds the event system on the context that owns the event tables: its migrations create them and the processor runs on it.</summary>
    public static IServiceCollection AddPlugboardEvents<TContext>(this IServiceCollection services, Action<EventProcessorOptions>? configure = null)
        where TContext : DbContext, IEventStoreContext
    {
        services.AddOptions<EventProcessorOptions>()
            .Validate(static o => !string.IsNullOrWhiteSpace(o.Host), "Host must be set.")
            .Validate(static o => o.SweepInterval > TimeSpan.Zero, "SweepInterval must be positive.")
            .Validate(static o => o.HostTimeout > TimeSpan.Zero, "HostTimeout must be positive.")
            .Validate(static o => o.Concurrency > 0, "Concurrency must be positive.")
            .Validate(static o => o.ChannelCapacity > 0, "ChannelCapacity must be positive.")
            .Validate(static o => o.Backoff is not null, "Backoff must be set.")
            .ValidateOnStart();

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new EventStore(typeof(TContext)));
        services.TryAddSingleton(static sp => Channel.CreateBounded<EventDelivery>(new BoundedChannelOptions(sp.GetRequiredService<IOptions<EventProcessorOptions>>().Value.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
        }));
        // Started in this order and stopped in reverse: the host row exists before any row is reserved and outlives
        // the processor's release; the sweeper stops publishing before the processor stops reading.
        services.AddHostedService<EventHeartbeat<TContext>>();
        services.AddHostedService<EventProcessor<TContext>>();
        services.AddHostedService<EventSweeper<TContext>>();
        return services.AddEventContext<TContext>();
    }

    /// <summary>
    /// Hands the events <typeparamref name="TContext"/> saves to this host's workers once they are committed, and keeps
    /// the event tables out of its migrations unless it is the context given to <see cref="AddPlugboardEvents{TContext}"/>.
    /// Called by the generated registration for every <see cref="IEventContext"/> in a module; registering a context
    /// twice attaches it once.
    /// </summary>
    public static IServiceCollection AddEventContext<TContext>(this IServiceCollection services)
        where TContext : DbContext, IEventContext
    {
        services.TryAddSingleton<EventContextInterceptor>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDbContextOptionsConfiguration<TContext>, EventContextConfiguration<TContext>>());
        return services;
    }

    /// <summary>The context that owns the event tables.</summary>
    private sealed record EventStore(Type ContextType);

    /// <summary>Applied by AddDbContext, AddDbContextPool and AddDbContextFactory when they build the context's options.</summary>
    private sealed class EventContextConfiguration<TContext> : IDbContextOptionsConfiguration<TContext>
        where TContext : DbContext
    {
        public void Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UsePlugboardEvents(
                serviceProvider.GetRequiredService<EventContextInterceptor>(),
                owner: serviceProvider.GetService<EventStore>()?.ContextType == typeof(TContext));
    }
}
