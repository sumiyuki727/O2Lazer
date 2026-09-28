using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania.Objects.Drawables;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.UI.Objects;

public partial class O2JamDrawableNote : DrawableNote
{
    [Resolved(canBeNull: true)]
    private ScoreProcessor? scoreProcessor { get; set; }

    public new O2JamNote HitObject => (O2JamNote)base.HitObject;

    public O2JamDrawableNote()
    {
    }

    public O2JamDrawableNote(O2JamNote hitObject)
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
        var result = O2JamJudgementBridge.CheckNote(HitObject, (O2JamJudgementResult)Result,
                                                   scoreProcessor, Time.Current, userTriggered);
        if (result != HitResult.None)
            ApplyResult(result);
    }

    protected override JudgementResult CreateResult(Judgement judgement) => new O2JamJudgementResult(HitObject, judgement);
}
