using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.O2Jam;
using osu.Game.Rulesets.O2Lazer.Scoring.Judgements;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

public partial class O2LazerScoreProcessor() : ScoreProcessor(new O2LazerRuleset())
{
    private static readonly Action<JudgementResult, int> set_combo_after = createComboAfterSetter();
    private static readonly Action<JudgementResult, int> set_highest_combo_after = createHighestComboAfterSetter();

    private double latestEndTime = double.MaxValue;
    private readonly List<O2LazerJudgementEvent> judgementEvents = [];
    private readonly Dictionary<JudgementResult, O2LazerJudgementEvent> eventsByResult = new();
    private readonly List<TimingHitEventEntry> timingHitEventEntries = [];
    private readonly List<HitEvent> timingHitEvents = [];
    private readonly List<JudgementResult> o2JamResults = [];
    private readonly O2JamScoreState o2JamScore = new();
    private bool isO2Jam;

    public IReadOnlyList<O2LazerJudgementEvent> JudgementEvents => judgementEvents;

    public int ScoringJudgementEventCount { get; private set; }

    public event Action<O2LazerTimingObservation>? EmptyPoorRegistered;

    public override void ApplyBeatmap(IBeatmap beatmap)
    {
        isO2Jam = beatmap is Beatmaps.O2LazerBeatmap { LayoutVariant: Parsing.O2LazerLayoutVariant.O2Jam7K };
        base.ApplyBeatmap(beatmap);

        if (beatmap.HitObjects.Count == 0)
        {
            latestEndTime = 0;
            return;
        }

        // Latest possible judgement time = max object end time + worst-case slow window.
        var maxEndTime = beatmap.HitObjects.Max(h => h.GetEndTime());
        var slowWindow = beatmap.HitObjects[0].HitWindows?.WindowFor(HitResult.Ok)
                         ?? O2LazerHitWindows.FALLBACK_BAD_WINDOW;

        latestEndTime = maxEndTime + slowWindow;
    }

    public override int GetBaseScoreForResult(HitResult result) => result switch
    {
        HitResult.Perfect => isO2Jam ? 200 : 2,
        HitResult.Great => isO2Jam ? 200 : 1,
        HitResult.Good => isO2Jam ? 100 : 0,
        HitResult.Ok => isO2Jam ? 4 : 0,
        _ => 0,
    };

    public override ScoreRank RankFromScore(double accuracy, IReadOnlyDictionary<HitResult, int> results)
    {
        if (isO2Jam)
            return base.RankFromScore(accuracy, results);

        return accuracy switch
        {
            // All PGREATs → rainbow S (DJ LEVEL MAX / perfect full combo).
            >= 1.0 - 1e-9 when results.GetValueOrDefault(HitResult.Great) == 0 &&
                               results.GetValueOrDefault(HitResult.Good) == 0 &&
                               results.GetValueOrDefault(HitResult.Ok) == 0 &&
                               results.GetValueOrDefault(HitResult.Meh) == 0
                => ScoreRank.X,
            // Traditional O2LAZER DJ LEVEL thresholds expressed as EX-score ratios.
            // AAA = 8/9 of max ≈ 0.889, AA = 7/9 ≈ 0.778, A = 6/9 ≈ 0.667.
            // We expose S/A/B/C/D as approximate equivalents.
            _ => O2LazerExScore.RankFromAccuracy(accuracy, allowX: false),
        };

        // O2LAZER pass/fail is determined solely by gauge at song end, not by score accuracy.
        // ScoreRank.F is never assigned here; failure is communicated through O2LazerHealthProcessor.
    }

    public override double AccuracyCutoffFromRank(ScoreRank rank) =>
        isO2Jam ? base.AccuracyCutoffFromRank(rank) : O2LazerExScore.AccuracyCutoffFromRank(rank);

    /// <summary>
    ///     Records an Empty POOR: a keypress that found no note to consume.
    ///     Increments the Empty POOR counter stored under
    ///     <see cref="HitResult.Miss"/> in the score statistics so it
    ///     appears in the results-screen statistics and the live HUD judgement counter.
    ///     Empty POORs do not affect EX-score, accuracy, or combo.
    /// </summary>
    public void RegisterEmptyPoor()
    {
        RegisterEmptyPoor(Clock?.CurrentTime ?? 0);
    }

