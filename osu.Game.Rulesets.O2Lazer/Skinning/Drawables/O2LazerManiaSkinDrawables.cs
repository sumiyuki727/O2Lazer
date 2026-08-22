using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Layout;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.IO.Input;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Configuration;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Drawables;

internal interface IO2LazerManiaHoldNoteVisualPiece
{
    int AnimationFrameCount => 0;

    int CurrentAnimationFrame => 0;

    void SetHolding(bool holding);

    void Recycle();
}

internal interface IO2LazerManiaHoldNoteBodyPiece : IO2LazerManiaHoldNoteVisualPiece
{
    void SetTailAtTop(bool tailAtTop);
}

/// <summary>
/// The native osu!mania default note, adapted only to the O2Jam drawable hierarchy.
/// </summary>
internal partial class O2LazerManiaDefaultNotePiece : CompositeDrawable
{
    internal const float NOTE_HEIGHT = 12;

    private readonly Bindable<Color4> accentColour = new();
    private readonly Box colouredBox;

    public O2LazerManiaDefaultNotePiece()
    {
        RelativeSizeAxes = Axes.X;
        Height = NOTE_HEIGHT;
        CornerRadius = 5;
        Masking = true;

        InternalChildren =
        [
            new Box { RelativeSizeAxes = Axes.Both },
            colouredBox = new Box
            {
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
                RelativeSizeAxes = Axes.X,
                Height = NOTE_HEIGHT / 2,
                Alpha = 0.1f,
            },
        ];
    }

    [BackgroundDependencyLoader(true)]
    private void load(DrawableHitObject? drawableObject)
    {
        if (drawableObject == null)
            return;

        accentColour.BindTo(drawableObject.AccentColour);
        accentColour.BindValueChanged(onAccentChanged, true);
    }

    private void onAccentChanged(ValueChangedEvent<Color4> accent)
    {
        colouredBox.Colour = accent.NewValue.Lighten(0.9f);
        EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Glow,
            Colour = accent.NewValue.Lighten(1f).Opacity(0.2f),
            Radius = 10,
        };
    }
}

/// <summary>
/// The native osu!mania default hold body, with holding state supplied by the O2Jam LN controller.
/// </summary>
internal partial class O2LazerManiaDefaultHoldBodyPiece : CompositeDrawable, IO2LazerManiaHoldNoteBodyPiece
{
    private readonly Bindable<Color4> accentColour = new();
    private readonly Bindable<bool> isHolding = new();

    private Drawable background = null!;
    private Container foregroundContainer = null!;

    public O2LazerManiaDefaultHoldBodyPiece()
    {
        RelativeSizeAxes = Axes.Both;
        Blending = BlendingParameters.Additive;
    }

    [BackgroundDependencyLoader(true)]
    private void load(DrawableHitObject? drawableObject)
    {
        InternalChildren =
        [
            background = new Box { RelativeSizeAxes = Axes.Both },
            foregroundContainer = new Container { RelativeSizeAxes = Axes.Both },
        ];

        if (drawableObject != null)
            accentColour.BindTo(drawableObject.AccentColour);

        accentColour.BindValueChanged(accent => background.Colour = accent.NewValue.Opacity(0.7f), true);
        Recycle();
    }

    public void SetHolding(bool holding) => isHolding.Value = holding;

    public void SetTailAtTop(bool tailAtTop)
    {
    }

    public void Recycle()
    {
        isHolding.Value = false;
        foregroundContainer.Child = new ForegroundPiece
        {
            AccentColour = { BindTarget = accentColour },
            IsHolding = { BindTarget = isHolding },
        };
    }

    private partial class ForegroundPiece : CompositeDrawable
    {
        public readonly Bindable<Color4> AccentColour = new();
        public readonly IBindable<bool> IsHolding = new Bindable<bool>();

        private readonly LayoutValue subtractionCache = new(Invalidation.DrawSize);

        private BufferedContainer foregroundBuffer = null!;
        private BufferedContainer subtractionBuffer = null!;
        private Container subtractionLayer = null!;

