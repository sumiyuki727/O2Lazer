using System;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Configuration;
using osu.Game.Rulesets.O2Lazer.Skinning.Drawables;
using osu.Game.Rulesets.O2Lazer.Skinning.LegacyDrawables;
using osu.Game.Rulesets.O2Lazer.Skinning.NoteTextures;
using osu.Game.Rulesets.O2Lazer.Skinning.Runtime;
using osu.Game.Rulesets.O2Lazer.UI.HudComponents;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Legacy;

/// <summary>
/// Skin transformer applied over a user-supplied or embedded skin during O2LAZER gameplay.
/// </summary>
/// <remarks>
/// Extends LegacySkinTransformer to handle O2LAZER-specific component lookups
/// (O2LazerSkinComponentLookup, hit results, and the in-ruleset HUD container).
/// <para>
/// The transformer is instantiated by O2LazerRuleset.CreateSkinTransformer for
/// every concrete Skin in the chain (user skins, unrecognised third-party skins).
/// For O2LazerEmbeddedSkin instances the ruleset returns <c>null</c> from
/// <c>CreateSkinTransformer</c>; those skins are wrapped here directly by
/// O2LazerEmbeddedSkinSource.
/// </para>
/// <para>
/// O2LAZER skin configuration (<c>skin.ini</c>) is decoded once and cached in
/// skinConfigurations. The config drives column widths, key images,
/// hit-explosion colours, and other per-column properties that are not covered by
/// the standard LegacyManiaSkinConfigurationLookup API.
/// </para>
/// </remarks>
public partial class O2LazerLegacySkinTransformer : LegacySkinTransformer, IO2LazerGameplaySkinDrawableSource
{

    /// <inheritdoc />
    /// <summary>
    /// Returns <c>true</c> if this transformer should supply legacy-style skin components.
    /// </summary>
    /// <remarks>
    /// Extends the base check (LegacySkinTransformer.IsProvidingLegacyResources
    /// = has a legacy combo font) to also return <c>true</c> when the wrapped skin provides
    /// O2LAZER-specific resources (hasO2LazerResources). This allows skins that ship
    /// mania textures without a full legacy font to still activate the O2LAZER legacy rendering path.
    /// </remarks>
    public override bool IsProvidingLegacyResources => base.IsProvidingLegacyResources || hasO2LazerResources.Value;

    internal const double HIT_EXPLOSION_FADE_IN_DURATION = 80;

    private readonly O2LazerLegacySkinConfigurationProvider configurationProvider;
    private readonly O2LazerLegacySkinResourceNames resourceNames;

    /// <summary>
    /// Lazily evaluated flag that is <c>true</c> when the wrapped skin contains
    /// at least one O2LAZER-specific resource — either a parsed <c>skin.ini</c>
    /// <c>[O2LAZER]</c> or <c>[Mania]</c> section, or a <c>mania-key1</c> / <c>mania-keyS</c>
    /// texture animation.
    /// </summary>
    /// <remarks>
    /// Used by IsProvidingLegacyResources to extend the base check to
    /// skins that have O2LAZER textures but no legacy font (which is what the base
    /// LegacySkinTransformer.IsProvidingLegacyResources checks for).
    /// Evaluated at most once per transformer instance.
    /// </remarks>
    private readonly Lazy<bool> hasO2LazerResources;

    private static readonly (HitResult Result, LegacyManiaSkinConfigurationLookups Lookup, string Filename)[] hit_result_mappings =
    [
        // O2LAZER → osu!mania legacy skin image mapping (matches LR2 / O2LAZER convention):
        (HitResult.Perfect, LegacyManiaSkinConfigurationLookups.Hit300g, "mania-hit300g"), // PGREAT
        (HitResult.Great, LegacyManiaSkinConfigurationLookups.Hit300, "mania-hit300"),     // GREAT
        (HitResult.Good, LegacyManiaSkinConfigurationLookups.Hit200, "mania-hit200"),      // GOOD
        (HitResult.Ok, LegacyManiaSkinConfigurationLookups.Hit50, "mania-hit50"),          // BAD
        (HitResult.Meh, LegacyManiaSkinConfigurationLookups.Hit0, "mania-hit0"),           // POOR (note consumed: passive miss or in-POOR-zone keypress)
        (HitResult.Miss, LegacyManiaSkinConfigurationLookups.Hit0, "mania-hit0"),          // Empty POOR (keypress with no note) — same image as POOR
    ];

