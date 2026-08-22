using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;

public class O2LazerLongNote : O2LazerHitObject, IHasDuration
{

    public double EndTime => StartTime + Duration;

    public double Duration { get; set; }

    /// <summary>
    ///     Precomputed scroll position at <see cref="EndTime"/>.
    ///     Computed once during beatmap loading.
    /// </summary>
    public double ScrollPositionAtEndTime { get; set; }

    /// <summary>
    ///     Creates a synthetic short-note endpoint for CN/HCN tail judgement
    ///     as a separate scoring event from the head judgement.
    /// </summary>
    public O2LazerHitObject CreateSyntheticEndpoint(double endpointTime)
    {
        var endpoint = new O2LazerNote();
        CopyTo(endpoint);

        endpoint.StartTime = endpointTime;

        return endpoint;
    }

    protected override void CopyTo(O2LazerHitObject target)
    {
        base.CopyTo(target);
        if (target is O2LazerLongNote ln)
        {
            ln.Duration = Duration;
            ln.ScrollPositionAtEndTime = ScrollPositionAtEndTime;
        }
    }
}

public enum O2LazerLongNoteMode
{
    Undefined = 0,
    LongNote = 1,
    ChargeNote = 2,
    HellChargeNote = 3,
}