        public ForegroundPiece()
        {
            RelativeSizeAxes = Axes.Both;
            AddLayout(subtractionCache);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = foregroundBuffer = new BufferedContainer(cachedFrameBuffer: true)
            {
                Blending = BlendingParameters.Additive,
                RelativeSizeAxes = Axes.Both,
                Children =
                [
                    new Box { RelativeSizeAxes = Axes.Both },
                    subtractionBuffer = new BufferedContainer(cachedFrameBuffer: true)
                    {
                        RelativeSizeAxes = Axes.Both,
                        BackgroundColour = Color4.White.Opacity(0),
                        Blending = new BlendingParameters { AlphaEquation = BlendingEquation.ReverseSubtract },
                        Child = subtractionLayer = new CircularContainer
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Width = 1,
                            Masking = true,
                            Child = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Alpha = 0,
                                AlwaysPresent = true,
                            },
                        },
                    },
                ],
            };

            AccentColour.BindValueChanged(onAccentChanged, true);
            IsHolding.BindValueChanged(_ => onAccentChanged(new ValueChangedEvent<Color4>(AccentColour.Value, AccentColour.Value)), true);
        }

        private void onAccentChanged(ValueChangedEvent<Color4> accent)
        {
            foregroundBuffer.Colour = accent.NewValue.Opacity(0.5f);

            const float animation_length = 50;

            foregroundBuffer.ClearTransforms(false, nameof(foregroundBuffer.Colour));

            if (IsHolding.Value)
            {
                var synchronisedOffset = animation_length * 2 - Time.Current % (animation_length * 2);
                using (foregroundBuffer.BeginDelayedSequence(synchronisedOffset))
                    foregroundBuffer.FadeColour(accent.NewValue.Lighten(0.2f), animation_length).Then().FadeColour(foregroundBuffer.Colour, animation_length).Loop();
            }

            subtractionCache.Invalidate();
        }

        protected override void Update()
        {
            base.Update();

            if (subtractionCache.IsValid)
                return;

            subtractionLayer.Width = 5;
            subtractionLayer.Height = Math.Max(0, DrawHeight - DrawWidth);
            subtractionLayer.EdgeEffect = new EdgeEffectParameters
            {
                Colour = Color4.White,
                Type = EdgeEffectType.Glow,
                Radius = DrawWidth,
            };

            foregroundBuffer.ForceRedraw();
            subtractionBuffer.ForceRedraw();
            subtractionCache.Validate();
        }
    }
}

internal partial class O2LazerManiaArgonNotePiece : CompositeDrawable
{
    internal const float NOTE_HEIGHT = 42;
    internal const float NOTE_ACCENT_RATIO = 0.82f;
    internal const float CORNER_RADIUS = 3.4f;

    private readonly Bindable<Color4> accentColour = new();
    private readonly Box colouredBox;

    public O2LazerManiaArgonNotePiece()
    {
        RelativeSizeAxes = Axes.X;
        Height = NOTE_HEIGHT;
        CornerRadius = CORNER_RADIUS;
        Masking = true;

        InternalChildren =
        [
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = ColourInfo.GradientVertical(Color4.Black.Opacity(0), Color4.Black),
            },
            new Container
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                RelativeSizeAxes = Axes.Both,
                Height = NOTE_ACCENT_RATIO,
                Masking = true,
                CornerRadius = CORNER_RADIUS,
                Child = colouredBox = new Box { RelativeSizeAxes = Axes.Both },
            },
            new Circle
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                RelativeSizeAxes = Axes.X,
                Height = CORNER_RADIUS * 2,
            },
            CreateIcon(),
        ];
    }

    protected virtual Drawable CreateIcon() => new SpriteIcon
    {
        Anchor = Anchor.Centre,
        Origin = Anchor.Centre,
        Y = 4,
        Icon = FontAwesome.Solid.AngleDown,
        Size = new Vector2(20),
        Scale = new Vector2(1, 0.7f),
    };

    [BackgroundDependencyLoader(true)]
    private void load(DrawableHitObject? drawableObject)
    {
        if (drawableObject == null)
            return;

        accentColour.BindTo(drawableObject.AccentColour);
        accentColour.BindValueChanged(accent => colouredBox.Colour = ColourInfo.GradientVertical(accent.NewValue.Lighten(0.1f), accent.NewValue), true);
    }
}

