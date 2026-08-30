using System.Diagnostics;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Diagnostics;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamSyncSessionTest
{
    private static readonly O2JamSyncState playing = new(true, false, false, false, false, 1);

    [Test]
    public void SteadyStateIsSampledOncePerSecond()
    {
        var session = new O2JamSyncSession();
        Assert.That(session.TryBeginSample(ticks(0), playing), Is.True);
        session.CompleteSample();
        Assert.That(session.TryBeginSample(ticks(999), playing), Is.False);
        Assert.That(session.TryBeginSample(ticks(1001), playing), Is.True);
    }

    [Test]
    public void AudioThreadBacklogDoesNotAccumulateSamples()
    {
        var session = new O2JamSyncSession();
        Assert.That(session.TryBeginSample(ticks(0), playing), Is.True);
        Assert.That(session.TryBeginSample(ticks(5000), playing), Is.False);
        Assert.That(session.TryBeginSample(ticks(10000), playing with { Paused = true }), Is.False);
        session.CompleteSample();
        Assert.That(session.TryBeginSample(ticks(10001), playing with { Paused = true }), Is.True);
    }

    [Test]
    public void StateChangesArePromptButAlsoBounded()
    {
        var session = new O2JamSyncSession();
        session.TryBeginSample(ticks(0), playing);
        session.CompleteSample();
        var paused = playing with { Running = false, Paused = true };
        Assert.That(session.TryBeginSample(ticks(50), paused), Is.False);
        Assert.That(session.TryBeginSample(ticks(101), paused), Is.True);
        session.CompleteSample();
        Assert.That(session.TryBeginSample(ticks(151), playing), Is.False);
        Assert.That(session.TryBeginSample(ticks(202), playing), Is.True);
    }

    [Test]
    public void LifecycleLogBurstReportsSuppressedEvents()
    {
        var session = new O2JamSyncSession();
        for (var index = 0; index < 8; index++)
            Assert.That(session.TryLogEvent(ticks(index), out _), Is.True);
        Assert.That(session.TryLogEvent(ticks(10), out _), Is.False);
        Assert.That(session.TryLogEvent(ticks(20), out _), Is.False);
        Assert.That(session.TryLogEvent(ticks(1001), out var suppressed), Is.True);
        Assert.That(suppressed, Is.EqualTo(2));
        Assert.That(session.TryLogEvent(ticks(1002), out suppressed), Is.True);
        Assert.That(suppressed, Is.Zero);
    }

    [TestCase(1)]
    [TestCase(1.5)]
    [TestCase(0.75)]
    public void BackgroundPositionsRemainInChartTimeWithoutMultiplyingRateAgain(double rate)
    {
        const double origin = 1278.409091;
        var sample = new O2JamSyncBackgroundSample(0, origin, 2000 - origin + 40, rate, true);
        Assert.That(sample.ChartTime, Is.EqualTo(2040).Within(0.000001));
        Assert.That(sample.LeadOver(2000), Is.EqualTo(40).Within(0.000001));
    }

    [Test]
    public void HitSummaryPreservesEarlySignAndResetsBetweenSamples()
    {
        var hits = new O2JamSyncHitAccumulator();
        hits.Add(-50);
        hits.Add(-20);
        hits.Add(10);
        hits.Add(double.NaN);
        hits.Add(double.PositiveInfinity);
        Assert.That(hits.Take(), Is.EqualTo(new O2JamSyncHitSummary(3, -20, -50, 10)));
        Assert.That(hits.Take(), Is.EqualTo(new O2JamSyncHitSummary(0, null, null, null)));
        hits.Add(30);
        Assert.That(hits.Take(), Is.EqualTo(new O2JamSyncHitSummary(1, 30, 30, 30)));
    }

    private static long ticks(double milliseconds) => (long)(milliseconds * Stopwatch.Frequency / 1000);
}
