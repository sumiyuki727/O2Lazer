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
using osu.Framework.Utils;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.IO.Input;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Configuration;
using osu.Game.Rulesets.O2Lazer.UI.Components;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.UI.Scrolling;
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
    void SetTailAtTop(bool tailAtTop, float bodyHeight);
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
    private void load(IScrollingInfo scrollingInfo, DrawableHitObject? drawableObject)
    {
        scrollingInfo.Direction.BindValueChanged(direction =>
        {
            colouredBox.Anchor = colouredBox.Origin = direction.NewValue == ScrollingDirection.Up ? Anchor.TopCentre : Anchor.BottomCentre;
        }, true);

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

    public void SetTailAtTop(bool tailAtTop, float bodyHeight)
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
    private void load(IScrollingInfo scrollingInfo, DrawableHitObject? drawableObject)
    {
        scrollingInfo.Direction.BindValueChanged(direction =>
        {
            colouredBox.Anchor = colouredBox.Origin = direction.NewValue == ScrollingDirection.Up ? Anchor.TopCentre : Anchor.BottomCentre;
            Scale = new Vector2(1, direction.NewValue == ScrollingDirection.Up ? -1 : 1);
        }, true);

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
    private void load(IScrollingInfo scrollingInfo, DrawableHitObject? drawableObject)
    {
        scrollingInfo.Direction.BindValueChanged(direction =>
        {
            Scale = new Vector2(1, direction.NewValue == ScrollingDirection.Up ? -1 : 1);
        }, true);

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

    public void SetTailAtTop(bool tailAtTop, float bodyHeight)
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

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChild = new Box
        {
            Name = "Background",
            RelativeSizeAxes = Axes.Both,
            Colour = Color4.Black,
        };
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
    private void load(ISkinSource skin, IScrollingInfo scrollingInfo)
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

        scrollingInfo.Direction.BindValueChanged(direction =>
        {
            if (direction.NewValue == ScrollingDirection.Up)
            {
                backgroundOverlay.Anchor = backgroundOverlay.Origin = Anchor.TopLeft;
                backgroundOverlay.Colour = ColourInfo.GradientVertical(brightColour, dimColour);
            }
            else
            {
                backgroundOverlay.Anchor = backgroundOverlay.Origin = Anchor.BottomLeft;
                backgroundOverlay.Colour = ColourInfo.GradientVertical(dimColour, brightColour);
            }
        }, true);
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
    private void load(ISkinSource skin, IScrollingInfo scrollingInfo)
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

        scrollingInfo.Direction.BindValueChanged(direction =>
        {
            if (direction.NewValue == ScrollingDirection.Up)
            {
                backgroundOverlay.Anchor = backgroundOverlay.Origin = Anchor.TopLeft;
                backgroundOverlay.Colour = ColourInfo.GradientVertical(brightColour, dimColour);
            }
            else
            {
                backgroundOverlay.Anchor = backgroundOverlay.Origin = Anchor.BottomLeft;
                backgroundOverlay.Colour = ColourInfo.GradientVertical(dimColour, brightColour);
            }
        }, true);
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
    private Container directionContainer = null!;
    private Color4 accentColour;
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
    private void load(ISkinSource skin, IScrollingInfo scrollingInfo)
    {
        const float icon_circle_size = 8;
        const float icon_spacing = 7;
        const float icon_vertical_offset = -30;

        InternalChild = directionContainer = new Container
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
                    Masking = true,
                    EdgeEffect = new EdgeEffectParameters { Type = EdgeEffectType.Glow },
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
                        new Circle
                        {
                            Size = new Vector2(icon_circle_size),
                            Anchor = Anchor.BottomCentre,
                            Origin = Anchor.Centre,
                            EdgeEffect = new EdgeEffectParameters { Type = EdgeEffectType.Glow },
                        },
                        new Circle
                        {
                            X = -icon_spacing,
                            Y = icon_spacing * 1.2f,
                            Size = new Vector2(icon_circle_size),
                            Anchor = Anchor.BottomCentre,
                            Origin = Anchor.Centre,
                            EdgeEffect = new EdgeEffectParameters { Type = EdgeEffectType.Glow },
                        },
                        new Circle
                        {
                            X = icon_spacing,
                            Y = icon_spacing * 1.2f,
                            Size = new Vector2(icon_circle_size),
                            Anchor = Anchor.BottomCentre,
                            Origin = Anchor.Centre,
                            EdgeEffect = new EdgeEffectParameters { Type = EdgeEffectType.Glow },
                        },
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
                    EdgeEffect = new EdgeEffectParameters { Type = EdgeEffectType.Glow },
                    Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                },
            ],
        };

        accentColour = skin.GetConfig<O2LazerSkinConfigurationLookup, Color4>(
            new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour, lookup))?.Value
            ?? Color4.Black;
        background.Colour = accentColour.Darken(0.2f);
        bottomIcon.Colour = accentColour;
        topIcon.Colour = accentColour;

        scrollingInfo.Direction.BindValueChanged(direction =>
        {
            if (direction.NewValue == ScrollingDirection.Up)
            {
                directionContainer.Scale = new Vector2(1, -1);
                directionContainer.Anchor = Anchor.TopLeft;
                directionContainer.Origin = Anchor.BottomLeft;
            }
            else
            {
                directionContainer.Scale = Vector2.One;
                directionContainer.Anchor = Anchor.BottomLeft;
                directionContainer.Origin = Anchor.BottomLeft;
            }
        }, true);
    }

    public bool OnPressed(KeyBindingPressEvent<O2LazerAction> e)
    {
        if (O2LazerKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) != lookup.ColumnIndex)
            return false;

        const double lighting_fade_in_duration = 70;
        var lightingColour = getLightingColour();

        background
            .FlashColour(accentColour.Lighten(0.8f), 200, Easing.OutQuint)
            .FadeTo(1, lighting_fade_in_duration, Easing.OutQuint)
            .Then()
            .FadeTo(0.8f, 500);

        hitTargetLine.FadeColour(Color4.White, lighting_fade_in_duration, Easing.OutQuint);
        hitTargetLine.TransformTo(nameof(EdgeEffect), new EdgeEffectParameters
        {
            Type = EdgeEffectType.Glow,
            Colour = lightingColour.Opacity(0.4f),
            Radius = 20,
        }, lighting_fade_in_duration, Easing.OutQuint);

        topIcon.ScaleTo(0.9f, lighting_fade_in_duration, Easing.OutQuint);
        topIcon.TransformTo(nameof(EdgeEffect), new EdgeEffectParameters
        {
            Type = EdgeEffectType.Glow,
            Colour = lightingColour.Opacity(0.1f),
            Radius = 20,
        }, lighting_fade_in_duration, Easing.OutQuint);

        bottomIcon.FadeColour(Color4.White, lighting_fade_in_duration, Easing.OutQuint);

        foreach (var circle in bottomIcon)
        {
            circle.TransformTo(nameof(EdgeEffect), new EdgeEffectParameters
            {
                Type = EdgeEffectType.Glow,
                Colour = lightingColour.Opacity(0.2f),
                Radius = 60,
            }, lighting_fade_in_duration, Easing.OutQuint);
        }

        return false;
    }

    public void OnReleased(KeyBindingReleaseEvent<O2LazerAction> e)
    {
        if (O2LazerKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) != lookup.ColumnIndex)
            return;

        const double lighting_fade_out_duration = 800;
        var lightingColour = getLightingColour().Opacity(0);

        background.FadeTo(0.3f, 50, Easing.OutQuint).Then().FadeOut(800, Easing.OutQuint);
        hitTargetLine.FadeColour(Color4.Gray, 800, Easing.OutQuint);
        bottomIcon.FadeColour(accentColour, 800, Easing.OutQuint);
        topIcon.ScaleTo(1, 200, Easing.OutQuint);

        hitTargetLine.TransformTo(nameof(EdgeEffect), new EdgeEffectParameters
        {
            Type = EdgeEffectType.Glow,
            Colour = lightingColour,
            Radius = 25,
        }, lighting_fade_out_duration, Easing.OutQuint);

        topIcon.TransformTo(nameof(EdgeEffect), new EdgeEffectParameters
        {
            Type = EdgeEffectType.Glow,
            Colour = lightingColour,
            Radius = 20,
        }, lighting_fade_out_duration, Easing.OutQuint);

        foreach (var circle in bottomIcon)
        {
            circle.TransformTo(nameof(EdgeEffect), new EdgeEffectParameters
            {
                Type = EdgeEffectType.Glow,
                Colour = lightingColour,
                Radius = 30,
            }, lighting_fade_out_duration, Easing.OutQuint);
        }
    }

    private Color4 getLightingColour() => Interpolation.ValueAt(0.2f, accentColour, Color4.White, 0, 1);
}

