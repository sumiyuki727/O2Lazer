using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Testing;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Objects.Drawables;
using osu.Game.Rulesets.Mania.Skinning;
using osu.Game.Rulesets.Mania.Skinning.Legacy;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.UI.Scrolling;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.Skinning;

/// <summary>
/// Extends only the blank remainder of an overlong non-stretch legacy mania body. The native
/// piece remains responsible for its first span, animation, lighting and future mania changes.
/// </summary>
internal sealed partial class O2JamLegacyHoldBodyPiece : CompositeDrawable
{
    private const float native_full_span = 32800;
    private const float extension_span = native_full_span / 2;

    private readonly Drawable nativePiece;
    private readonly Container extensionContainer;
    private readonly List<Sprite> extensionSegments = [];
    private readonly Dictionary<Texture, Texture> extensionFrames = [];
    private readonly IBindable<ScrollingDirection> direction = new Bindable<ScrollingDirection>();

    private Texture? extensionTexture;
    private Texture? animationFrame;
    private TextureAnimation? nativeBodyAnimation;
    private IBindable<double?>? missingStartTime;
    private bool canExtend;

    public O2JamLegacyHoldBodyPiece(Drawable nativePiece)
    {
        this.nativePiece = nativePiece;
        RelativeSizeAxes = nativePiece.RelativeSizeAxes;
        AutoSizeAxes = (nativePiece as CompositeDrawable)?.AutoSizeAxes ?? Axes.None;

        InternalChildren =
        [
            nativePiece,
            extensionContainer = new Container { RelativeSizeAxes = Axes.Both },
        ];
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin, IScrollingInfo scrollingInfo, Column column, StageDefinition stage, DrawableHitObject drawableObject)
    {
        direction.BindTo(scrollingInfo.Direction);
        missingStartTime = (drawableObject as DrawableHoldNote)?.MissingStartTime;

        var style = skin.GetManiaSkinConfig<LegacyManiaSkinConfiguration.LegacyNoteBodyStyle>(
                            LegacyManiaSkinConfigurationLookups.NoteBodyStyle)?.Value;
        if (style == LegacyManiaSkinConfiguration.LegacyNoteBodyStyle.Stretch)
            return;

        var imageName = skin.GetManiaSkinConfig<string>(
                                 LegacyManiaSkinConfigurationLookups.HoldNoteBodyImage, column.Index)?.Value
                        ?? $"mania-note{fallbackColumnIndex(column, stage)}L";
        var texture = skin.GetTexture(imageName, WrapMode.Repeat, WrapMode.Repeat)
                      ?? skin.GetTexture(AnimationFrameName(imageName, 0), WrapMode.Repeat, WrapMode.Repeat);
        if (texture == null)
            return;

        setExtensionTexture(texture);
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();

        // The hold-note parent owns the O2Jam visual toggle for all skin systems. Resetting
        // this legacy-only native tint avoids multiplying two grey tints together.
        if (missingStartTime?.Value != null)
            nativePiece.Colour = Colour4.White;

        synchroniseAnimationFrame();
        extensionContainer.Colour = nativePiece.Colour;
        updateExtensions();
    }

    private void synchroniseAnimationFrame()
    {
        nativeBodyAnimation ??= nativePiece.ChildrenOfType<TextureAnimation>()
                                               .FirstOrDefault(animation => animation.FrameCount > 1);
        if (nativeBodyAnimation == null)
            return;

        var frame = nativeBodyAnimation.CurrentFrame;
        if (ReferenceEquals(frame, animationFrame))
            return;

        animationFrame = frame;
        setExtensionTexture(frame);
    }

    private void setExtensionTexture(Texture texture)
    {
        if (texture.Width <= 0 || texture.Height < 2)
        {
            canExtend = false;
            return;
        }

        if (!extensionFrames.TryGetValue(texture, out var frame))
        {
            frame = texture.Crop(
                new osu.Framework.Graphics.Primitives.RectangleF(0, texture.Height / 2f, texture.Width, texture.Height / 2f),
                wrapModeS: WrapMode.Repeat,
                wrapModeT: WrapMode.Repeat);
            extensionFrames.Add(texture, frame);
        }

        extensionTexture = frame;
        canExtend = true;

        foreach (var segment in extensionSegments)
            segment.Texture = extensionTexture;
    }

    private void updateExtensions()
    {
        var heights = canExtend && O2JamRuntimeOptions.UsePercyLongNoteBodyRepeat
            ? ComputeExtensionHeights(DrawHeight)
            : [];
        var required = heights.Count;

        while (extensionSegments.Count < required)
        {
            var segment = new Sprite
            {
                Texture = extensionTexture,
                FillMode = FillMode.Stretch,
                RelativeSizeAxes = Axes.X,
            };
            extensionSegments.Add(segment);
            extensionContainer.Add(segment);
        }

        while (extensionSegments.Count > required)
        {
            extensionContainer.Remove(extensionSegments[^1], true);
            extensionSegments.RemoveAt(extensionSegments.Count - 1);
        }

        var tailAtTop = direction.Value == ScrollingDirection.Down;
        for (var index = 0; index < extensionSegments.Count; index++)
        {
            var segment = extensionSegments[index];
            var height = heights[index];
            segment.Height = height;
            segment.Scale = new Vector2(1, (tailAtTop ? 1 : -1) * extension_span / Math.Max(1, height));
            segment.Anchor = tailAtTop ? Anchor.TopCentre : Anchor.BottomCentre;
            segment.Origin = Anchor.TopCentre;
            segment.Y = tailAtTop
                ? native_full_span + index * extension_span
                : -(native_full_span + index * extension_span);
        }
    }

    internal static IReadOnlyList<float> ComputeExtensionHeights(float bodyHeight)
    {
        var remaining = bodyHeight - native_full_span;
        if (remaining <= 0)
            return [];

        var required = (int)Math.Ceiling(remaining / extension_span);
        var heights = new float[required];
        for (var index = 0; index < required; index++)
            heights[index] = Math.Min(extension_span, Math.Max(0, remaining - index * extension_span));

        return heights;
    }

    internal static string AnimationFrameName(string imageName, int frameIndex) => $"{imageName}-{frameIndex}";

    private static string fallbackColumnIndex(Column column, StageDefinition stage)
    {
        if (column.IsSpecial)
            return "S";

        var columnInStage = column.Index % stage.Columns;
        var distanceToEdge = Math.Min(columnInStage, stage.Columns - 1 - columnInStage);
        return distanceToEdge % 2 == 0 ? "1" : "2";
    }
}