    /// <param name="skin">
    /// The skin to wrap. Maybe a user skin, a third-party skin, or a
    /// O2LazerEmbeddedSkin when created directly by O2LazerEmbeddedSkinSource.
    /// </param>
    /// <param name="beatmap">
    /// The current beatmap, used to derive the O2LazerLayoutVariant (column layout).
    /// A O2LazerBeatmap is preferred; other beatmap types fall back to
    /// BeatmapInfo.Difficulty <c>CircleSize</c>.
    /// </param>
    public O2LazerLegacySkinTransformer(ISkin skin, IBeatmap beatmap)
        : base(skin)
    {
        O2LazerLayoutVariant layoutVariant1;
        if (beatmap is O2LazerBeatmap o2lazerBeatmap)
        {
            layoutVariant1 = o2lazerBeatmap.LayoutVariant;
        }
        else
        {
            layoutVariant1 = O2LazerLayout.VariantFromTotalColumns(O2LazerDifficultyInfo.GetKeyCount(beatmap.BeatmapInfo.Difficulty));
        }

        configurationProvider = new O2LazerLegacySkinConfigurationProvider(Skin, layoutVariant1);
        resourceNames = new O2LazerLegacySkinResourceNames((lookup, componentLookup, columnIndex) => GetManiaConfig<string>(lookup, componentLookup, columnIndex)?.Value);

        hasO2LazerResources = new Lazy<bool>(()
            // A parsed [O2LAZER] or [Mania] section is the strongest signal.
            => configurationProvider.HasConfigurations
               // mania-key1 is a standard mania image name — any legacy mania skin has it —
               // so it is a weak signal and is mostly redundant with base.IsProvidingLegacyResources
               // (which triggers on a legacy combo font that the same skin almost certainly ships).
               // Kept here only as a last-resort catch for skins that somehow ship mania-key1
               // without a combo font and without a skin.ini.
               || hasAnimation("mania-key1")
               // mania-keyS is a scratch-column key image; standard mania has no scratch lane,
               // so its presence unambiguously identifies a O2LAZER skin even without a skin.ini.
               || hasAnimation("mania-keyS")
        );
    }

    public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
    {
        if (O2LazerDefaultHud.TryGetMainHudWithStage(lookup, () => base.GetDrawableComponent(lookup), out var mainHud))
            return mainHud;

        var hud = O2LazerDefaultHud.GetDrawableComponent(lookup);
        if (hud != null) return hud;

        // Judgement lookups are not O2LAZER-specific lookup objects; osu! asks by HitResult. Map them
        // to O2LAZER judgement assets only when this transformer is actually active for legacy resources.
        if (lookup is SkinComponentLookup<HitResult> resultLookup && IsProvidingLegacyResources)
            return getResult(resultLookup.Component) ?? base.GetDrawableComponent(lookup);

        if (lookup is not O2LazerSkinComponentLookup o2lazerLookup)
            return base.GetDrawableComponent(lookup);

        if (!IsProvidingLegacyResources)
            return null;

        return getDrawableFactory(o2lazerLookup)?.Create();
    }

    public override IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
    {
        if (lookup is O2LazerSkinConfigurationLookup o2lazerLookup)
            return configurationProvider.GetConfig<TValue>(o2lazerLookup);

        return base.GetConfig<TLookup, TValue>(lookup);
    }

    internal IBindable<T>? GetManiaConfig<T>(LegacyManiaSkinConfigurationLookups lookup, O2LazerSkinComponentLookup? componentLookup = null, int? columnIndex = null)
        where T : notnull
        => GetConfig<O2LazerSkinConfigurationLookup, T>(new O2LazerSkinConfigurationLookup(lookup, componentLookup, columnIndex));

    internal Drawable? GetLegacyAnimation(string name) =>
        this.GetAnimation(name, WrapMode.ClampToEdge, WrapMode.ClampToEdge, true, true);

    internal string GetKeyImageName(O2LazerSkinComponentLookup lookup, bool down) =>
        resourceNames.GetKeyImageName(lookup, down);

    internal string GetHitExplosionImageName(O2LazerSkinComponentLookup lookup) =>
        resourceNames.GetHitExplosionImageName(lookup);

