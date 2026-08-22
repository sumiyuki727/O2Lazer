using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.IO.Input;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Legacy;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.Skinning.LegacyDrawables;

/// <summary>
/// Separated from the lane background so legacy ordering places the light above the hit target but below notes.
/// </summary>
internal sealed partial class LegacyO2LazerColumnLight : CompositeDrawable, IKeyBindingHandler<O2LazerAction>
{
    private readonly O2LazerSkinComponentLookup lookup;
    private readonly string lightImage;
    private readonly Color4 lightColour;
    private readonly double lightFrameLength;
    private readonly float lightPosition;

    private Drawable? light;

    [Resolved(CanBeNull = true)]
    private O2LazerPlayfield? playfield { get; set; }

    public LegacyO2LazerColumnLight(O2LazerLegacySkinTransformer transformer, O2LazerSkinComponentLookup lookup)
    {
        this.lookup = lookup;
        RelativeSizeAxes = Axes.Both;

        lightColour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLightColour, lookup)?.Value ?? Color4.White;
        lightImage = transformer.GetManiaConfig<string>(LegacyManiaSkinConfigurationLookups.LightImage, lookup)?.Value ?? "mania-stage-light";
        lightPosition = transformer.GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.LightPosition, lookup)?.Value ?? 0;
        var lightFramePerSecond = transformer.GetManiaConfig<int>(LegacyManiaSkinConfigurationLookups.LightFramePerSecond, lookup)?.Value ?? 60;
        lightFrameLength = 1000d / lightFramePerSecond;
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin)
    {
        // The active source includes the embedded legacy assets used when a user skin omits mania-stage-light.
        InternalChild = light = skin.GetAnimation(lightImage, true, true, frameLength: lightFrameLength)?.With(d =>
        {
            d.Anchor = Anchor.BottomCentre;
            d.Origin = Anchor.BottomCentre;
            d.Y = -lightPosition;
            d.RelativeSizeAxes = Axes.X;
            d.Width = 1;
            d.Colour = LegacyColourCompatibility.DisallowZeroAlpha(lightColour);
            d.Alpha = 0;
        }) ?? Empty();
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (playfield == null)
            return;

        playfield.Stage.LightPositionOffsetChanged += updateLightPosition;
        updateLightPosition(playfield.Stage.LightPositionOffset);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (playfield != null)
            playfield.Stage.LightPositionOffsetChanged -= updateLightPosition;

        base.Dispose(isDisposing);
    }

    private void updateLightPosition(float offset)
    {
        light?.Y = -(lightPosition + offset);
    }

    public bool OnPressed(KeyBindingPressEvent<O2LazerAction> e)
    {
        if (lookup.ColumnIndex == null || O2LazerKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) != lookup.ColumnIndex)
            return false;

        light?.FadeIn();
        light?.ScaleTo(Vector2.One);
        return false;
    }

    public void OnReleased(KeyBindingReleaseEvent<O2LazerAction> e)
    {
        if (lookup.ColumnIndex == null || O2LazerKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) != lookup.ColumnIndex)
            return;

        light?.FadeTo(0, 250);
        light?.ScaleTo(new Vector2(1, 0), 250);
    }
}
