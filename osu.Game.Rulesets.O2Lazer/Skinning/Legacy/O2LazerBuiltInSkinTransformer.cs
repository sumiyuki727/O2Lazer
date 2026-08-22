using osu.Game.Beatmaps;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Configuration;
using osu.Game.Rulesets.O2Lazer.Skinning.Drawables;
using osu.Game.Rulesets.O2Lazer.Skinning.NoteTextures;
using osu.Game.Rulesets.O2Lazer.Skinning.Runtime;
using osu.Game.Rulesets.O2Lazer.UI.HudComponents;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Legacy;

/// <inheritdoc />
/// <summary>
/// Skin transformer applied over osu! built-in skins (Argon, ArgonPro, Triangles,
/// DefaultLegacy, Retro) during O2LAZER gameplay.
/// </summary>
public partial class O2LazerBuiltInSkinTransformer(ISkin skin, IBeatmap beatmap) : SkinTransformer(skin), IO2LazerGameplaySkinDrawableSource
{
    private readonly bool isO2Jam = beatmap is O2LazerBeatmap { LayoutVariant: O2LazerLayoutVariant.O2Jam7K };

    internal O2LazerBuiltInSkinTransformer(ISkin skin)
        : this(skin, new O2LazerBeatmap { LayoutVariant = O2LazerLayoutVariant.O2Jam7K, TotalColumns = 7 })
    {
    }

    /// <inheritdoc/>
    public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
    {
        if (O2LazerDefaultHud.TryGetMainHudWithStage(lookup, () => base.GetDrawableComponent(lookup), out var mainHud))
            return mainHud;

        if (lookup is O2LazerSkinComponentLookup o2lazerLookup)
            return getDrawableFactory(o2lazerLookup)?.Create();

        return lookup is SkinComponentLookup<HitResult>
            ? null
            : O2LazerDefaultHud.GetDrawableComponent(lookup) ?? base.GetDrawableComponent(lookup);
    }

    /// <inheritdoc/>
    /// return null for every skin lookup, thus fall through to O2LazerEmbeddedSkinFallbackChain
    public override IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
    {
        if (lookup is O2LazerSkinConfigurationLookup o2lazerLookup)
        {
            if (!isO2Jam)
                return null;

            var column = o2lazerLookup.ComponentLookup?.ManiaColumnIndex ?? o2lazerLookup.ColumnIndex;
            var native = Skin.GetConfig<LegacyManiaSkinConfigurationLookup, TValue>(
                new LegacyManiaSkinConfigurationLookup(7, o2lazerLookup.Lookup, column));

            return native ?? getO2JamDefaultConfig<TValue>(o2lazerLookup, column);
        }

        return base.GetConfig<TLookup, TValue>(lookup);
    }

    O2LazerResolvedDrawableFactory? IO2LazerGameplaySkinDrawableSource.GetDrawableFactory(O2LazerSkinComponentLookup lookup) => getDrawableFactory(lookup);

    private O2LazerResolvedDrawableFactory? getDrawableFactory(O2LazerSkinComponentLookup lookup)
    {
        if (!isO2Jam)
            return null;

        if (Skin is ArgonSkin or ArgonProSkin)
        {
            return lookup.Component switch
            {
                O2LazerSkinComponents.Note => new O2LazerResolvedDrawableFactory(() => new O2LazerManiaArgonNotePiece()),
                O2LazerSkinComponents.HoldNoteHead => new O2LazerResolvedDrawableFactory(() => new O2LazerManiaArgonHoldHeadPiece()),
                O2LazerSkinComponents.HoldNoteTail => new O2LazerResolvedDrawableFactory(() => new O2LazerManiaArgonHoldTailPiece()),
                O2LazerSkinComponents.HoldNoteBody => new O2LazerResolvedDrawableFactory(() => new O2LazerManiaArgonHoldBodyPiece()),
                O2LazerSkinComponents.StageBackground => new O2LazerResolvedDrawableFactory(() => new O2LazerManiaArgonStageBackground()),
                O2LazerSkinComponents.ColumnBackground => new O2LazerResolvedDrawableFactory(() => new O2LazerManiaArgonColumnBackground(lookup)),
                O2LazerSkinComponents.KeyArea => new O2LazerResolvedDrawableFactory(() => new O2LazerManiaArgonKeyArea(lookup)),
                O2LazerSkinComponents.HitTarget => new O2LazerResolvedDrawableFactory(() => new O2LazerManiaArgonHitTarget()),
                O2LazerSkinComponents.StageForeground or O2LazerSkinComponents.ColumnLight => new O2LazerResolvedDrawableFactory(() => Drawable.Empty()),
                _ => null,
            };
        }

        if (Skin is TrianglesSkin)
        {
            return lookup.Component switch
            {
                O2LazerSkinComponents.Note or O2LazerSkinComponents.HoldNoteHead or O2LazerSkinComponents.HoldNoteTail
                    => new O2LazerResolvedDrawableFactory(() => new O2LazerManiaDefaultNotePiece()),
                O2LazerSkinComponents.HoldNoteBody
                    => new O2LazerResolvedDrawableFactory(() => new O2LazerManiaDefaultHoldBodyPiece()),
                O2LazerSkinComponents.StageBackground => new O2LazerResolvedDrawableFactory(() => new O2LazerManiaDefaultStageBackground()),
                O2LazerSkinComponents.ColumnBackground => new O2LazerResolvedDrawableFactory(() => new O2LazerManiaDefaultColumnBackground(lookup)),
                O2LazerSkinComponents.KeyArea => new O2LazerResolvedDrawableFactory(() => new O2LazerManiaDefaultKeyArea(lookup)),
                O2LazerSkinComponents.HitTarget => new O2LazerResolvedDrawableFactory(() => new O2LazerManiaDefaultHitTarget()),
                O2LazerSkinComponents.StageForeground or O2LazerSkinComponents.ColumnLight => new O2LazerResolvedDrawableFactory(() => Drawable.Empty()),
                _ => null,
            };
        }

        if (lookup.Component == O2LazerSkinComponents.HoldNoteBody)
        {
            return new O2LazerResolvedDrawableFactory(() => O2LazerLegacyTextureResolver.HasManiaKeyTexture(this)
                ? new O2LazerLegacyStretchedHoldNoteBodyPiece(this, lookup)
                : new O2LazerManiaDefaultHoldBodyPiece());
        }

        if (lookup.Component is O2LazerSkinComponents.Note or O2LazerSkinComponents.HoldNoteHead or O2LazerSkinComponents.HoldNoteTail)
        {
            var textures = O2LazerLegacyTextureResolver.ResolveNoteTextures(this, lookup);
            var widthForNoteHeightScale = GetConfig<O2LazerSkinConfigurationLookup, float>(
                new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.WidthForNoteHeightScale, lookup))?.Value;

            return new O2LazerResolvedDrawableFactory(() =>
                O2LazerLegacyTextureResolver.HasManiaKeyTexture(this) && textures.Length > 0
                    ? new O2LazerResolvedNotePiece(lookup, textures, widthForNoteHeightScale)
                    : new O2LazerManiaDefaultNotePiece());
        }

