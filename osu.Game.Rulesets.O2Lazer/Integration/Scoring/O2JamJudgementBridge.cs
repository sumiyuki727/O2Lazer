using O2Jam.Core;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

/// <summary>
/// Translates native callbacks into domain decisions, then prepares the native result.
/// DrawableHitObject must still commit that result through its own ApplyResult lifecycle.
/// </summary>
internal static class O2JamJudgementBridge
{
    public static HitResult CheckNote(O2JamNote note, O2JamJudgementResult result,
                                     ScoreProcessor? processor, double sourceTime, bool userTriggered)
    {
        var judgement = note.Judge(sourceTime, userTriggered);
        var accuracy = userTriggered || judgement.Accuracy == O2JamAccuracy.Miss
            ? judgement.Accuracy : O2JamAccuracy.None;
        return Prepare(result, processor, accuracy);
    }

    public static HitResult CheckHead(O2JamHoldHead head, O2JamHoldState state,
                                     O2JamJudgementResult result, ScoreProcessor? processor,
                                     double sourceTime, bool userTriggered)
    {
        var engine = new O2JamHoldJudgementEngine(new O2JamPositionClock(head.TimingMap));
        return Prepare(result, processor, engine.InspectHead(state, head.ChartPosition, sourceTime, userTriggered));
    }

    public static HitResult CheckTail(O2JamHoldTail tail, O2JamHoldState state,
                                     O2JamJudgementResult result, ScoreProcessor? processor,
                                     double sourceTime, bool userTriggered)
    {
        var engine = new O2JamHoldJudgementEngine(new O2JamPositionClock(tail.TimingMap));
        return Prepare(result, processor, engine.InspectTail(state, tail.ChartPosition, sourceTime,
                                                            userTriggered, tail.ReleaseTimingDisabled));
    }

    public static HitResult Prepare(O2JamJudgementResult result, ScoreProcessor? processor, O2JamAccuracy accuracy)
    {
        if (accuracy == O2JamAccuracy.None || result.HasResult)
            return HitResult.None;

        // Resolve pills before native health, combo, samples and judgement display see the result.
        // Do not set Type here: that would bypass DrawableHitObject's legal None -> result transition.
        var resolved = processor is IO2JamJudgementResolver resolver
            ? resolver.ResolveAccuracyForApplication(result, accuracy)
            : accuracy;
        return O2JamResultMapper.ToFramework(resolved);
    }

    // Native ignored parent/body results follow the framework hit classification, where BAD
    // maps to Ok. This does not imply an O2Jam combo success or change the endpoint accuracy.
    public static bool IsTailHit(O2JamHoldState state) =>
        O2JamResultMapper.ToFramework(state.TailAccuracy) is HitResult.Perfect or HitResult.Good or HitResult.Ok;

    public static O2JamHoldState ReadHoldState(JudgementResult? head, JudgementResult? tail, bool isHolding) =>
        new(readAccuracy(head), readAccuracy(tail), isHolding);

    private static O2JamAccuracy readAccuracy(JudgementResult? result) =>
        result is O2JamJudgementResult { ResolutionApplied: true } resolved
            ? resolved.Resolution.ResolvedAccuracy
            : O2JamResultMapper.FromFramework(result?.Type ?? HitResult.None);
}