internal partial class O2LazerManiaArgonHoldHeadPiece : O2LazerManiaArgonNotePiece
{
    protected override Drawable CreateIcon() => new Circle
    {
        Anchor = Anchor.Centre,
        Origin = Anchor.Centre,
        Y = 2,
        Size = new Vector2(20, 5),
    };
}

internal partial class O2LazerManiaArgonHoldTailPiece : CompositeDrawable, IO2LazerManiaHoldNoteVisualPiece
{
    private readonly Bindable<Color4> accentColour = new();
    private readonly O2LazerManiaArgonHittingLayer hittingLayer;
    private readonly Box foreground;
    private readonly Box foregroundAdditive;

    public O2LazerManiaArgonHoldTailPiece()
    {
        RelativeSizeAxes = Axes.X;
        Height = O2LazerManiaArgonNotePiece.NOTE_HEIGHT;

        InternalChild = new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = O2LazerManiaArgonNotePiece.NOTE_HEIGHT,
            CornerRadius = O2LazerManiaArgonNotePiece.CORNER_RADIUS,
            Masking = true,
            Children =
            [
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientVertical(Color4.Black.Opacity(0), Color4.Black),
                    Height = 0.9f,
                },
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    Height = O2LazerManiaArgonNotePiece.NOTE_ACCENT_RATIO,
                    CornerRadius = O2LazerManiaArgonNotePiece.CORNER_RADIUS,
                    Masking = true,
                    Children =
                    [
                        foreground = new Box { RelativeSizeAxes = Axes.Both },
                        hittingLayer = new O2LazerManiaArgonHittingLayer(),
                        foregroundAdditive = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Blending = BlendingParameters.Additive,
                            Height = 0.5f,
                        },
                    ],
                },
            ],
        };
    }

    [BackgroundDependencyLoader(true)]
    private void load(DrawableHitObject? drawableObject)
    {
        if (drawableObject == null)
            return;

        accentColour.BindTo(drawableObject.AccentColour);
        accentColour.BindValueChanged(accent =>
        {
            foreground.Colour = accent.NewValue.Darken(0.6f);
            foregroundAdditive.Colour = ColourInfo.GradientVertical(accent.NewValue.Opacity(0.4f), accent.NewValue.Opacity(0));
            hittingLayer.AccentColour.Value = accent.NewValue;
        }, true);
    }

    public void SetHolding(bool holding) => hittingLayer.IsHolding.Value = holding;

    public void Recycle() => hittingLayer.Recycle();
}

internal partial class O2LazerManiaArgonHoldBodyPiece : CompositeDrawable, IO2LazerManiaHoldNoteBodyPiece
{
    private readonly Bindable<Color4> accentColour = new();
    private O2LazerManiaArgonHittingLayer hittingLayer = null!;
    private Drawable background = null!;

    public O2LazerManiaArgonHoldBodyPiece()
    {
        RelativeSizeAxes = Axes.Both;
        Masking = true;
        CornerRadius = O2LazerManiaArgonNotePiece.CORNER_RADIUS;
    }

    [BackgroundDependencyLoader(true)]
    private void load(DrawableHitObject? drawableObject)
    {
        InternalChildren =
        [
            background = new Box { RelativeSizeAxes = Axes.Both },
            hittingLayer = new O2LazerManiaArgonHittingLayer(),
        ];

        if (drawableObject != null)
            accentColour.BindTo(drawableObject.AccentColour);

        accentColour.BindValueChanged(accent =>
        {
            background.Colour = accent.NewValue.Darken(0.6f);
            hittingLayer.AccentColour.Value = accent.NewValue;
        }, true);
    }

    public void SetHolding(bool holding) => hittingLayer.IsHolding.Value = holding;

    public void SetTailAtTop(bool tailAtTop)
    {
    }

    public void Recycle() => hittingLayer.Recycle();
}

internal partial class O2LazerManiaArgonHittingLayer : Box
{
    public readonly Bindable<Color4> AccentColour = new();
    public readonly Bindable<bool> IsHolding = new();

