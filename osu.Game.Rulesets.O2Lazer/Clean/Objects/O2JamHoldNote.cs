using System.Threading;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Objects;

public sealed class O2JamHoldNote : HoldNote
{
    public bool ReleaseTimingDisabled { get; set; }

    public double HeadChartPosition { get; set; }

    public double TailChartPosition { get; set; }

    public O2JamTimingMap TimingMap { get; set; } = new(120);

    protected override void CreateNestedHitObjects(CancellationToken cancellationToken)
    {
        // Keep release nodes silent even when the object was constructed outside the OJN factory.
        NodeSamples = [GetNodeSamples(0), []];

        AddNested(Head = new O2JamHoldHead
        {
            StartTime = StartTime,
            Column = Column,
            Samples = GetNodeSamples(0),
            ChartPosition = HeadChartPosition,
            TimingMap = TimingMap,
        });

        AddNested(Tail = new O2JamHoldTail
        {
            StartTime = EndTime,
            Column = Column,
            Samples = GetNodeSamples(NodeSamples.Count - 1),
            ChartPosition = TailChartPosition,
            TimingMap = TimingMap,
            ReleaseTimingDisabled = ReleaseTimingDisabled,
        });

        AddNested(Body = new O2JamHoldBody
        {
            StartTime = StartTime,
            Column = Column,
            Duration = Duration,
        });
    }
}

public sealed class O2JamHoldHead : HeadNote, IO2JamJudgedObject
{
    public double ChartPosition { get; set; }

    public O2JamTimingMap TimingMap { get; set; } = new(120);

    public O2JamEndpointKind EndpointKind => O2JamEndpointKind.HoldHead;

    public O2JamJudgement Judge(double sourceTime, bool explicitAttempt) =>
        O2JamHitObjectTiming.Judge(this, sourceTime, explicitAttempt);

    public override double MaximumJudgementOffset => O2JamHitObjectTiming.MaximumJudgementOffset(this);

    public override Judgement CreateJudgement() => new O2JamJudgementDefinition();

    protected override osu.Game.Rulesets.Scoring.HitWindows CreateHitWindows() => new O2JamFrameworkHitWindows(this);
}

public sealed class O2JamHoldTail : TailNote, IO2JamJudgedObject
{
    public bool ReleaseTimingDisabled { get; set; }

    public double ChartPosition { get; set; }

    public O2JamTimingMap TimingMap { get; set; } = new(120);

    public O2JamEndpointKind EndpointKind => O2JamEndpointKind.HoldRelease;

    public O2JamJudgement Judge(double sourceTime, bool explicitAttempt) =>
        O2JamHitObjectTiming.Judge(this, sourceTime, explicitAttempt);

    public override double MaximumJudgementOffset => O2JamHitObjectTiming.MaximumJudgementOffset(this);

    public override Judgement CreateJudgement() => new O2JamJudgementDefinition();

    protected override osu.Game.Rulesets.Scoring.HitWindows CreateHitWindows() => new O2JamFrameworkHitWindows(this);
}

public sealed class O2JamHoldBody : HoldNoteBody
{
    public override Judgement CreateJudgement() => new IgnoreJudgement();
}
