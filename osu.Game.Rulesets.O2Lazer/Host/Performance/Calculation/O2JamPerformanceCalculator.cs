using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mania.Difficulty;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Difficulty;

public sealed class O2JamPerformanceCalculator : ManiaPerformanceCalculator
{
    protected override PerformanceAttributes CreatePerformanceAttributes(ScoreInfo score, DifficultyAttributes attributes)
    {
        // O2Jam's original scoring system has no osu! performance equivalent. MS supplies
        // ManiaDifficultyAttributes, allowing the native mania formula to run unchanged.
        if (!O2JamGameplayProfile.UsesManiaScore(score.Mods))
            return new ManiaPerformanceAttributes();

        return base.CreatePerformanceAttributes(score, attributes);
    }
}
