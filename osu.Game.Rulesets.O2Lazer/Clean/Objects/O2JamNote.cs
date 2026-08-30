using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Objects;

public sealed class O2JamNote : Note, IO2JamJudgedObject
{
    public double ChartPosition { get; set; }

    public O2JamTimingMap TimingMap { get; set; } = new(120);

    public O2JamEndpointKind EndpointKind => O2JamEndpointKind.Tap;

    public O2JamJudgement Judge(double sourceTime, bool explicitAttempt) =>
        O2JamHitObjectTiming.Judge(this, sourceTime, explicitAttempt);

    public override double MaximumJudgementOffset => O2JamHitObjectTiming.MaximumJudgementOffset(this);

    public override Judgement CreateJudgement() => new O2JamJudgementDefinition();

    protected override HitWindows CreateHitWindows() => new O2JamFrameworkHitWindows(this);
}