internal partial class O2LazerManiaDefaultKeyArea : CompositeDrawable, IKeyBindingHandler<O2LazerAction>
{
    private readonly O2LazerSkinComponentLookup lookup;
    private Container directionContainer = null!;
    private Drawable gradient = null!;
    private Container keyIcon = null!;

    public O2LazerManiaDefaultKeyArea(O2LazerSkinComponentLookup lookup)
    {
        this.lookup = lookup;
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin, IScrollingInfo scrollingInfo)
    {
        const float key_icon_size = 10;
        const float key_icon_corner_radius = 3;

        var accent = skin.GetConfig<O2LazerSkinConfigurationLookup, Color4>(
            new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour, lookup))?.Value
            ?? Color4.Black;

        InternalChild = directionContainer = new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = 110,
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            Children =
            [
                gradient = new Box
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

        scrollingInfo.Direction.BindValueChanged(direction =>
        {
            if (direction.NewValue == ScrollingDirection.Up)
            {
                keyIcon.Anchor = Anchor.BottomCentre;
                keyIcon.Y = -20;
                directionContainer.Anchor = directionContainer.Origin = Anchor.TopLeft;
                gradient.Colour = ColourInfo.GradientVertical(Color4.Black, Color4.Black.Opacity(0));
            }
            else
            {
                keyIcon.Anchor = Anchor.TopCentre;
                keyIcon.Y = 20;
                directionContainer.Anchor = directionContainer.Origin = Anchor.BottomLeft;
                gradient.Colour = ColourInfo.GradientVertical(Color4.Black.Opacity(0), Color4.Black);
            }
        }, true);
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

    [BackgroundDependencyLoader]
    private void load(IScrollingInfo scrollingInfo)
    {
        scrollingInfo.Direction.BindValueChanged(direction =>
        {
            Anchor = Origin = direction.NewValue == ScrollingDirection.Up ? Anchor.TopLeft : Anchor.BottomLeft;
        }, true);
    }
}

internal partial class O2LazerManiaDefaultHitTarget : CompositeDrawable
{
    private readonly O2LazerSkinComponentLookup lookup;
    private Box hitTargetBar = null!;
    private Container hitTargetLine = null!;

