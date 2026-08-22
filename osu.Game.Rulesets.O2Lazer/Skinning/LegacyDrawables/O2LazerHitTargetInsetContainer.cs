using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Rulesets.O2Lazer.UI.Components;

namespace osu.Game.Rulesets.O2Lazer.Skinning.LegacyDrawables;

/// <summary>
/// Keeps legacy separators from covering key-area artwork below the hit target, matching mania's stage composition.
/// </summary>
internal sealed partial class O2LazerHitTargetInsetContainer : Container
{
    protected override Container<Drawable> Content => content;

    private readonly Container content;

    [Resolved(CanBeNull = true)]
    private O2LazerPlayfield? playfield { get; set; }

    public O2LazerHitTargetInsetContainer()
    {
        RelativeSizeAxes = Axes.Both;
        InternalChild = content = new Container { RelativeSizeAxes = Axes.Both };
    }

    protected override void Update()
    {
        base.Update();

        var bottomInset = playfield?.Stage.HitTargetPosition ?? O2LazerStage.HIT_TARGET_POSITION;

        if (content.Padding.Bottom == bottomInset)
            return;

        content.Padding = new MarginPadding
        {
            Bottom = bottomInset,
        };
    }
}