    public void RegisterEmptyPoor(double eventTime)
        => RegisterEmptyPoor(eventTime, eventTime, 0);

    public void RegisterEmptyPoor(double eventTime, double expectedTime, int column)
    {
        ScoreResultCounts[HitResult.Miss] = ScoreResultCounts.GetValueOrDefault(HitResult.Miss) + 1;

        var source = new O2LazerJudgementSource(eventTime, column, O2LazerJudgementSourceKind.EmptyPoor);
        var observation = new O2LazerTimingObservation(O2LazerTimingObservationKind.Note, expectedTime, eventTime, 1, HitResult.Miss);
        addJudgementEvent(new O2LazerJudgementEvent(source, HitResult.Miss, [observation]));
        EmptyPoorRegistered?.Invoke(observation);
    }

    /// <summary>
    ///     Applies a separate CN/HCN endpoint without requiring the source drawable to complete.
    /// </summary>
    public O2LazerLongNoteJudgementResult ApplySyntheticLongNoteEndpoint(O2LazerLongNoteEndpointResult endpointResult)
    {
        var endpoint = endpointResult.Source.CreateSyntheticEndpoint(endpointResult.ExpectedTime);
        var result = new O2LazerLongNoteJudgementResult(endpoint, endpoint.CreateJudgement(), [endpointResult])
        {
            Type = endpointResult.Result,
        };
        ApplyResult(result);
        return result;
    }

    public override void PopulateScore(ScoreInfo score)
    {
        base.PopulateScore(score);
        score.HitEvents = timingHitEvents;
        O2LazerJudgementEventStore.SetView(score, judgementEvents);

        // Attribution (e.g. which gauge an Auto Gauge run resolved to) is owned by the mods
        // that introduce the behaviour, so the score processor stays free of gauge-specific logic.
        foreach (var mod in Mods.Value.OfType<IApplicableToScorePopulation>())
            mod.ApplyToScore(score);

    }

    protected override void Update()
    {
        // Don't call base — JudgementProcessor.Update() checks JudgedHits == MaxHits,
        // which never becomes true when mines expire without a result.  Replace with a
        // time-based check: play is complete when the last object's slow window has passed.
        // This must also clear completion after a rewind because looping players wait for
        // that transition before they stop seeking back to the start of the beatmap.
        if (HasCompleted is BindableBool bb)
            bb.Value = Time.Current >= latestEndTime;
    }

    protected override void Reset(bool storeResults)
    {
        base.Reset(storeResults);
        judgementEvents.Clear();
        eventsByResult.Clear();
        timingHitEventEntries.Clear();
        timingHitEvents.Clear();
        ScoringJudgementEventCount = 0;
        o2JamResults.Clear();
        o2JamScore.Reset();
    }

    /// <summary>
    ///     Scaled EX-score.  Accuracy = EXScore / MaxEXScore (0–1).
    ///     Total score = accuracy × 1 000 000; bonus portion carries over from base.
    /// </summary>
    protected override double ComputeTotalScore(double comboProgress, double accuracyProgress, double bonusPortion)
        => isO2Jam ? o2JamScore.Score : 1_000_000 * Accuracy.Value * accuracyProgress;

    // EX-score has no combo multiplier — every PGREAT is always worth exactly 2.
    protected override double GetComboScoreChange(JudgementResult result) => 0;

    /// <summary>
    ///     O2LAZER BAD (Ok) and POOR (Meh) must break combo, but osu!'s framework considers them
    ///     "hit" results (<c>HitResult.IsHit()</c> = <c>true</c>) so <c>IncreasesCombo()</c>
    ///     fires instead of <c>BreaksCombo()</c> in the sealed <c>ApplyResultInternal</c>.
    ///     We force-reset both <c>Combo.Value</c> and the already-stamped
    ///     <c>ComboAfterJudgement</c> (via reflection) so that revert arithmetic stays correct.
    /// </summary>
    protected override void ApplyScoreChange(JudgementResult result)
    {
        if (isO2Jam)
        {
            o2JamResults.Add(result);
            o2JamScore.Apply(result.Type);

            // Keep the displayed combo aligned with consecutive COOL/GOOD hits. BAD/POOR reset
            // through o2JamScore, so the framework's temporary combo increment is overwritten here.
            Combo.Value = o2JamScore.Combo;
            HighestCombo.Value = Math.Max(o2JamScore.MaximumCombo, o2JamScore.Combo);

            set_combo_after(result, Combo.Value);
            set_highest_combo_after(result, HighestCombo.Value);
            return;
        }

        if (result.Type is HitResult.Ok or HitResult.Meh)
        {
            Combo.Value = 0;
            set_combo_after(result, 0);
        }
    }

