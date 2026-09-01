using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Input.Bindings;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Filter;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Input;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.O2Lazer.Skinning;
using osu.Game.Rulesets.O2Lazer.SongSelect;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Rulesets.O2Lazer.UI.Icons;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer;

public sealed class O2LazerRuleset : Ruleset
{
    private static readonly ManiaRuleset maniaPresentation = new();

    public O2LazerRuleset()
    {
        O2JamWorkingBeatmapHook.InstallOnce();
        O2JamDifficultyIconPatch.InstallOnce();
        O2JamComboCompatibilityPatches.InstallOnce();
        O2JamSongSelectRankPatch.InstallOnce();
        O2JamBeatmapBoundaryPatches.InstallOnce();
        O2JamReplayPersistencePatch.InstallOnce();
        O2JamPerformanceEligibilityPatch.InstallOnce();
        O2JamPlayerSettingsPatch.InstallOnce();
        O2JamStarRatingDisplayPatch.InstallOnce();
        O2JamLevelSortPatch.InstallOnce();
        O2JamLevelGroupPatch.InstallOnce();
        O2JamHitSampleLookupPatch.InstallOnce();
        O2JamEditorAccessPatch.InstallOnce();
    }

    public override string Description => O2LazerStrings.RulesetName.ToString();

    public override string ShortName => O2LazerIdentity.ShortName;

    public override string RulesetAPIVersionSupported => CURRENT_RULESET_API_VERSION;

    public override IEnumerable<int> AvailableVariants => [O2LazerIdentity.O2Jam7KVariant];

    public override LocalisableString VariantDescription => O2LazerStrings.Layout;

    public override LocalisableString GetVariantName(int variant) =>
        variant == O2LazerIdentity.O2Jam7KVariant ? O2LazerStrings.SevenKeys : string.Empty;

    public override int GetVariantForBeatmap(IBeatmapInfo beatmapInfo, IReadOnlyList<Mod>? mods = null) =>
        O2LazerIdentity.O2Jam7KVariant;

    public override IEnumerable<KeyBinding> GetDefaultKeyBindings(int variant = 0) =>
        variant is 0 or O2LazerIdentity.O2Jam7KVariant ? O2LazerKeyBindings.Defaults : [];

    public override DrawableRuleset CreateDrawableRulesetWith(IBeatmap beatmap, IReadOnlyList<Mod>? mods = null) =>
        new O2JamDrawableRuleset(this, beatmap, mods);

    public override ISkin? CreateSkinTransformer(ISkin skin, IBeatmap beatmap)
    {
        var transformer = maniaPresentation.CreateSkinTransformer(skin, beatmap);
        return transformer == null ? null : O2JamSkinTransformer.WrapIfNeeded(transformer);
    }

    public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap) => new O2JamBeatmapConverter(beatmap, this);

    public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap) =>
        new O2JamDifficultyCalculator(RulesetInfo, beatmap);

    public override ScoreProcessor CreateScoreProcessor() => new O2JamScoreProcessor(this);

    public override ScoreMultiplierCalculator CreateScoreMultiplierCalculator(ScoreMultiplierContext context) =>
        new O2JamScoreMultiplierCalculator(context);

    public override HealthProcessor CreateHealthProcessor(double drainStartTime) => new O2JamHealthProcessor();

    public override IEnumerable<Mod> GetModsFor(ModType type) => type switch
    {
        ModType.DifficultyReduction =>
        [
            new O2JamModNoFail(),
            new MultiMod(new O2JamModHalfTime(), new O2JamModDaycore()),
            new O2JamModNoRelease(),
        ],
        ModType.DifficultyIncrease =>
        [
            new MultiMod(new O2JamModSuddenDeath(), new O2JamModPerfect()),
            new MultiMod(new O2JamModDoubleTime(), new O2JamModNightcore()),
            new MultiMod(new O2JamModFadeIn(), new O2JamModHidden(), new O2JamModCover()),
            new O2JamModFlashlight(),
            new O2JamModAccuracyChallenge(),
        ],
        ModType.Conversion =>
        [
            new O2JamModRandom(),
            new O2JamModMirror(),
            new O2JamModInvert(),
            new O2JamModConstantSpeed(),
            new O2JamModManiaScore(),
        ],
        ModType.Automation => [new O2JamModAutoplay()],
        ModType.Fun =>
        [
            new MultiMod(new O2JamModWindUp(), new O2JamModWindDown()),
            new O2JamModMuted(),
            new O2JamModAdaptiveSpeed(),
        ],
        _ => [],
    };

    public override IRulesetFilterCriteria CreateRulesetFilterCriteria() => new O2JamFilterCriteria();

    public override IEnumerable<RulesetBeatmapAttribute> GetBeatmapAttributesForDisplay(IBeatmapInfo beatmapInfo, IReadOnlyCollection<Mod> mods) =>
        O2JamBeatmapAttributes.Create(beatmapInfo);

    public override IEnumerable<HitResult> GetValidHitResults() =>
    [
        HitResult.Perfect,
        HitResult.Good,
        HitResult.Ok,
        HitResult.Miss,
        HitResult.IgnoreHit,
        HitResult.IgnoreMiss,
    ];

    public override LocalisableString GetDisplayNameForHitResult(HitResult result) => result switch
    {
        HitResult.Perfect => O2LazerStrings.Cool,
        HitResult.Good => O2LazerStrings.Good,
        HitResult.Ok => O2LazerStrings.Bad,
        HitResult.Miss => O2LazerStrings.Miss,
        _ => base.GetDisplayNameForHitResult(result),
    };

    public override IRulesetConfigManager CreateConfig(SettingsStore? settings) => new O2JamRulesetConfigManager(settings, RulesetInfo);

    public override RulesetSettingsSubsection CreateSettings() => new O2JamSettingsSubsection(this);

    public override Drawable CreateIcon() => new O2JamRulesetIcon();
}
