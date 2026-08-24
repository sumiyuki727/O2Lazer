using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Input.Bindings;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Editor;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Rulesets.O2Lazer.Result;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.O2Lazer.Scoring.Judgements;
using osu.Game.Rulesets.O2Lazer.Settings;
using osu.Game.Rulesets.O2Lazer.SongSelect;
using osu.Game.Rulesets.O2Lazer.Skinning.Legacy;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Rulesets.O2Lazer.UI.Gameplay;
using osu.Game.Rulesets.O2Lazer.UI.Icons;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Filter;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Statistics;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer;

public partial class O2LazerRuleset : Ruleset, IO2LazerStyleUnrankedBadgeRuleset
{
    public override string Description => "O2Jam";

    public override string ShortName => Constant.SHORT_NAME;

    public override string RulesetAPIVersionSupported => CURRENT_RULESET_API_VERSION;

    public override IEnumerable<int> AvailableVariants => O2LazerKeyBindingConfiguration.AvailableVariants;

    public override LocalisableString VariantDescription => O2LazerStrings.Layout;

    public static readonly IReadOnlyDictionary<HitResult, string> HIT_RESULT_LABELS = new Dictionary<HitResult, string>
    {
        [HitResult.Perfect] = "COOL",
        [HitResult.Great] = "GREAT",
        [HitResult.Good] = "GOOD",
        [HitResult.Ok] = "BAD",
        [HitResult.Meh] = "MISS",
        [HitResult.Miss] = "MISS",
    };

    public static readonly IReadOnlyList<HitResult> STATIC_VALID_HIT_RESULTS =
    [
        HitResult.Perfect,
        HitResult.Good,
        HitResult.Ok,
        HitResult.Miss,
    ];

    static O2LazerRuleset()
    {
        O2LazerDifficultyIconPatcher.InstallOnce();
        O2LazerReplayPatcher.InstallOnce();
        O2LazerReplaySettingsPatcher.InstallOnce();
        O2LazerEditorPatcher.InstallOnce();
        O2LazerSongSelectLampPatcher.InstallOnce();
        O2LazerBeatmapSearchPatcher.InstallOnce();
        O2LazerModSelectDeselectAllPatcher.InstallOnce();
        O2LazerLocalLeaderboardPatcher.InstallOnce();
        O2LazerRankingHitResultColourPatcher.InstallOnce();
        O2LazerWorkingBeatmapPatcher.InstallOnce();
        O2LazerSongSelectPlayPatcher.InstallOnce();
        O2LazerRankedDisplayPatcher.InstallOnce();
        O2LazerBeatmapCompatibilityPatcher.InstallOnce();
        O2LazerDifficultyStatisticsPatcher.InstallOnce();
        O2LazerFrameStatisticsPatcher.InstallOnce();
        O2LazerComboEffectsPatcher.InstallOnce();
        O2LazerHitErrorTimeOffsetPatcher.InstallOnce();
    }

    public override ScoreMultiplierCalculator CreateScoreMultiplierCalculator(ScoreMultiplierContext context) =>
        new O2LazerScoreMultiplierCalculator(context);

    public override PerformanceCalculator? CreatePerformanceCalculator() =>
        new O2LazerPerformanceCalculator(this);

    public override DrawableRuleset CreateDrawableRulesetWith(IBeatmap beatmap, IReadOnlyList<Mod>? mods = null) =>
        new O2LazerDrawableRuleset(this, beatmap, mods);

