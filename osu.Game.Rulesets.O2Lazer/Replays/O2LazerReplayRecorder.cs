using System.Collections.Generic;
using osu.Game.Rulesets.O2Lazer.IO.Input;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.O2Lazer.Replays;

public partial class O2LazerReplayRecorder(Score score) : ReplayRecorder<O2LazerAction>(score)
{
    protected override ReplayFrame HandleFrame(Vector2 mousePosition, List<O2LazerAction> actions, ReplayFrame previousFrame) =>
        new O2LazerReplayFrame(Time.Current, actions.ToArray());
}