    protected override void RemoveScoreChange(JudgementResult result)
    {
        base.RemoveScoreChange(result);

        if (isO2Jam)
        {
            o2JamResults.Remove(result);
            o2JamScore.Reset();
            foreach (var remaining in o2JamResults)
                o2JamScore.Apply(remaining.Type);
            Combo.Value = o2JamScore.Combo;
            HighestCombo.Value = o2JamScore.MaximumCombo;
        }

        if (eventsByResult.Remove(result, out var judgementEvent))
            removeJudgementEvent(judgementEvent);
    }

    protected override HitEvent CreateHitEvent(JudgementResult result)
    {
        var frameworkEvent = base.CreateHitEvent(result);
        var judgementEvent = createJudgementEvent(result);
        addJudgementEvent(judgementEvent);
        eventsByResult.Add(result, judgementEvent);

        return O2LazerJudgementEventProjection.CreateTimingHitEvent(
            judgementEvent.Source,
            judgementEvent.TimingObservations[^1],
            frameworkEvent.LastHitObject);
    }

    protected override IEnumerable<HitObject> EnumerateHitObjects(IBeatmap beatmap)
    {
        foreach (var hitObject in base.EnumerateHitObjects(beatmap).Order(JudgementOrderComparer.DEFAULT))
        {
            yield return hitObject;

            if (hitObject is O2LazerLongNote longNote
                && (longNote.Beatmap?.LayoutVariant == Parsing.O2LazerLayoutVariant.O2Jam7K
                    || longNote.Beatmap?.LockedLongNoteMode is O2LazerLongNoteMode.ChargeNote or O2LazerLongNoteMode.HellChargeNote))
                yield return longNote.CreateSyntheticEndpoint(longNote.EndTime);
        }
    }

    protected override HitResult GetSimulatedHitResult(Judgement judgement) => judgement is O2LazerJudgement { MaxResult: HitResult.Meh }
        ? HitResult.IgnoreMiss
        : base.GetSimulatedHitResult(judgement);

    private void addJudgementEvent(O2LazerJudgementEvent judgementEvent)
    {
        judgementEvents.Add(judgementEvent);
        addTimingHitEvents(judgementEvent);

        if (judgementEvent.Source.IsScoring)
            ScoringJudgementEventCount++;
    }

    private void removeJudgementEvent(O2LazerJudgementEvent judgementEvent)
    {
        judgementEvents.Remove(judgementEvent);
        removeTimingHitEvents(judgementEvent);

        if (judgementEvent.Source.IsScoring)
            ScoringJudgementEventCount--;
    }

    private void addTimingHitEvents(O2LazerJudgementEvent judgementEvent)
    {
        foreach (var observation in judgementEvent.TimingObservations)
        {
            var insertionIndex = findTimingInsertionIndex(observation.ActualTime);
            var hitObject = O2LazerJudgementEventProjection.CreateTimingHitEvent(judgementEvent.Source, observation, null).HitObject;

            timingHitEventEntries.Insert(insertionIndex, new TimingHitEventEntry(judgementEvent, observation, hitObject));
            timingHitEvents.Insert(insertionIndex, createTimingHitEvent(insertionIndex));
            repairNextTimingHitEvent(insertionIndex);
        }
    }

