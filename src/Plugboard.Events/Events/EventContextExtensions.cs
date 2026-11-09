using Microsoft.EntityFrameworkCore;

namespace Plugboard.Events;

/// <summary>Publishing through a context.</summary>
public static class EventContextExtensions
{
    /// <summary>
    /// Stores <paramref name="integrationEvent"/> with the next save of <paramref name="context"/> and delivers it to
    /// its subscriptions. For code with no entity of its own to raise on.
    /// </summary>
    public static void Publish<TContext, TEvent>(this TContext context, TEvent integrationEvent)
        where TContext : DbContext, IEventContext =>
        context.Events.Add(EventEnvelope.Create(integrationEvent));
}
