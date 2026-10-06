namespace O2Jam.Core;

/// <summary>
/// Endpoint accuracies are scored values, while the original head accuracy controls continuation.
/// Immutable snapshots allow rewind without replaying presentation callbacks. Hosts with no pill
/// conversion may omit the original accuracy because it is identical to the scored value.
/// </summary>
public readonly record struct O2JamHoldState(
    O2JamAccuracy HeadAccuracy,
    O2JamAccuracy TailAccuracy,
    bool IsHolding,
    O2JamAccuracy? RequestedHeadAccuracy = null)
{
    public bool HeadResolved => HeadAccuracy != O2JamAccuracy.None;
    public bool IsComplete => TailAccuracy != O2JamAccuracy.None;
    public O2JamHoldHeadOutcome HeadOutcome => O2JamHoldRules.ResolveHead(RequestedHeadAccuracy ?? HeadAccuracy);
    public bool RequiresTailMiss => !IsComplete && HeadOutcome == O2JamHoldHeadOutcome.EndWithMiss;
    public bool CanBeginHold => !IsComplete && HeadOutcome == O2JamHoldHeadOutcome.BeginHold;
    public bool CanRelease => !IsComplete && IsHolding;

    public O2JamHoldState ResolveHead(O2JamAccuracy accuracy) =>
        HeadResolved || accuracy == O2JamAccuracy.None
            ? this : this with { HeadAccuracy = accuracy, RequestedHeadAccuracy = accuracy };

    public O2JamHoldState ResolveHead(O2JamResolvedJudgement resolution) =>
        HeadResolved || resolution.ResolvedAccuracy == O2JamAccuracy.None ? this : this with
        {
            HeadAccuracy = resolution.ResolvedAccuracy,
            RequestedHeadAccuracy = resolution.RequestedAccuracy,
        };

    public O2JamHoldState BeginHold() => CanBeginHold ? this with { IsHolding = true } : this;

    public O2JamHoldState ResolveTail(O2JamAccuracy accuracy) =>
        IsComplete || accuracy == O2JamAccuracy.None
            ? this
            : this with { TailAccuracy = accuracy, IsHolding = false };
}

/// <summary>
/// Owns hold endpoint decisions independently of input delivery and native result submission.
/// Returning None means there is no result to commit; inspecting an open window is not a hit.
/// </summary>
public sealed class O2JamHoldJudgementEngine(IO2JamPositionClock clock)
{
    private readonly O2JamJudgementEngine judgement = new(clock);

    public O2JamAccuracy InspectHead(O2JamHoldState state, double position, double time, bool pressed)
    {
        if (state.HeadResolved || state.IsComplete)
            return O2JamAccuracy.None;

        return evaluate(position, time, O2JamEndpointKind.HoldHead, pressed);
    }

    public O2JamAccuracy InspectTail(O2JamHoldState state, double position, double time,
                                    bool released, bool releaseTimingDisabled)
    {
        if (state.IsComplete)
            return O2JamAccuracy.None;
        if (state.RequiresTailMiss)
            return O2JamAccuracy.Miss;

        // Disabling release timing only rewards a hold which reaches the charted end.
        // Early release still uses the normal release window.
        if (releaseTimingDisabled && state.IsHolding && clock.PositionAt(time) >= position)
            return O2JamAccuracy.Cool;

        if (released && !state.CanRelease)
            return O2JamAccuracy.None;

        return evaluate(position, time, O2JamEndpointKind.HoldRelease, released);
    }

    private O2JamAccuracy evaluate(double position, double time, O2JamEndpointKind endpoint, bool attempted)
    {
        var result = attempted ? judgement.Judge(position, time, endpoint) : judgement.Inspect(position, time, endpoint);
        return attempted || result.Accuracy == O2JamAccuracy.Miss ? result.Accuracy : O2JamAccuracy.None;
    }
}
