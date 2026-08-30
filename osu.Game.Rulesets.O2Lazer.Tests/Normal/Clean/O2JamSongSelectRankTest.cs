using System;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamSongSelectRankTest
{
    [Test]
    public void SharedSourceHashStillSelectsOnlyExactDifficulty()
    {
        var ruleset = new RulesetInfo { ShortName = O2LazerIdentity.ShortName };
        var ex = new BeatmapInfo(ruleset) { ID = Guid.NewGuid(), Hash = "shared-ojn-hash" };
        var nx = new BeatmapInfo(ruleset) { ID = Guid.NewGuid(), Hash = "shared-ojn-hash" };
        var exScore = createScore(ex, ruleset, 1000, ScoreRank.A);
        var nxScore = createScore(nx, ruleset, 2000, ScoreRank.S);

        var selected = O2JamSongSelectRankPatch.SelectTopScore([exScore, nxScore], ex, ruleset, 2);

        Assert.That(selected, Is.SameAs(exScore));
        Assert.That(selected!.Rank, Is.EqualTo(ScoreRank.A));
    }

    private static ScoreInfo createScore(BeatmapInfo beatmap, RulesetInfo ruleset, long score, ScoreRank rank) =>
        new(beatmap, ruleset, new RealmUser { OnlineID = 2, Username = "Local user" })
        {
            TotalScore = score,
            Rank = rank,
            Date = DateTimeOffset.UtcNow,
        };
}
