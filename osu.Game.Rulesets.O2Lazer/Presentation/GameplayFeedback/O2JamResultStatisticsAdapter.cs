using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Statistics;

namespace osu.Game.Rulesets.O2Lazer.UI;

internal static class O2JamResultStatisticsAdapter
{
    private static readonly ManiaRuleset maniaPresentation = new();

    internal static StatisticItem[] Create(ScoreInfo score, IBeatmap playableBeatmap)
    {
        var statistics = maniaPresentation.CreateStatisticsForScore(score, playableBeatmap);
        if (O2JamGameplayProfile.UsesManiaScore(score.Mods))
            return statistics;

        // Mania places its PP breakdown first. O2Jam scores have no performance model, but the
        // timing distribution and hit-error statistics remain valid for their recorded hit events.
        return statistics.Length > 1 ? statistics[1..] : [];
    }
}