    internal string GetHitTargetImageName() =>
        resourceNames.GetHitTargetImageName();

    internal string[] GetStageBackgroundImageNames() =>
        resourceNames.GetStageBackgroundImageNames();

    internal string GetStageForegroundImageName() =>
        resourceNames.GetStageForegroundImageName();

    O2LazerResolvedDrawableFactory? IO2LazerGameplaySkinDrawableSource.GetDrawableFactory(O2LazerSkinComponentLookup lookup) => getDrawableFactory(lookup);

    private O2LazerResolvedDrawableFactory? getDrawableFactory(O2LazerSkinComponentLookup o2lazerLookup)
    {
        if (!IsProvidingLegacyResources)
            return null;

        // Component routing stops here. Rendering details live in the separate LegacyO2Lazer* drawables;
        // this class only decides which legacy asset family is available for the requested lookup.
        return o2lazerLookup.Component switch
        {
            O2LazerSkinComponents.Note
                => createNoteFactory(o2lazerLookup),
            O2LazerSkinComponents.ColumnBackground
                => new O2LazerResolvedDrawableFactory(() => new LegacyO2LazerColumnBackground(this, o2lazerLookup)),
            O2LazerSkinComponents.ColumnLight
                => new O2LazerResolvedDrawableFactory(() => new LegacyO2LazerColumnLight(this, o2lazerLookup)),
            O2LazerSkinComponents.HitTarget when o2lazerLookup.ColumnIndex == null
                => new O2LazerResolvedDrawableFactory(() => new LegacyO2LazerHitTarget(this)),
            O2LazerSkinComponents.KeyArea
                => new O2LazerResolvedDrawableFactory(() => new LegacyO2LazerKeyArea(this, o2lazerLookup)),
            O2LazerSkinComponents.Mine
                => createNoteFactory(o2lazerLookup),
            O2LazerSkinComponents.HitExplosion
                => new O2LazerResolvedDrawableFactory(() => new LegacyO2LazerHitExplosion(this, o2lazerLookup)),
            O2LazerSkinComponents.StageBackground
                => new O2LazerResolvedDrawableFactory(() => new LegacyO2LazerStageBackground(this)),
            O2LazerSkinComponents.StageForeground
                => new O2LazerResolvedDrawableFactory(() => new LegacyO2LazerStageForeground(this)),
            O2LazerSkinComponents.HoldNoteHead
                => createNoteFactory(o2lazerLookup),
            O2LazerSkinComponents.HoldNoteTail
                => createNoteFactory(o2lazerLookup),
            O2LazerSkinComponents.HoldNoteBody when o2lazerLookup.LayoutVariant == O2LazerLayoutVariant.O2Jam7K
                => new O2LazerResolvedDrawableFactory(() => O2LazerLegacyTextureResolver.HasManiaKeyTexture(this)
                    ? new O2LazerLegacyStretchedHoldNoteBodyPiece(this, o2lazerLookup)
                    : new O2LazerManiaDefaultHoldBodyPiece()),
            _ => null,
        };
    }

    private O2LazerResolvedDrawableFactory createNoteFactory(O2LazerSkinComponentLookup lookup)
    {
        if (lookup.LayoutVariant == O2LazerLayoutVariant.O2Jam7K && !O2LazerLegacyTextureResolver.HasManiaKeyTexture(this))
            return new O2LazerResolvedDrawableFactory(() => new O2LazerManiaDefaultNotePiece());

        var textures = O2LazerLegacyTextureResolver.ResolveNoteTextures(this, lookup);
        var widthForNoteHeightScale = GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.WidthForNoteHeightScale)?.Value;
        return new O2LazerResolvedDrawableFactory(() => new O2LazerResolvedNotePiece(lookup, textures, widthForNoteHeightScale));
    }

    private bool hasAnimation(string name) => GetLegacyAnimation(name) != null;

    private Drawable? getResult(HitResult result)
    {
        foreach (var (mappedResult, lookup, filename) in hit_result_mappings)
        {
            if (mappedResult != result)
                continue;

            var image = GetManiaConfig<string>(lookup)?.Value ?? filename;
            var animation = this.GetAnimation(image, true, true, frameLength: 1000 / 20d);
            return animation == null ? null : new LegacyO2LazerJudgementPiece(result, animation);
        }

        return null;
    }
}
