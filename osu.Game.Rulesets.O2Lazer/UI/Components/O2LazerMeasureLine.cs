using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.O2Lazer.UI.Gameplay;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.UI.Components;

public sealed partial class O2LazerMeasureLine : CompositeDrawable
{

    private readonly Box mainLine;
    private readonly Box leftAnchor;
    private readonly Box rightAnchor;
    private O2LazerGameplayScrollController? scrollController;
    private O2LazerStage? stage;

    // Two coordinate representations of the same measure line:
    //   scrollAtTick — tick-based scroll coordinate (used in normal BPM-aware mode)
    //   timeAtTick   — projected real time          (used in constant-scroll mode)
    // Progress = chosenValue - CurrentScrollPosition/Time.Current.
    private double scrollAtTick;
    private double timeAtTick;

    public O2LazerMeasureLine()
    {
        Anchor = Anchor.TopLeft;
        Origin = Anchor.TopLeft;

        var edgeSmoothness = new Vector2(0.3f);

        InternalChildren =
        [
            mainLine = new Box
            {
                Name = "Bar line",
                EdgeSmoothness = edgeSmoothness,
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
                RelativeSizeAxes = Axes.Both,
            },
            leftAnchor = new Box
            {
                Name = "Left anchor",
                EdgeSmoothness = edgeSmoothness,
                Blending = BlendingParameters.Additive,
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreRight,
                Width = 10,
                RelativeSizeAxes = Axes.Y,
                Colour = ColourInfo.GradientHorizontal(Color4.Transparent, Color4.White),
            },
            rightAnchor = new Box
            {
                Name = "Right anchor",
                EdgeSmoothness = edgeSmoothness,
                Blending = BlendingParameters.Additive,
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreLeft,
                Width = 10,
                RelativeSizeAxes = Axes.Y,
                Colour = ColourInfo.GradientHorizontal(Color4.White, Color4.Transparent),
            },
        ];
    }

    internal void Apply(O2LazerMeasureLineContainer.MeasureLineInfo info, O2LazerGameplayScrollController scrollController, O2LazerStage stage)
    {
        this.scrollController = scrollController;
        this.stage = stage;
        scrollAtTick = info.ScrollPosition;
        timeAtTick = info.Time;
    }

    protected override void Update()
    {
        base.Update();

        if (Parent == null || scrollController == null || stage == null || !stage.IsLoaded || stage.DrawHeight <= 0)
        {
            Alpha = 0;
            return;
        }

        var progress = scrollController.ConstantScrollActive
            ? timeAtTick - scrollController.CurrentScrollPosition
            : scrollAtTick - scrollController.CurrentScrollPosition;

        // Once the measure start has passed the judgement line (progress < 0),
        // the measure line should no longer be displayed.
        if (progress < 0)
        {
            Alpha = 0;
            return;
        }

        var y = scrollController.YForScrollProgress(progress, stage.DrawHeight, stage.HitTargetPosition);

        if (!float.IsFinite(y))
        {
            Alpha = 0;
            return;
        }

        X = 0;
        Y = y - stage.BarLineHeight / 2;
        Width = Math.Max(1, stage.DrawWidth);
        Height = Math.Max(0, stage.BarLineHeight);
        Alpha = Height > 0 ? 1 : 0;
        mainLine.Colour = stage.BarLineColour;
        mainLine.Alpha = 0.5f;
        leftAnchor.Alpha = rightAnchor.Alpha = mainLine.Alpha * 0.3f;
    }
}
