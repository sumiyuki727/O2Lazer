using System.Collections.Generic;
using System;
using osu.Game.Beatmaps;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Mods;

public class O2LazerModAutoplay : ModAutoplay
{
    public static Score CreateScoreWithBranchDecisions(IBeatmap beatmap, ModReplayData replayData)
    {
        var score = new Score
        {
            Replay = replayData.Replay,
            ScoreInfo =
            {
                Date = DateTimeOffset.Now,
                User = new APIUser
                {
                    Id = replayData.User.OnlineID,
                    Username = replayData.User.Username,
                    IsBot = replayData.User.IsBot,
                },
            },
        };

        return score;
    }

    public override ModReplayData CreateReplayData(IBeatmap beatmap, IReadOnlyList<Mod> mods)
    {
        var replay = new O2LazerAutoGenerator((O2LazerBeatmap)beatmap).Generate();

        return new ModReplayData(replay, new ModCreatedUser { Username = "osu!topus" });
    }

}
