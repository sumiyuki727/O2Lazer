using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.O2Jam;
using osu.Game.Rulesets.O2Lazer.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

public class O2LazerHitWindows(int rank = 2, O2LazerLayoutVariant layout = O2LazerLayoutVariant.O2Jam7K, int column = 1, double? judgementRate = null, double bpm = O2JamScoring.DefaultBpm) : HitWindows
{
    /// <summary>Fallback BAD window (ms) used when <see cref="HitWindows" /> is unavailable.</summary>
    public const double FALLBACK_BAD_WINDOW = 280;

    private readonly double judgementRate = resolveRate(rank, layout, judgementRate);
    private readonly double bpm = bpm;
    private O2LazerJudgementWindowTable table = createTable(resolveRate(rank, layout, judgementRate), layout, column, bpm);

    public override bool IsHitResultAllowed(HitResult result) => result switch
    {
        HitResult.Perfect or HitResult.Great or HitResult.Good or HitResult.Ok or HitResult.Meh or HitResult.Miss => true,
        _ => false,
    };

    public override void SetDifficulty(double difficulty)
    {
        table = createTable(judgementRate, layout, column, bpm);
    }

    /// <summary>
    /// Framework-facing symmetric window derived from the active beatoraja profile.
    /// <list type="bullet">
    ///     <item>For PGREAT / GREAT / GOOD: returns the tighter side of the asymmetric window.</item>
    ///     <item>For BAD / Meh / Miss: returns the slow side (the conservative bound used by
    ///     framework lifetime and HUD calculations).</item>
    /// </list>
    /// </summary>
    public override double WindowFor(HitResult result) => result switch
    {
        HitResult.Perfect => table.FrameworkWindowFor(HitResult.Perfect),
        HitResult.Great => table.FrameworkWindowFor(HitResult.Great),
        HitResult.Good => table.FrameworkWindowFor(HitResult.Good),
        HitResult.Ok => table.SlowWindowFor(HitResult.Ok),
        HitResult.Meh => table.SlowWindowFor(HitResult.Ok),
        HitResult.Miss => table.SlowWindowFor(HitResult.Ok),
        _ => 0,
    };

    private static O2LazerJudgementWindowTable createTable(double judgementRate, O2LazerLayoutVariant layout, int column, double bpm)
        => O2LazerJudgementProfileProvider.GetTable(layout, column, judgementRate, tail: false, bpm);

    private static double resolveRate(int rank, O2LazerLayoutVariant layout, double? judgementRate) =>
        judgementRate ?? O2LazerJudgementProfileProvider.RateForRank(layout, rank);
}
