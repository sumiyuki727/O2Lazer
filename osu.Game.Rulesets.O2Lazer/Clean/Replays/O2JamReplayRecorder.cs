using System.Collections.Generic;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.O2Lazer.Replays;

public partial class O2JamReplayRecorder(Score score) : ReplayRecorder<ManiaAction>(score)
{
    protected override ReplayFrame HandleFrame(Vector2 mousePosition, List<ManiaAction> actions, ReplayFrame previousFrame) =>
        new O2JamReplayFrame(Time.Current, actions.ToArray());
}