    private void removeTimingHitEvents(O2LazerJudgementEvent judgementEvent)
    {
        for (var i = timingHitEventEntries.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(timingHitEventEntries[i].JudgementEvent, judgementEvent))
                continue;

            timingHitEventEntries.RemoveAt(i);
            timingHitEvents.RemoveAt(i);
            repairNextTimingHitEvent(i - 1);
        }
    }

    private int findTimingInsertionIndex(double actualTime)
    {
        var low = 0;
        var high = timingHitEventEntries.Count;

        // Match OrderBy's stable ordering by placing equal-time observations after existing entries.
        while (low < high)
        {
            var middle = low + (high - low) / 2;

            if (timingHitEventEntries[middle].Observation.ActualTime <= actualTime)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private HitEvent createTimingHitEvent(int index)
    {
        var entry = timingHitEventEntries[index];
        return new HitEvent(
            entry.Observation.TimeOffset,
            entry.Observation.GameplayRate,
            entry.Observation.Result,
            entry.HitObject,
            index == 0 ? null : timingHitEvents[index - 1].HitObject,
            null);
    }

    private void repairNextTimingHitEvent(int index)
    {
        var nextIndex = index + 1;

        if (nextIndex < timingHitEvents.Count)
            timingHitEvents[nextIndex] = createTimingHitEvent(nextIndex);
    }

    private static Action<JudgementResult, int> createComboAfterSetter()
        => createResultIntSetter("<ComboAfterJudgement>k__BackingField", "ComboAfterJudgement");

    private static Action<JudgementResult, int> createHighestComboAfterSetter()
        => createResultIntSetter("<HighestComboAfterJudgement>k__BackingField", "HighestComboAfterJudgement");

    private static Action<JudgementResult, int> createResultIntSetter(string fieldName, string displayName)
    {
        try
        {
            var field = typeof(JudgementResult).GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (field == null)
            {
                O2LazerLogger.Log(
                    $"O2LAZER ScoreProcessor: Could not find JudgementResult.{displayName} backing field. "
                    + "The osu! framework may have changed; BAD/POOR combo-break revert will not function correctly.",
                    level: LogLevel.Error);
                return (_, _) => { };
            }

            return (r, v) => field.SetValue(r, v);
        }
        catch (Exception ex)
        {
            O2LazerLogger.Error(ex, $"O2LAZER ScoreProcessor: Failed to bind {displayName} setter via reflection. "
                             + "BAD/POOR combo-break revert will not function correctly.");
            return (_, _) => { };
        }
    }

    private static O2LazerJudgementEvent createJudgementEvent(JudgementResult result)
    {
        if (result is O2LazerLongNoteJudgementResult longNoteResult)
        {
            var source = longNoteResult.EndpointResults[0].Source;
            var observations = longNoteResult.EndpointResults.Select(endpoint => new O2LazerTimingObservation(
                endpoint.Kind == O2LazerLongNoteEndpointKind.Head
                    ? O2LazerTimingObservationKind.LongNoteHead
                    : O2LazerTimingObservationKind.LongNoteTail,
                endpoint.ExpectedTime,
                endpoint.EventTime,
                endpoint.GameplayRate,
                endpoint.Result));

            return new O2LazerJudgementEvent(
                O2LazerJudgementSource.From(longNoteResult.EndpointResults.Count > 1 ? source : result.HitObject),
                result.Type,
                observations);
        }

        var expectedTime = result.HitObject.GetEndTime();
        return new O2LazerJudgementEvent(O2LazerJudgementSource.From(result.HitObject), result.Type,
        [
            new O2LazerTimingObservation(
                O2LazerTimingObservationKind.Note,
                expectedTime,
                expectedTime + result.TimeOffset,
                result.GameplayRate,
                result.Type),
        ]);
    }

    private class JudgementOrderComparer : IComparer<HitObject>
    {
        public static readonly JudgementOrderComparer DEFAULT = new();

        public int Compare(HitObject? x, HitObject? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            var result = x.GetEndTime().CompareTo(y.GetEndTime());
            if (result != 0)
                return result;

            // Native O2LAZER should judge objects with identical end times in chart/lane order.
            if (x is O2LazerHitObject bx && y is O2LazerHitObject by)
                return bx.Column.CompareTo(by.Column);

            return 0;
        }
    }

    private readonly record struct TimingHitEventEntry(
        O2LazerJudgementEvent JudgementEvent,
        O2LazerTimingObservation Observation,
        HitObject HitObject);
}
