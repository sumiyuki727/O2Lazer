using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania.Objects.Drawables;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.UI.Objects;

public partial class O2JamDrawableHoldHead : DrawableHoldNoteHead
{
    [Resolved(canBeNull: true)]
    private ScoreProcessor? scoreProcessor { get; set; }

    public new O2JamHoldHead HitObject => (O2JamHoldHead)base.HitObject;

    public O2JamDrawableHoldHead()
    {
    }

    public O2JamDrawableHoldHead(O2JamHoldHead hitObject)
        : base(hitObject)
    {
    }

    [BackgroundDependencyLoader(true)]
    private void load(O2JamHitSoundRateAdjustments? rateAdjustments) => rateAdjustments?.Bind(Samples);

    protected override float SamplePlaybackPosition => HitObject.Samples.OfType<O2JamHitSampleInfo>().FirstOrDefault() is { } sample
        ? Math.Clamp((sample.Pan + 1) / 2, 0, 1)
        : base.SamplePlaybackPosition;

    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
        var judgement = HitObject.Judge(Time.Current, userTriggered);

        if (!userTriggered && judgement.Accuracy != O2JamAccuracy.Miss)
            return;
        if (judgement.Accuracy == O2JamAccuracy.None)
            return;

        apply(judgement.Accuracy);
    }

    protected override JudgementResult CreateResult(Judgement judgement) => new O2JamJudgementResult(HitObject, judgement);

    private void apply(O2JamAccuracy accuracy)
    {
        var result = (O2JamJudgementResult)Result;
        var resolved = scoreProcessor is O2JamScoreProcessor processor
            ? processor.ResolveForApplication(result, accuracy).ResolvedAccuracy
            : accuracy;

        ApplyResult(O2JamResultMapper.ToFramework(resolved));
    }
}

public partial class O2JamDrawableHoldTail : DrawableHoldNoteTail
{
    [Resolved(canBeNull: true)]
    private ScoreProcessor? scoreProcessor { get; set; }

    public new O2JamHoldTail HitObject => (O2JamHoldTail)base.HitObject;

    public O2JamDrawableHoldTail()
    {
    }

    public O2JamDrawableHoldTail(O2JamHoldTail hitObject)
        : base(hitObject)
    {
    }

    [BackgroundDependencyLoader(true)]
    private void load(O2JamHitSoundRateAdjustments? rateAdjustments) => rateAdjustments?.Bind(Samples);

    protected override float SamplePlaybackPosition => HitObject.Samples.OfType<O2JamHitSampleInfo>().FirstOrDefault() is { } sample
        ? Math.Clamp((sample.Pan + 1) / 2, 0, 1)
        : base.SamplePlaybackPosition;

    public void ResolveForcedMiss()
    {
        if (!AllJudged)
            apply(O2JamAccuracy.Miss);
    }

    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
        if (HitObject.ReleaseTimingDisabled && HoldNote.IsHolding.Value && timeOffset >= 0)
        {
            apply(O2JamAccuracy.Cool);
            return;
        }

        var judgement = HitObject.Judge(Time.Current, userTriggered);

        if (!userTriggered && judgement.Accuracy != O2JamAccuracy.Miss)
            return;
        if (judgement.Accuracy == O2JamAccuracy.None)
            return;

        apply(judgement.Accuracy);
    }

    protected override JudgementResult CreateResult(Judgement judgement) => new O2JamJudgementResult(HitObject, judgement);

    protected override void UpdateHitStateTransforms(ArmedState state)
    {
        if (state != ArmedState.Miss && !(state == ArmedState.Hit && O2JamRuntimeOptions.UseO2JamLongNoteMissVisual))
        {
            base.UpdateHitStateTransforms(state);
            return;
        }

        // Retaining only the parent would still let mania hide the tail sprite on an early hit.
        // Forced misses at the head likewise must not remove a tail still far above the line.
        LifetimeEnd = HitObject.StartTime + 150;
    }

    private void apply(O2JamAccuracy accuracy)
    {
        var result = (O2JamJudgementResult)Result;
        var resolved = scoreProcessor is O2JamScoreProcessor processor
            ? processor.ResolveForApplication(result, accuracy).ResolvedAccuracy
            : accuracy;

        ApplyResult(O2JamResultMapper.ToFramework(resolved));
    }
}

public partial class O2JamDrawableHoldBody : DrawableHoldNoteBody
{
    public O2JamDrawableHoldBody()
    {
    }

    public O2JamDrawableHoldBody(O2JamHoldBody hitObject)
        : base(hitObject)
    {
    }

    public void Resolve(bool held)
    {
        if (AllJudged)
            return;

        if (held)
            ApplyMaxResult();
        else
            ApplyMinResult();
    }
}