    public O2LazerManiaArgonHittingLayer()
    {
        RelativeSizeAxes = Axes.Both;
        Blending = BlendingParameters.Additive;
        Alpha = 0;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        AccentColour.BindValueChanged(accent => Colour = accent.NewValue.Lighten(0.2f).Opacity(0.3f), true);
        IsHolding.BindValueChanged(holding =>
        {
            const float animation_length = 80;

            ClearTransforms();

            if (holding.NewValue)
            {
                var synchronisedOffset = animation_length * 2 - Time.Current % (animation_length * 2);
                using (BeginDelayedSequence(synchronisedOffset))
                {
                    this.FadeTo(1, animation_length, Easing.OutSine).Then()
                        .FadeTo(0.5f, animation_length, Easing.InSine)
                        .Loop();
                }
            }
            else
                this.FadeOut(animation_length);
        }, true);
    }

    public void Recycle()
    {
        IsHolding.Value = false;
        ClearTransforms();
        Alpha = 0;
    }
}

internal partial class O2LazerManiaArgonStageBackground : CompositeDrawable
{
    public O2LazerManiaArgonStageBackground()
    {
        RelativeSizeAxes = Axes.Both;
    }
}

internal partial class O2LazerManiaDefaultStageBackground : CompositeDrawable
{
    public O2LazerManiaDefaultStageBackground()
    {
        RelativeSizeAxes = Axes.Both;
    }
}

internal partial class O2LazerManiaArgonColumnBackground : CompositeDrawable, IKeyBindingHandler<O2LazerAction>
{
    private readonly O2LazerSkinComponentLookup lookup;
    private Box background = null!;
    private Box backgroundOverlay = null!;
    private Color4 brightColour;
    private Color4 dimColour;

    public O2LazerManiaArgonColumnBackground(O2LazerSkinComponentLookup lookup)
    {
        this.lookup = lookup;
        RelativeSizeAxes = Axes.Both;
        Masking = true;
        CornerRadius = O2LazerManiaArgonNotePiece.CORNER_RADIUS;
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin)
    {
        InternalChildren =
        [
            background = new Box
            {
                Name = "Background",
                RelativeSizeAxes = Axes.Both,
            },
            backgroundOverlay = new Box
            {
                Name = "Background Gradient Overlay",
                RelativeSizeAxes = Axes.Both,
                Height = 0.5f,
                Blending = BlendingParameters.Additive,
                Alpha = 0,
            },
        ];

        var accent = skin.GetConfig<O2LazerSkinConfigurationLookup, Color4>(
            new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour, lookup))?.Value
            ?? Color4.Black;
        background.Colour = accent.Darken(3).Opacity(0.8f);
        brightColour = accent.Opacity(0.6f);
        dimColour = accent.Opacity(0);
        backgroundOverlay.Colour = ColourInfo.GradientVertical(dimColour, brightColour);
        backgroundOverlay.Anchor = backgroundOverlay.Origin = Anchor.BottomLeft;
    }

    public bool OnPressed(KeyBindingPressEvent<O2LazerAction> e)
    {
        if (O2LazerKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) != lookup.ColumnIndex)
            return false;

        backgroundOverlay.FadeTo(1, 50, Easing.OutQuint).Then().FadeTo(0.5f, 250, Easing.OutQuint);
        return false;
    }

    public void OnReleased(KeyBindingReleaseEvent<O2LazerAction> e)
    {
        if (O2LazerKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) == lookup.ColumnIndex)
            backgroundOverlay.FadeTo(0, 250, Easing.OutQuint);
    }
}

internal partial class O2LazerManiaDefaultColumnBackground : CompositeDrawable, IKeyBindingHandler<O2LazerAction>
{
    private readonly O2LazerSkinComponentLookup lookup;
    private Box background = null!;
    private Box backgroundOverlay = null!;
    private Color4 brightColour;
    private Color4 dimColour;

