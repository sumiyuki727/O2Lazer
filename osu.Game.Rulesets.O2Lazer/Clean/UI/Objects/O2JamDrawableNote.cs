using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania.Objects.Drawables;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Audio;
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

        var result = (O2JamJudgementResult)Result;
        var resolved = scoreProcessor is O2JamScoreProcessor processor
            ? processor.ResolveForApplication(result, judgement.Accuracy).ResolvedAccuracy
            : judgement.Accuracy;

        ApplyResult(O2JamResultMapper.ToFramework(resolved));
    }

    protected override JudgementResult CreateResult(Judgement judgement) => new O2JamJudgementResult(HitObject, judgement);
}
