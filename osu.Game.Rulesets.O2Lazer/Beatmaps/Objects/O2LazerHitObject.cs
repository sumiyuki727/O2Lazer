using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.O2Lazer.O2Jam;
using osu.Game.Rulesets.O2Lazer.Scoring.Judgements;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;

public class O2LazerHitObject : HitObject
{
    public IO2LazerBeatmap Beatmap { get; set; } = null!;

    public int Column { get; set; }

    public ushort SourceChannel { get; set; }

    public ushort? SampleKey { get; set; }

    public int SampleVolume { get; set; } = 100;

    public double JudgementRate { get; set; } = double.NaN;

    public double EffectiveJudgementRate => double.IsNaN(JudgementRate)
        ? Beatmap.ExRank is { } exRank
            ? O2LazerJudgementProfileProvider.RateForExRank(Beatmap.LayoutVariant, exRank)
            : O2LazerJudgementProfileProvider.RateForRank(Beatmap.LayoutVariant, Beatmap.Rank)
        : JudgementRate;

    /// <summary>
    /// Active absolute BPM at the head judgement time, used to convert O2Jam's beat-based windows to milliseconds.
    /// </summary>
    public double BpmAtStartTime => getBpmAtTime(StartTime);

    /// <summary>
    /// Active absolute BPM at the tail judgement time, used to convert O2Jam's beat-based windows to milliseconds.
    /// </summary>
    public double BpmAtEndTime => getBpmAtTime(HitObjectExtensions.GetEndTime(this));

    /// <summary>
    ///     Precomputed scroll position at <see cref="HitObject.StartTime"/>.
    ///     Computed once during beatmap loading via <see cref="Parsing.O2LazerTimingMap.GetScrollPositionAtTime"/>.
    ///     Eliminates per-frame calls to the full timing-map lookup chain in the drawable hot path.
    /// </summary>
    public double ScrollPositionAtStartTime { get; set; }

    public static O2LazerHitObject CreateForKind(bool isLongNote, bool isMine) => isLongNote ? new O2LazerLongNote() : new O2LazerNote();

    public O2LazerHitObject ToTypedHitObject()
    {
        if (GetType() != typeof(O2LazerHitObject))
            return this;

        var typed = CreateForKind(this is O2LazerLongNote, false);
        CopyTo(typed);
        return typed;
    }

    public override Judgement CreateJudgement() => new O2LazerJudgement();

    protected override O2LazerHitWindows CreateHitWindows() => new(Beatmap.Rank, Beatmap.LayoutVariant, Column, EffectiveJudgementRate, BpmAtStartTime);

    private double getBpmAtTime(double time) => Beatmap.TimingMap?.GetBpmAtTime(time) ?? O2JamScoring.DefaultBpm;

    protected virtual void CopyTo(O2LazerHitObject target)
    {
        target.StartTime = StartTime;
        target.Column = Column;
        target.SourceChannel = SourceChannel;
        target.SampleKey = SampleKey;
        target.SampleVolume = SampleVolume;
        target.JudgementRate = JudgementRate;
        target.ScrollPositionAtStartTime = ScrollPositionAtStartTime;
        target.Beatmap = Beatmap;
    }
}

