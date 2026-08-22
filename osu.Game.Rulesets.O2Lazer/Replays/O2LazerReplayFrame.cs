using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.O2Lazer.IO.Input;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.O2Lazer.Replays;

public class O2LazerReplayFrame : ReplayFrame
{
    public const double BRANCH_DECISION_FRAME_TIME = -1000000000;

    // deserialization requires a setter
    // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
    public List<O2LazerAction> Actions { get; set; } = [];

    public string BranchDecisions { get; set; } = string.Empty;

    // deserialization requires a default ctor
    // ReSharper disable once UnusedMember.Global
    public O2LazerReplayFrame()
    {
    }

    public O2LazerReplayFrame(double time, params O2LazerAction[] actions)
        : base(time)
    {
        Actions.AddRange(actions);
    }

    public O2LazerReplayFrame(double time, string branchDecisions)
        : base(time)
    {
        BranchDecisions = branchDecisions;
    }

    public override bool IsEquivalentTo(ReplayFrame other) =>
        other is O2LazerReplayFrame o2lazerFrame
        && Math.Abs(Time - o2lazerFrame.Time) < 0.0001
        && BranchDecisions == o2lazerFrame.BranchDecisions
        && Actions.SequenceEqual(o2lazerFrame.Actions);
}