    public O2LazerManiaDefaultHitTarget(O2LazerSkinComponentLookup lookup)
    {
        this.lookup = lookup;

        RelativeSizeAxes = Axes.X;
        Height = O2LazerManiaDefaultNotePiece.NOTE_HEIGHT;
        Anchor = Anchor.BottomLeft;
        Origin = Anchor.BottomLeft;
        InternalChildren =
        [
            hitTargetBar = new Box
            {
                RelativeSizeAxes = Axes.X,
                Height = O2LazerManiaDefaultNotePiece.NOTE_HEIGHT,
                Alpha = 0.6f,
                Colour = Color4.Black,
            },
            hitTargetLine = new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 2,
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Masking = true,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.White,
                },
            },
        ];
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin, IScrollingInfo scrollingInfo)
    {
        var accent = skin.GetConfig<O2LazerSkinConfigurationLookup, Color4>(
            new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour, lookup)
        )?.Value ?? Color4.Black;

        hitTargetLine.EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Glow,
            Radius = 5,
            Colour = accent.Opacity(0.5f),
        };

        scrollingInfo.Direction.BindValueChanged(direction =>
        {
            Anchor = Origin = direction.NewValue == ScrollingDirection.Up ? Anchor.TopLeft : Anchor.BottomLeft;
            hitTargetBar.Anchor = hitTargetBar.Origin = direction.NewValue == ScrollingDirection.Up ? Anchor.TopLeft : Anchor.BottomLeft;
            hitTargetLine.Anchor = hitTargetLine.Origin = direction.NewValue == ScrollingDirection.Up ? Anchor.TopLeft : Anchor.BottomLeft;
        }, true);
    }
}

internal partial class O2LazerManiaArgonHitExplosion : CompositeDrawable, IO2LazerHitExplosion
{
    public override bool RemoveWhenNotAlive => true;

    private readonly O2LazerSkinComponentLookup lookup;
    private Container largeFaint = null!;

    public O2LazerManiaArgonHitExplosion(O2LazerSkinComponentLookup lookup)
    {
        this.lookup = lookup;

        Origin = Anchor.Centre;
        Anchor = Anchor.BottomCentre;
        Y = -O2LazerManiaArgonNotePiece.NOTE_HEIGHT / 2;

        RelativeSizeAxes = Axes.X;
        Height = O2LazerManiaArgonNotePiece.NOTE_HEIGHT;
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin, IScrollingInfo scrollingInfo)
    {
        InternalChildren =
        [
            largeFaint = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Height = O2LazerManiaArgonNotePiece.NOTE_ACCENT_RATIO,
                Masking = true,
                CornerRadius = O2LazerManiaArgonNotePiece.CORNER_RADIUS,
                Blending = BlendingParameters.Additive,
                Child = new Box
                {
                    Colour = Color4.White,
                    RelativeSizeAxes = Axes.Both,
                },
            },
        ];

