using osu.Game.Rulesets.Difficulty;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Difficulty;

/// <summary>
/// O2Lazer does not award performance points yet. Returning a zero result lets osu!'s native
/// results display run its normal "no PP awarded" graying instead of leaving PP undefined.
/// </summary>
public class O2LazerPerformanceCalculator(Ruleset ruleset) : PerformanceCalculator(ruleset)
{
    protected override PerformanceAttributes CreatePerformanceAttributes(ScoreInfo score, DifficultyAttributes attributes)
        => new() { Total = 0 };
}
