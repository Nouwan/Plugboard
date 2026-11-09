using NUnit.Framework;

namespace Plugboard.Events.Tests;

[TestFixture]
internal sealed class EventDeliveryTests
{
    private static readonly HostId Host1 = new("host-1");
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private static readonly EventDelivery Free = new() { EventId = Guid.NewGuid(), Subscription = "send-confirmation" };

    [Test]
    public void Reserve_ShouldNameTheHostDropTheNotBeforeAndCountTheAttempt() =>
        AssertStamped((Free with { NotBefore = Now }).Reserve(Host1), Free with { LockedBy = Host1, Attempts = 1, Version = 1 });

    [Test]
    public void Defer_ShouldKeepTheHostAndWait() =>
        AssertStamped(Free.Reserve(Host1).Defer(Now.AddMinutes(5)), Free with
        {
            LockedBy = Host1,
            Attempts = 1,
            NotBefore = Now.AddMinutes(5),
            Version = 2,
        });

    [Test]
    public void Free_ShouldDropTheHostAndKeepTheAttempts() =>
        AssertStamped(Free.Reserve(Host1).Defer(Now).Reserve(Host1).Free(), Free with { Attempts = 2, NotBefore = null, Version = 4 });

    [Test]
    public void Finished_ShouldCarryTheIdentityAndTheAttempts()
    {
        EventDelivery reserved = Free.Reserve(Host1);

        DeliveryHistory finished = reserved.Finished();

        Assert.That(finished.FinishedAt, Is.EqualTo(DateTimeOffset.UtcNow).Within(TimeSpan.FromSeconds(5)));
        Assert.That(finished, Is.EqualTo(new DeliveryHistory { Id = reserved.Id, EventId = reserved.EventId, Subscription = "send-confirmation", Attempts = 1, FinishedAt = finished.FinishedAt }));
    }

    [Test]
    public void DeadLettered_ShouldCarryTheIdentityTheAttemptsAndTheException()
    {
        EventDelivery reserved = Free.Reserve(Host1);

        DeadLetter deadLetter = reserved.DeadLettered(new InvalidOperationException("boom"));

        Assert.That(deadLetter.FailedAt, Is.EqualTo(DateTimeOffset.UtcNow).Within(TimeSpan.FromSeconds(5)));
        Assert.That(deadLetter, Is.EqualTo(new DeadLetter
        {
            Id = reserved.Id,
            EventId = reserved.EventId,
            Subscription = "send-confirmation",
            Attempts = 1,
            Error = "System.InvalidOperationException: boom",
            FailedAt = deadLetter.FailedAt,
        }));
    }

    /// <summary>The transition stamped the row with the current time and changed nothing else than expected.</summary>
    private static void AssertStamped(EventDelivery actual, EventDelivery expected)
    {
        Assert.That(actual.UpdatedAt, Is.EqualTo(DateTimeOffset.UtcNow).Within(TimeSpan.FromSeconds(5)));
        Assert.That(actual, Is.EqualTo(expected with { UpdatedAt = actual.UpdatedAt }));
    }
}
