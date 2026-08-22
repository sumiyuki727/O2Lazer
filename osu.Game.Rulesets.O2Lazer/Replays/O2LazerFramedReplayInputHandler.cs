using System.Collections.Generic;
using osu.Framework.Input.StateChanges;
using osu.Game.Replays;
using osu.Game.Rulesets.O2Lazer.IO.Input;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.O2Lazer.Replays;

public class O2LazerFramedReplayInputHandler : FramedReplayInputHandler<O2LazerReplayFrame>
{
    public O2LazerFramedReplayInputHandler(Replay replay)
        : base(replay)
    {
    }

    protected override bool IsImportant(O2LazerReplayFrame frame) => true;

    protected override void CollectReplayInputs(List<IInput> inputs)
    {
        inputs.Add(new ReplayState<O2LazerAction> { PressedActions = CurrentFrame?.Actions ?? [] });
    }
}
