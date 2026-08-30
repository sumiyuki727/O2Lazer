using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.O2Lazer.Replays;

/// <summary>
/// Stores the complete O2Jam key state at one point in a replay.
/// </summary>
public class O2JamReplayFrame : ReplayFrame
{
    public List<ManiaAction> Actions { get; set; } = [];

    public O2JamReplayFrame()
    {
    }

    public O2JamReplayFrame(double time, params ManiaAction[] actions)
        : base(time)
    {
        Actions.AddRange(actions);
    }

    public override bool IsEquivalentTo(ReplayFrame other) =>
        other is O2JamReplayFrame o2JamFrame
        && Time == o2JamFrame.Time
        && Actions.SequenceEqual(o2JamFrame.Actions);
}
