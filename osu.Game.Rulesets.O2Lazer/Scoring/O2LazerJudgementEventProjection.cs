using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

public static class O2LazerJudgementEventProjection
{
    public static List<HitEvent> CreateScoringHitEvents(IEnumerable<O2LazerJudgementEvent> judgementEvents)
    {
        var events = judgementEvents.ToArray();

        var hitEvents = new List<HitEvent>(events.Length);
        HitObject? lastHitObject = null;

        foreach (var judgementEvent in events)
        {
            var observation = judgementEvent.TimingObservations[^1];
            var hitObject = createScoringHitObject(judgementEvent.Source, observation);

            // E-POOR stays anchored to the press for timeline ordering, while its timing error is
            // measured against the upcoming note that caused the non-consuming miss row.
            var hitEvent = new HitEvent(
                judgementEvent.Source.Kind == O2LazerJudgementSourceKind.EmptyPoor
                    ? observation.TimeOffset
                    : observation.ActualTime - hitObject.GetEndTime(),
                observation.GameplayRate,
                judgementEvent.Result,
                hitObject,
                lastHitObject,
                null);

            hitEvents.Add(hitEvent);
            lastHitObject = hitEvent.HitObject;
        }

        return hitEvents;
    }

    public static List<HitEvent> CreateTimingHitEvents(IEnumerable<O2LazerJudgementEvent> judgementEvents)
    {
        var observations = judgementEvents
            .SelectMany(judgementEvent => judgementEvent.TimingObservations.Select(observation => (judgementEvent.Source, Observation: observation)))
            .OrderBy(item => item.Observation.ActualTime)
            .ToArray();

        var hitEvents = new List<HitEvent>(observations.Length);
        HitObject? lastHitObject = null;

        foreach (var (source, observation) in observations)
        {
            var hitEvent = CreateTimingHitEvent(source, observation, lastHitObject);
            hitEvents.Add(hitEvent);
            lastHitObject = hitEvent.HitObject;
        }

        return hitEvents;
    }

    internal static HitEvent CreateTimingHitEvent(O2LazerJudgementSource source, O2LazerTimingObservation observation, HitObject? lastHitObject)
        => new(
            observation.TimeOffset,
            observation.GameplayRate,
            observation.Result,
            createObservationHitObject(source, observation),
            lastHitObject,
            null
        );

    private static HitObject createScoringHitObject(O2LazerJudgementSource source, O2LazerTimingObservation observation)
    {
        if (source.Kind == O2LazerJudgementSourceKind.LongNote
            && observation.Kind == O2LazerTimingObservationKind.LongNoteHead)
            return createNote(source, observation.ExpectedTime);

        return createHitObject(source);
    }

    private static HitObject createObservationHitObject(O2LazerJudgementSource source, O2LazerTimingObservation observation)
    {
        if (source.Kind == O2LazerJudgementSourceKind.LongNote
            && observation.Kind is O2LazerTimingObservationKind.LongNoteHead or O2LazerTimingObservationKind.LongNoteTail)
            return createNote(source, observation.ExpectedTime);

        return createHitObject(source);
    }

    private static HitObject createHitObject(O2LazerJudgementSource source)
    {
        HitObject hitObject = source.Kind switch
        {
            O2LazerJudgementSourceKind.LongNote => new O2LazerLongNote { Duration = source.Duration },
            O2LazerJudgementSourceKind.Note => new O2LazerNote(),
            _ => new HitObject(),
        };

        hitObject.StartTime = source.StartTime;

        if (hitObject is O2LazerHitObject o2lazer)
            o2lazer.Column = source.Column;

        return hitObject;
    }

    private static O2LazerNote createNote(O2LazerJudgementSource source, double startTime) => new()
    {
        StartTime = startTime,
        Column = source.Column,
    };
}
