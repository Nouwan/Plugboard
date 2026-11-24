namespace Plugboard.Events;

internal static class Jitter
{
    internal static TimeSpan Jittered(TimeSpan interval)
    {
        long shortest = (long)Math.Floor(interval.TotalMilliseconds * 0.8);
        long longest = (long)Math.Ceiling(interval.TotalMilliseconds * 1.2);

        var delay = Random.Shared.NextInt64(shortest, longest);
        return TimeSpan.FromMilliseconds(delay);
    }
}
