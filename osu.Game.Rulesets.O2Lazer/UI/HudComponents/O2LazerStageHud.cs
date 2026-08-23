using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Configuration;
using osu.Game.Rulesets.O2Lazer.Settings.Components;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.UI;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.UI.HudComponents;

internal sealed partial class O2LazerStageHud : O2LazerHudComponent
{
    [SettingSource(typeof(O2LazerStrings), nameof(O2LazerStrings.ScaleStageWidthByColumns), nameof(O2LazerStrings.ScaleStageWidthByColumnsDescription),
        SettingControlType = typeof(StageWidthScalingCheckbox))]
    public BindableFloat ProportionalWidthReference { get; } = new();

    [SettingSource(typeof(O2LazerStrings), nameof(O2LazerStrings.JudgementLineOffset), nameof(O2LazerStrings.JudgementLineOffsetDescription))]
    public BindableFloat JudgementLineOffset { get; } = new()
    {
        MinValue = -768,
        MaxValue = 768,
        Precision = 1,
    };

    [SettingSource(typeof(O2LazerStrings), nameof(O2LazerStrings.LightPositionOffset), nameof(O2LazerStrings.LightPositionOffsetDescription))]
    public BindableFloat LightPositionOffset { get; } = new()
    {
        MinValue = -768,
        MaxValue = 768,
        Precision = 1,
    };

    private readonly Container editHandle;
    private O2LazerStageHudController? controller;
    private float currentStageWidth;

    [Resolved]
    private DrawableRuleset drawableRuleset { get; set; } = null!;

    public O2LazerStageHud()
    {
        Anchor = Anchor.BottomCentre;
        Origin = Anchor.BottomCentre;
        RelativeSizeAxes = Axes.Y;
        Size = Vector2.Zero;
        AlwaysPresent = true;
        Alpha = 0;

        InternalChild = editHandle = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Masking = true,
            BorderThickness = 2,
            BorderColour = new Color4(70, 210, 255, 255),
            Alpha = 0,
            Children =
            [
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(20, 40, 55, 255),
                    Alpha = 0.45f,
                },
                new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.X,
                    Height = 2,
                    Colour = Color4.White,
                },
                new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Y,
                    Width = 2,
                    Colour = Color4.White,
                },
            ],
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (drawableRuleset is O2LazerDrawableRuleset o2lazerDrawableRuleset)
        {
            controller = o2lazerDrawableRuleset.StageHudController;
            controller.Register(this, this.FindClosestParent<ISerialisableDrawableContainer>());
            JudgementLineOffset.ValueChanged += onJudgementLineOffsetChanged;
            LightPositionOffset.ValueChanged += onLightPositionOffsetChanged;
            controller.SetHitTargetPositionOffset(JudgementLineOffset.Value);
            controller.SetLightPositionOffset(LightPositionOffset.Value);
        }

        if (SkinEditor != null)
            SkinEditor.State.BindValueChanged(_ => updateEditModeVisibility(), true);
        else
            applyEditModeVisibility(false);
    }

    protected override void Dispose(bool isDisposing)
    {
        JudgementLineOffset.ValueChanged -= onJudgementLineOffsetChanged;
        LightPositionOffset.ValueChanged -= onLightPositionOffsetChanged;
        controller?.Unregister(this);
        controller = null;

        base.Dispose(isDisposing);
    }

    private void onJudgementLineOffsetChanged(ValueChangedEvent<float> offset) => controller?.SetHitTargetPositionOffset(offset.NewValue);

    private void onLightPositionOffsetChanged(ValueChangedEvent<float> offset) => controller?.SetLightPositionOffset(offset.NewValue);

    internal float GetCurrentStageWidth() => currentStageWidth;

    internal void SetCurrentStageWidth(float width) => currentStageWidth = width;

    internal void SetJudgementLineOffsetRange(float minimum, float maximum)
    {
        JudgementLineOffset.MinValue = minimum;
        JudgementLineOffset.MaxValue = maximum;
    }

    internal void SetLightPositionOffsetRange(float minimum, float maximum)
    {
        LightPositionOffset.MinValue = minimum;
        LightPositionOffset.MaxValue = maximum;
    }

    private void updateEditModeVisibility() => applyEditModeVisibility(SkinEditor?.State.Value == Visibility.Visible);

    private void applyEditModeVisibility(bool isEditing)
    {
        ClearTransforms();

        // Runtime rendering belongs to O2LazerStage; this shell only exposes a stable skin-editor handle.
        Alpha = isEditing ? 1 : 0;
        editHandle.Alpha = isEditing ? 1 : 0;
    }
}