        var accent = skin.GetConfig<O2LazerSkinConfigurationLookup, Color4>(
            new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour, lookup)
        )?.Value ?? Color4.Black;

        largeFaint.Colour = Interpolation.ValueAt(0.8f, accent, Color4.White, 0, 1);
        largeFaint.EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Glow,
            Colour = accent,
            Roundness = 40,
            Radius = 60,
        };

        scrollingInfo.Direction.BindValueChanged(direction =>
        {
            if (direction.NewValue == ScrollingDirection.Up)
            {
                Anchor = Anchor.TopCentre;
                largeFaint.Anchor = largeFaint.Origin = Anchor.TopCentre;
                Y = O2LazerManiaArgonNotePiece.NOTE_HEIGHT / 2;
            }
            else
            {
                Anchor = Anchor.BottomCentre;
                largeFaint.Anchor = largeFaint.Origin = Anchor.BottomCentre;
                Y = -O2LazerManiaArgonNotePiece.NOTE_HEIGHT / 2;
            }
        }, true);
    }

    public void Animate(JudgementResult result) => this.FadeOutFromOne(O2LazerHitExplosion.DURATION, Easing.Out);
}

internal partial class O2LazerManiaDefaultHitExplosion : CompositeDrawable, IO2LazerHitExplosion
{
    private const float default_large_faint_size = 0.8f;

    public override bool RemoveWhenNotAlive => true;

    private readonly O2LazerSkinComponentLookup lookup;
    private CircularContainer largeFaint = null!;
    private CircularContainer mainGlow1 = null!;
    private CircularContainer mainGlow2 = null!;
    private CircularContainer mainGlow3 = null!;

    public O2LazerManiaDefaultHitExplosion(O2LazerSkinComponentLookup lookup)
    {
        this.lookup = lookup;

        Origin = Anchor.Centre;
        Anchor = Anchor.BottomCentre;
        Y = -O2LazerManiaDefaultNotePiece.NOTE_HEIGHT / 2;

        RelativeSizeAxes = Axes.X;
        Height = O2LazerManiaDefaultNotePiece.NOTE_HEIGHT;
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin, IScrollingInfo scrollingInfo)
    {
        const float angle_variance = 15;
        const float roundness = 80;
        const float initial_height = 10;

        InternalChildren =
        [
            largeFaint = new CircularContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                Size = new Vector2(default_large_faint_size),
                Blending = BlendingParameters.Additive,
            },
            mainGlow1 = new CircularContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                Blending = BlendingParameters.Additive,
            },
            mainGlow2 = new CircularContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                Size = new Vector2(0.01f, initial_height),
                Blending = BlendingParameters.Additive,
                Rotation = RNG.NextSingle(-angle_variance, angle_variance),
            },
            mainGlow3 = new CircularContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                Size = new Vector2(0.01f, initial_height),
                Blending = BlendingParameters.Additive,
                Rotation = RNG.NextSingle(-angle_variance, angle_variance),
            },
        ];

        var accent = skin.GetConfig<O2LazerSkinConfigurationLookup, Color4>(
            new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour, lookup)
        )?.Value ?? Color4.Black;

        largeFaint.EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Glow,
            Colour = Interpolation.ValueAt(0.1f, accent, Color4.White, 0, 1).Opacity(0.3f),
            Roundness = 160,
            Radius = 200,
        };
        mainGlow1.EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Glow,
            Colour = Interpolation.ValueAt(0.6f, accent, Color4.White, 0, 1),
            Roundness = 20,
            Radius = 50,
        };
        mainGlow2.EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Glow,
            Colour = Interpolation.ValueAt(0.4f, accent, Color4.White, 0, 1),
            Roundness = roundness,
            Radius = 40,
        };
        mainGlow3.EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Glow,
            Colour = Interpolation.ValueAt(0.4f, accent, Color4.White, 0, 1),
            Roundness = roundness,
            Radius = 40,
        };

        scrollingInfo.Direction.BindValueChanged(direction =>
        {
            if (direction.NewValue == ScrollingDirection.Up)
            {
                Anchor = Anchor.TopCentre;
                Y = O2LazerManiaDefaultNotePiece.NOTE_HEIGHT / 2;
            }
            else
            {
                Anchor = Anchor.BottomCentre;
                Y = -O2LazerManiaDefaultNotePiece.NOTE_HEIGHT / 2;
            }
        }, true);
    }

    public void Animate(JudgementResult result)
    {
        Vector2 scale = new Vector2(1, 0.6f);

        this.ScaleTo(scale);

        largeFaint
            .ResizeTo(default_large_faint_size)
            .Then()
            .ResizeTo(default_large_faint_size * new Vector2(5, 1), O2LazerHitExplosion.DURATION, Easing.OutQuint)
            .FadeOut(O2LazerHitExplosion.DURATION * 2);

        mainGlow1
            .ScaleTo(1)
            .Then()
            .ScaleTo(1.4f, O2LazerHitExplosion.DURATION, Easing.OutQuint);

        this.FadeOutFromOne(O2LazerHitExplosion.DURATION, Easing.Out);
    }
}
