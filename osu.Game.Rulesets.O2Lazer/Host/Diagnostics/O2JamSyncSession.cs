using System;
using System.Diagnostics;
using System.Threading;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.O2Lazer.Diagnostics;

// This observer never owns or adjusts a clock. Its gates use wall time so a pause, seek or
// slow audio thread cannot flood the log or accumulate an unbounded queue of observations.
internal sealed class O2JamSyncSession
{
    private static int nextId;
    public int Id { get; } = Interlocked.Increment(ref nextId);
    public Action<string> WriteLog { get; }

    public O2JamSyncSession(Action<string>? writeLog = null)
    {
        // Tests can capture the trace without scheduling writes through another fixture's host.
        WriteLog = writeLog ?? (message => Logger.Log(message, outputToListeners: false));
    }

    private int samplePending;
    private long? lastSample;
    private O2JamSyncState? previousState;
    private long? eventWindow;
    private int eventsInWindow;
    private int suppressedEvents;

    public bool TryBeginSample(long now, O2JamSyncState state)
    {
        var interval = state == previousState ? 1000 : 100;
        if (lastSample is { } last && Stopwatch.GetElapsedTime(last, now).TotalMilliseconds < interval)
            return false;
        if (Interlocked.CompareExchange(ref samplePending, 1, 0) != 0)
            return false;

        lastSample = now;
        previousState = state;
        return true;
    }

    public void CompleteSample() => Interlocked.Exchange(ref samplePending, 0);

    // Only the audio thread accesses the event gate. Gameplay sampling uses the separate gate above.
    public bool TryLogEvent(long now, out int suppressed)
    {
        suppressed = 0;
        if (eventWindow == null || Stopwatch.GetElapsedTime(eventWindow.Value, now).TotalMilliseconds >= 1000)
        {
            eventWindow = now;
            eventsInWindow = 0;
        }
        if (eventsInWindow >= 8)
        {
            suppressedEvents++;
            return false;
        }

        eventsInWindow++;
        suppressed = suppressedEvents;
        suppressedEvents = 0;
        return true;
    }
}

internal readonly record struct O2JamSyncState(bool Running, bool Paused, bool Rewinding, bool CatchingUp, bool Replay, double Rate);

internal readonly record struct O2JamSyncGameplaySample(
    long Timestamp, int Epoch, O2JamSyncState State, double JudgementTime, double? ParentTime,
    double VirtualTime, double? TotalOffset, double FrameElapsed, O2JamSyncHitSummary Hits);

internal readonly record struct O2JamSyncBackgroundSample(int SampleId, double EventTime, double Position, double Rate, bool Running)
{
    public double ChartTime => EventTime + Position;
    public double LeadOver(double virtualTime) => ChartTime - virtualTime;
}

internal readonly record struct O2JamSyncHitSummary(int Count, double? Mean, double? Minimum, double? Maximum);

internal sealed class O2JamSyncHitAccumulator
{
    private int count;
    private double sum;
    private double minimum = double.PositiveInfinity;
    private double maximum = double.NegativeInfinity;

    public void Add(double offset)
    {
        if (!double.IsFinite(offset))
            return;
        count++;
        sum += offset;
        minimum = Math.Min(minimum, offset);
        maximum = Math.Max(maximum, offset);
    }

    public O2JamSyncHitSummary Take()
    {
        var result = count == 0
            ? new O2JamSyncHitSummary(0, null, null, null)
            : new O2JamSyncHitSummary(count, sum / count, minimum, maximum);
        count = 0;
        sum = 0;
        minimum = double.PositiveInfinity;
        maximum = double.NegativeInfinity;
        return result;
    }
}
