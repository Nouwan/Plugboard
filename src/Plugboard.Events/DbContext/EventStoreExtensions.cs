using Microsoft.EntityFrameworkCore;

namespace Plugboard.Events;

internal static class EventStoreExtensions
{
    private static readonly int[] BackoffInMinutes = [1, 2, 5, 10, 15, 30, 60];

    private static DateTimeOffset Backoff(int attempts)
    {
        int index = Math.Clamp(attempts - 1, 0, BackoffInMinutes.Length - 1);
        return DateTimeOffset.UtcNow.AddMinutes(BackoffInMinutes[index]);
    }

    extension<TContext>(TContext context) where TContext : DbContext, IEventStoreContext
    {
        /// <summary>
        /// Retries the provided dead letter item by putting it back in the delivery queue
        /// </summary>
        /// <param name="deadLetter">deadLetter entry to be retried</param>
        /// <param name="ct">Cancellation token</param>
        public async Task RetryDeadLetterAsync(DeadLetter deadLetter, CancellationToken ct)
        {
            context.DeadLetters.Remove(deadLetter);
            context.DeliveryQueue.Add(DeliveryQueueItem.RetryDeadLetter(deadLetter));
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        internal async Task FinishAsync(DeliveryQueueItem delivery, CancellationToken ct)
        {
            context.DeliveryQueue.Remove(delivery);
            context.DeliveryHistory.Add(DeliveryHistory.Create(delivery));
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        internal Task DeadLetterAsync(DeliveryQueueItem delivery, Exception exception, CancellationToken ct) =>
            context.DeadLetterAsync(delivery, $"{exception.GetType().FullName}: {exception.Message}", ct);

        internal async Task DeadLetterAsync(DeliveryQueueItem delivery, string error, CancellationToken ct)
        {
            context.DeliveryQueue.Remove(delivery);
            context.DeadLetters.Add(DeadLetter.Create(delivery, error));
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        internal async Task RetryAsync(DeliveryQueueItem delivery, CancellationToken ct)
        {
            DateTimeOffset notBefore = Backoff(delivery.Attempts);
            delivery.Retry(notBefore);
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        internal async Task ReserveAsync(DeliveryQueueItem delivery, InstanceId instance, CancellationToken ct)
        {
            delivery.Reserve(instance);
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}