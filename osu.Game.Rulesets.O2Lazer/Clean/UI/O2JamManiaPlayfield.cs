using JetBrains.Annotations;
using osu.Framework.Allocation;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.UI.Objects;

namespace osu.Game.Rulesets.O2Lazer.UI;

public partial class O2JamManiaPlayfield : ManiaPlayfield
{
    public O2JamManiaPlayfield(System.Collections.Generic.List<StageDefinition> stageDefinitions)
        : base(stageDefinitions)
    {
    }

    [Pure]
    protected override Stage CreateStage(int firstColumnIndex, StageDefinition stageDefinition, ref ManiaAction columnAction) =>
        new O2JamManiaStage(firstColumnIndex, stageDefinition, ref columnAction);
}

public partial class O2JamManiaStage : Stage
{
    public O2JamManiaStage(int firstColumnIndex, StageDefinition definition, ref ManiaAction columnStartAction)
        : base(firstColumnIndex, definition, ref columnStartAction)
    {
    }

    [Pure]
    protected override Column CreateColumn(int index, bool isSpecial) => new O2JamManiaColumn(index, isSpecial);
}

public partial class O2JamManiaColumn : Column
{
    public O2JamManiaColumn(int index, bool isSpecial)
        : base(index, isSpecial)
    {
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        // Exact-type pools let O2Jam own judgement while native Mania drawables remain the visual foundation.
        RegisterPool<O2JamNote, O2JamDrawableNote>(10, 50);
        RegisterPool<O2JamHoldNote, O2JamDrawableHoldNote>(10, 50);
        RegisterPool<O2JamHoldHead, O2JamDrawableHoldHead>(10, 50);
        RegisterPool<O2JamHoldTail, O2JamDrawableHoldTail>(10, 50);
        RegisterPool<O2JamHoldBody, O2JamDrawableHoldBody>(10, 50);
    }
}
