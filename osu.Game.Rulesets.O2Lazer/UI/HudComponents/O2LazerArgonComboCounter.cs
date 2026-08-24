using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Rulesets.UI.Scrolling;
using osu.Game.Screens.Play.HUD;

namespace osu.Game.Rulesets.O2Lazer.UI.HudComponents;

public partial class O2LazerArgonComboCounter : ArgonComboCounter
{
    protected override bool DisplayXSymbol => false;

    [Resolved]
    private IScrollingInfo scrollingInfo { get; set; } = null!;

    private IBindable<ScrollingDirection> direction = null!;

    protected override void LoadComplete()
    {
        base.LoadComplete();

        UsesFixedAnchor = true;

        direction = scrollingInfo.Direction.GetBoundCopy();
        direction.BindValueChanged(_ => updateAnchor());

        Schedule(() => Schedule(updateAnchor));
    }

    private void updateAnchor()
    {
        if (Anchor.HasFlag(Anchor.y1))
            return;

        Anchor &= ~(Anchor.y0 | Anchor.y2);
        Anchor |= direction.Value == ScrollingDirection.Up ? Anchor.y2 : Anchor.y0;
        Y = Math.Abs(Y) * (direction.Value == ScrollingDirection.Up ? -1 : 1);
    }
}