    public O2LazerManiaDefaultColumnBackground(O2LazerSkinComponentLookup lookup)
    {
        this.lookup = lookup;
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin)
    {
        InternalChildren =
        [
            background = new Box
            {
                Name = "Background",
                RelativeSizeAxes = Axes.Both,
            },
            backgroundOverlay = new Box
            {
                Name = "Background Gradient Overlay",
                RelativeSizeAxes = Axes.Both,
                Height = 0.5f,
                Blending = BlendingParameters.Additive,
                Alpha = 0,
            },
        ];

        var accent = skin.GetConfig<O2LazerSkinConfigurationLookup, Color4>(
            new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour, lookup))?.Value
            ?? Color4.Black;
        background.Colour = accent.Darken(5);
        brightColour = accent.Opacity(0.6f);
        dimColour = accent.Opacity(0);
        backgroundOverlay.Colour = ColourInfo.GradientVertical(dimColour, brightColour);
        backgroundOverlay.Anchor = backgroundOverlay.Origin = Anchor.BottomLeft;
    }

    public bool OnPressed(KeyBindingPressEvent<O2LazerAction> e)
    {
        if (O2LazerKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) != lookup.ColumnIndex)
            return false;

        backgroundOverlay.FadeTo(1, 50, Easing.OutQuint).Then().FadeTo(0.5f, 250, Easing.OutQuint);
        return false;
    }

    public void OnReleased(KeyBindingReleaseEvent<O2LazerAction> e)
    {
        if (O2LazerKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) == lookup.ColumnIndex)
            backgroundOverlay.FadeTo(0, 250, Easing.OutQuint);
    }
}

internal partial class O2LazerManiaArgonKeyArea : CompositeDrawable, IKeyBindingHandler<O2LazerAction>
{
    private readonly O2LazerSkinComponentLookup lookup;
    private Drawable background = null!;
    private Drawable hitTargetLine = null!;
    private Container<Circle> bottomIcon = null!;
    private Drawable topIcon = null!;

    public O2LazerManiaArgonKeyArea(O2LazerSkinComponentLookup lookup)
    {
        this.lookup = lookup;
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin)
    {
        const float icon_circle_size = 8;
        const float icon_spacing = 7;
        const float icon_vertical_offset = -30;

        InternalChild = new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = 110 + O2LazerManiaArgonNotePiece.CORNER_RADIUS * 2,
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            Children =
            [
                new Container
                {
                    Masking = true,
                    RelativeSizeAxes = Axes.Both,
                    CornerRadius = O2LazerManiaArgonNotePiece.CORNER_RADIUS,
                    Child = background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0,
                    },
                },
                hitTargetLine = new Circle
                {
                    RelativeSizeAxes = Axes.X,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Colour = Color4.Gray,
                    Height = O2LazerManiaArgonNotePiece.CORNER_RADIUS * 2,
                },
                bottomIcon = new Container<Circle>
                {
                    AutoSizeAxes = Axes.Both,
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.Centre,
                    Blending = BlendingParameters.Additive,
                    Y = icon_vertical_offset,
                    Children =
                    [
                        new Circle { Size = new Vector2(icon_circle_size) },
                        new Circle { X = -icon_spacing, Y = icon_spacing * 1.2f, Size = new Vector2(icon_circle_size) },
                        new Circle { X = icon_spacing, Y = icon_spacing * 1.2f, Size = new Vector2(icon_circle_size) },
                    ],
                },
                topIcon = new CircularContainer
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.Centre,
                    Y = -icon_vertical_offset,
                    Size = new Vector2(22, 14),
                    Masking = true,
                    BorderThickness = 4,
                    BorderColour = Color4.White,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                },
            ],
        };

        var accent = skin.GetConfig<O2LazerSkinConfigurationLookup, Color4>(
            new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour, lookup))?.Value
            ?? Color4.Black;
        background.Colour = accent.Darken(0.2f);
        bottomIcon.Colour = accent;
        topIcon.Colour = accent;
    }

    public bool OnPressed(KeyBindingPressEvent<O2LazerAction> e)
    {
        if (O2LazerKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) != lookup.ColumnIndex)
            return false;

        background.FadeTo(1, 70, Easing.OutQuint).Then().FadeTo(0.8f, 500);
        hitTargetLine.FadeColour(Color4.White, 70, Easing.OutQuint);
        bottomIcon.FadeColour(Color4.White, 70, Easing.OutQuint);
        topIcon.ScaleTo(0.9f, 70, Easing.OutQuint);
        return false;
    }

    public void OnReleased(KeyBindingReleaseEvent<O2LazerAction> e)
    {
        if (O2LazerKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) != lookup.ColumnIndex)
            return;

        background.FadeTo(0.3f, 50, Easing.OutQuint).Then().FadeOut(800, Easing.OutQuint);
        hitTargetLine.FadeColour(Color4.Gray, 800, Easing.OutQuint);
        bottomIcon.FadeColour(background.Colour, 800, Easing.OutQuint);
        topIcon.ScaleTo(1, 200, Easing.OutQuint);
    }
}

