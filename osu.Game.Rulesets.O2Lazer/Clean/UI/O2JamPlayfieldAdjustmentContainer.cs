using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.UI;
using osuTK;

namespace osu.Game.Rulesets.O2Lazer.UI;

public partial class O2JamPlayfieldAdjustmentContainer : PlayfieldAdjustmentContainer
{
    protected override Container<Drawable> Content { get; }

    private readonly DrawSizePreservingFillContainer scalingContainer;
    private readonly O2JamDrawableRuleset drawableRuleset;

    public O2JamPlayfieldAdjustmentContainer(O2JamDrawableRuleset drawableRuleset)
    {
        this.drawableRuleset = drawableRuleset;

        InternalChild = scalingContainer = new DrawSizePreservingFillContainer
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Child = Content = new Container { RelativeSizeAxes = Axes.Both },
        };
    }

    protected override void Update()
    {
        base.Update();

        var aspectRatio = DrawWidth / DrawHeight;
        var isPortrait = aspectRatio < 1;

        if (isPortrait && drawableRuleset.Beatmap.Stages.Count == 1)
        {
            const float baseScale = 1.25f;
            const float baseWidth = 768f / baseScale;
            const float sideGap = 0.9f;

            scalingContainer.Strategy = DrawSizePreservationStrategy.Maximum;
            var stageWidth = drawableRuleset.Playfield.Stages[0].DrawWidth;
            scalingContainer.TargetDrawSize = new Vector2(1024, baseWidth * Math.Max(stageWidth / aspectRatio / (baseWidth * sideGap), 1));
        }
        else
        {
            scalingContainer.Strategy = DrawSizePreservationStrategy.Minimum;
            scalingContainer.Scale = Vector2.One;
            scalingContainer.Size = Vector2.One;
            scalingContainer.TargetDrawSize = new Vector2(1024, 768);
        }
    }
}
