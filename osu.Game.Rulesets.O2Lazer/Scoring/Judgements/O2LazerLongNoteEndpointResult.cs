using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Scoring.Judgements;

public enum O2LazerLongNoteEndpointKind
{
    Head,
    Tail,
}

public readonly record struct O2LazerLongNoteEndpointResult(
    O2LazerLongNote Source,
    O2LazerLongNoteEndpointKind Kind,
    double EventTime,
    double GameplayRate,
    HitResult Result)
{
    public double ExpectedTime => Kind == O2LazerLongNoteEndpointKind.Head ? Source.StartTime : Source.EndTime;

    public double TimeOffset => EventTime - ExpectedTime;
}
