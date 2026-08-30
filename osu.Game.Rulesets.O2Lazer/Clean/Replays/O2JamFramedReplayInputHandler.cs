using System.Collections.Generic;
using System.Linq;
using osu.Framework.Input.StateChanges;
using osu.Game.Replays;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.O2Lazer.Replays;

public sealed class O2JamFramedReplayInputHandler : FramedReplayInputHandler<O2JamReplayFrame>
{
    public O2JamFramedReplayInputHandler(Replay replay)
        : base(replay)
    {
    }

    protected override bool IsImportant(O2JamReplayFrame frame) => frame.Actions.Any();

    protected override void CollectReplayInputs(List<IInput> inputs) =>
        inputs.Add(new ReplayState<ManiaAction> { PressedActions = CurrentFrame?.Actions ?? [] });
}
