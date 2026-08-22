using osu.Framework.Allocation;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.UI.Objects;

namespace osu.Game.Rulesets.O2Lazer.UI.Components;

public sealed partial class O2LazerColumnGeneric<TCol>(int index, O2LazerPlayfield playfield)
    : O2LazerColumn(index, playfield)
    where TCol : struct, IColumnProvider
{

    [BackgroundDependencyLoader]
    private void load()
    {
        RegisterPool<O2LazerNote, DrawableO2LazerNote<TCol>>(64, int.MaxValue);
        RegisterPool<O2LazerLongNote, DrawableO2LazerLongNote<TCol>>(32, int.MaxValue);
    }
}