        // DefaultLegacy/Retro and other non-legacy fallback skins use mania's procedural
        // column/stage/key pieces so O2LAZER-embedded textures never override the current skin.
        if (lookup.Component is O2LazerSkinComponents.StageBackground
            or O2LazerSkinComponents.ColumnBackground
            or O2LazerSkinComponents.KeyArea
            or O2LazerSkinComponents.HitTarget
            or O2LazerSkinComponents.StageForeground
            or O2LazerSkinComponents.ColumnLight)
        {
            return lookup.Component switch
            {
                O2LazerSkinComponents.StageBackground => new O2LazerResolvedDrawableFactory(() => new O2LazerManiaDefaultStageBackground()),
                O2LazerSkinComponents.ColumnBackground => new O2LazerResolvedDrawableFactory(() => new O2LazerManiaDefaultColumnBackground(lookup)),
                O2LazerSkinComponents.KeyArea => new O2LazerResolvedDrawableFactory(() => new O2LazerManiaDefaultKeyArea(lookup)),
                O2LazerSkinComponents.HitTarget => new O2LazerResolvedDrawableFactory(() => new O2LazerManiaDefaultHitTarget()),
                _ => new O2LazerResolvedDrawableFactory(() => Drawable.Empty()),
            };
        }

        return null;
    }

    private IBindable<TValue>? getO2JamDefaultConfig<TValue>(O2LazerSkinConfigurationLookup lookup, int? column)
        where TValue : notnull
    {
        if (Skin is ArgonSkin or ArgonProSkin)
        {
            return lookup.Lookup switch
            {
                LegacyManiaSkinConfigurationLookups.LeftColumnSpacing or
                    LegacyManiaSkinConfigurationLookups.RightColumnSpacing
                    => SkinUtils.As<TValue>(new Bindable<float>(1)),
                LegacyManiaSkinConfigurationLookups.StagePaddingBottom or
                    LegacyManiaSkinConfigurationLookups.StagePaddingTop
                    => SkinUtils.As<TValue>(new Bindable<float>(30)),
                LegacyManiaSkinConfigurationLookups.ColumnWidth
                    => SkinUtils.As<TValue>(new Bindable<float>(column == 3 ? 120 : 60)),
                LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour
                    => SkinUtils.As<TValue>(new Bindable<Color4>(argonColourFor(column ?? 0))),
                _ => null,
            };
        }

        if (Skin is TrianglesSkin && lookup.Lookup == LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour)
            return SkinUtils.As<TValue>(new Bindable<Color4>(trianglesColourFor(column ?? 0)));

        if (Skin is DefaultLegacySkin or RetroSkin && lookup.Lookup == LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour)
            return SkinUtils.As<TValue>(new Bindable<Color4>(Color4.Black));

        return null;
    }

    private static Color4 argonColourFor(int column) => column switch
    {
        0 or 2 or 4 or 6 => new Color4(213, 35, 90, 255),
        1 or 5 => new Color4(252, 109, 1, 255),
        3 => new Color4(169, 106, 255, 255),
        _ => Color4.White,
    };

    private static Color4 trianglesColourFor(int column)
    {
        if (column == 3)
            return new Color4(0, 48, 63, 255);

        var distanceToEdge = System.Math.Min(System.Math.Clamp(column, 0, 6), 6 - System.Math.Clamp(column, 0, 6));
        return distanceToEdge % 2 == 0
            ? new Color4(94, 0, 57, 255)
            : new Color4(6, 84, 0, 255);
    }
}
