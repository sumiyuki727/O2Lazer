using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

public enum O2LazerTimingObservationKind
{
    Note,
    LongNoteHead,
    LongNoteTail,
}

public readonly record struct O2LazerTimingObservation(
    O2LazerTimingObservationKind Kind,
    double ExpectedTime,
    double ActualTime,
    double? GameplayRate,
    HitResult Result)
{
    public double TimeOffset => ActualTime - ExpectedTime;
}

public enum O2LazerJudgementSourceKind
{
    Note,
    LongNote,
    EmptyPoor,
}

public readonly record struct O2LazerJudgementSource(
    double StartTime,
    int Column,
    O2LazerJudgementSourceKind Kind,
    double Duration = 0)
{
    public double EndTime => StartTime + Duration;

    public bool IsScoring => Kind is O2LazerJudgementSourceKind.Note or O2LazerJudgementSourceKind.LongNote;

    public static O2LazerJudgementSource From(HitObject hitObject)
    {
        ArgumentNullException.ThrowIfNull(hitObject);

        return new O2LazerJudgementSource(
            hitObject.StartTime,
            hitObject is O2LazerHitObject o2lazer ? o2lazer.Column : 0,
            hitObject switch
            {
                O2LazerLongNote => O2LazerJudgementSourceKind.LongNote,
                O2LazerHitObject => O2LazerJudgementSourceKind.Note,
                _ => O2LazerJudgementSourceKind.EmptyPoor,
            },
            hitObject is O2LazerLongNote longNote ? longNote.Duration : 0);
    }
}

public sealed record O2LazerJudgementEvent
{
    public O2LazerJudgementSource Source { get; }

    public HitResult Result { get; }

    public IReadOnlyList<O2LazerTimingObservation> TimingObservations { get; }

    public O2LazerJudgementEvent(
        O2LazerJudgementSource source,
        HitResult result,
        IEnumerable<O2LazerTimingObservation> timingObservations)
    {
        ArgumentNullException.ThrowIfNull(timingObservations);

        var observations = timingObservations.ToArray();
        if (observations.Length == 0)
            throw new ArgumentException(@"At least one timing observation is required.", nameof(timingObservations));

        Source = source;
        Result = result;
        TimingObservations = Array.AsReadOnly(observations);
    }
}
