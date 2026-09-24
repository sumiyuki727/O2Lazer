using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.UI;

// Exact-difficulty selection is independent of the collection notification transport.
internal static partial class O2JamSongSelectRankPatch
{
    internal static ScoreInfo? SelectTopScore(
        IEnumerable<ScoreInfo> scores,
        BeatmapInfo beatmap,
        RulesetInfo ruleset,
        int localUserId) =>
        scores.Where(score => score.BeatmapInfo?.ID == beatmap.ID)
              .Where(score => score.UserID == localUserId || score.UserID <= 1)
              .Where(score => string.Equals(score.Ruleset.ShortName, ruleset.ShortName, StringComparison.Ordinal) && !score.DeletePending)
              .MaxBy(score => (score.TotalScore, -score.Date.UtcDateTime.Ticks));
}