internal partial class O2LazerManiaDefaultKeyArea : CompositeDrawable, IKeyBindingHandler<O2LazerAction>
{
    private readonly O2LazerSkinComponentLookup lookup;
    private Container keyIcon = null!;

    public O2LazerManiaDefaultKeyArea(O2LazerSkinComponentLookup lookup)
    {
        this.lookup = lookup;
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin)
    {
        const float key_icon_size = 10;
        const float key_icon_corner_radius = 3;

        var accent = skin.GetConfig<O2LazerSkinConfigurationLookup, Color4>(
            new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour, lookup))?.Value
            ?? Color4.Black;

        InternalChild = new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = 110,
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            Children =
            [
                new Box
                {
                    Name = "Key gradient",
                    RelativeSizeAxes = Axes.Both,
                    Alpha = 0.5f,
                    Colour = Color4.Black,
                },
                keyIcon = new Container
                {
                    Size = new Vector2(key_icon_size),
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.Centre,
                    Y = 20,
                    Masking = true,
                    CornerRadius = key_icon_corner_radius,
                    BorderThickness = 2,
                    BorderColour = Color4.White,
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Glow,
                        Radius = 5,
                        Colour = accent.Opacity(0.5f),
                    },
                    Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                },
            ],
        };
    }

    public bool OnPressed(KeyBindingPressEvent<O2LazerAction> e)
    {
        if (O2LazerKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) != lookup.ColumnIndex)
            return false;

        keyIcon.ScaleTo(1.4f, 50, Easing.OutQuint).Then().ScaleTo(1.3f, 250, Easing.OutQuint);
        return false;
    }

    public void OnReleased(KeyBindingReleaseEvent<O2LazerAction> e)
    {
        if (O2LazerKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) == lookup.ColumnIndex)
            keyIcon.ScaleTo(1f, 125, Easing.OutQuint);
    }
}

internal partial class O2LazerManiaArgonHitTarget : CompositeDrawable
{
    public O2LazerManiaArgonHitTarget()
    {
        RelativeSizeAxes = Axes.X;
        Height = O2LazerManiaArgonNotePiece.NOTE_HEIGHT * O2LazerManiaArgonNotePiece.NOTE_ACCENT_RATIO;
        Masking = true;
        CornerRadius = O2LazerManiaArgonNotePiece.CORNER_RADIUS;
        Anchor = Anchor.BottomLeft;
        Origin = Anchor.BottomLeft;
        InternalChild = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Alpha = 0.3f,
            Blending = BlendingParameters.Additive,
            Colour = Color4.White,
        };
    }
}

internal partial class O2LazerManiaDefaultHitTarget : CompositeDrawable
{
    public O2LazerManiaDefaultHitTarget()
    {
        RelativeSizeAxes = Axes.X;
        Height = O2LazerManiaDefaultNotePiece.NOTE_HEIGHT;
        Anchor = Anchor.BottomLeft;
        Origin = Anchor.BottomLeft;
        InternalChildren =
        [
            new Box
            {
                RelativeSizeAxes = Axes.X,
                Height = O2LazerManiaDefaultNotePiece.NOTE_HEIGHT,
                Alpha = 0.6f,
                Colour = Color4.Black,
            },
            new Box
            {
                RelativeSizeAxes = Axes.X,
                Height = 2,
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Colour = Color4.White,
            },
        ];
    }
}