    public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap) =>
        new O2LazerBeatmapConverter(beatmap, this);

    public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap) =>
        new O2LazerDifficultyCalculator(RulesetInfo, beatmap);

    public override LocalisableString GetVariantName(int variant) => (O2LazerLayoutVariant)variant switch
    {
        O2LazerLayoutVariant.O2Jam7K => "7K",
        _ => string.Empty,
    };

    public override int GetVariantForBeatmap(IBeatmapInfo beatmapInfo, IReadOnlyList<Mod>? mods = null)
        => (int)O2LazerLayoutVariant.O2Jam7K;

    public override IEnumerable<KeyBinding> GetDefaultKeyBindings(int variant = 0) =>
        O2LazerKeyBindingConfiguration.GetDefaultKeyBindings(variant);

    public override ScoreProcessor CreateScoreProcessor() =>
        new O2LazerScoreProcessor();

    public override HealthProcessor CreateHealthProcessor(double drainStartTime) =>
        new O2LazerHealthProcessor();

    public override StatisticItem[] CreateStatisticsForScore(ScoreInfo score, IBeatmap playableBeatmap) =>
    [
        new(O2LazerStrings.Timeline, () => new O2LazerTimelineStatistic(score, playableBeatmap), requiresHitEvents: true),
        new(O2LazerStrings.HitScatter, () => new O2LazerHitScatterStatistic(score.HitEvents, playableBeatmap), requiresHitEvents: true),
        new(O2LazerStrings.HitOffset, () => new O2LazerHitOffsetStatistic(score.HitEvents, playableBeatmap), requiresHitEvents: true),
    ];

    public override IEnumerable<Mod> GetModsFor(ModType type) => type switch
    {
        ModType.DifficultyReduction =>
        [
            new O2LazerModNoFail(),
            new MultiMod(new O2LazerModHalfTime(), new O2LazerModDaycore()),
        ],
        ModType.DifficultyIncrease =>
        [
            new MultiMod(new O2LazerModDoubleTime(), new O2LazerModNightcore()),
        ],
        ModType.Automation =>
        [
            new O2LazerModAutoplay(),
        ],
        ModType.Conversion =>
        [
            new O2LazerModRandom(),
            new O2LazerModMirror(),
        ],
        _ => [],
    };

    public override BeatmapDifficulty GetAdjustedDisplayDifficulty(IBeatmapInfo beatmapInfo, IReadOnlyCollection<Mod> mods)
    {
        var adjustedDifficulty = new BeatmapDifficulty(beatmapInfo.Difficulty);

        foreach (var mod in mods.OfType<IApplicableToDifficulty>())
            mod.ApplyToDifficulty(adjustedDifficulty);

        return adjustedDifficulty;
    }

    public override IEnumerable<RulesetBeatmapAttribute> GetBeatmapAttributesForDisplay(IBeatmapInfo beatmapInfo, IReadOnlyCollection<Mod> mods)
    {
        var original = O2LazerDifficultyInfo.FromOsuDifficulty(beatmapInfo.Difficulty);
        var adjustedDifficulty = GetAdjustedDisplayDifficulty(beatmapInfo, mods);
        var adjusted = O2LazerDifficultyInfo.FromOsuDifficulty(adjustedDifficulty);
        var colours = new OsuColour();
        var originalLevel = original.PlayLevel ?? (float)original.Total;
        var adjustedLevel = adjusted.PlayLevel ?? (float)adjusted.Total;

        yield return new RulesetBeatmapAttribute(O2LazerStrings.O2JamLevel, "LV", originalLevel, adjustedLevel, 100)
        {
            Description = O2LazerStrings.O2JamLevelDescription,
            AdditionalMetrics =
            [
                new(O2LazerStrings.Layout, "7K", colours.Blue1),
            ],
        };
    }

    public override ISkin? CreateSkinTransformer(ISkin skin, IBeatmap beatmap) => skin switch
    {
        LegacyBeatmapSkin => new O2LazerIgnoredBeatmapSkinTransformer(skin),
        ArgonSkin or ArgonProSkin or TrianglesSkin => new O2LazerBuiltInSkinTransformer(skin, beatmap),
        Skin => new O2LazerLegacySkinTransformer(skin, beatmap),
        _ => null,
    };

    public override IEnumerable<HitResult> GetValidHitResults() => STATIC_VALID_HIT_RESULTS;

    public override LocalisableString GetDisplayNameForHitResult(HitResult result) =>
        HIT_RESULT_LABELS.TryGetValue(result, out var label) ? label : base.GetDisplayNameForHitResult(result);

    public override IRulesetConfigManager CreateConfig(SettingsStore? settings)
    {
        return O2LazerRulesetRuntime.ConfigManager = new O2LazerRulesetConfigManager(settings, RulesetInfo);
    }

    public override RulesetSettingsSubsection CreateSettings() =>
        new O2LazerSettingsSubsection(this);

    public override IRulesetFilterCriteria CreateRulesetFilterCriteria() =>
        new O2LazerFilterCriteria(O2LazerRulesetRuntime.ConfigManager);

    public override Drawable CreateIcon() => new O2LazerRulesetIcon();

}




