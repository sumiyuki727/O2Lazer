using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

public static class O2LazerScoreGraphScoreSelector
{
    public static ScoreInfo? SelectBest(IEnumerable<ScoreInfo> scores, IReadOnlyList<Mod> selectedMods, int maximumExScore) =>
        scores.MaxBy(score => (O2LazerExScore.Calculate(score, maximumExScore), -score.Date.UtcDateTime.Ticks));
}
