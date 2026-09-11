using System;
using System.Collections.Generic;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Scoring;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.UI.Icons;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Mods;

public sealed class O2JamModManiaScore : ManiaModDifficultyAdjust, IApplicableAfterBeatmapConversion, IApplicableHealthProcessor
{
    public const float DefaultDifficulty = 7;

    public override string Name => O2LazerStrings.ModManiaScoreName.ToString();

    public override string Acronym => O2LazerStrings.ModManiaScoreAcronym.ToString();

    public override LocalisableString Description => O2LazerStrings.ModManiaScoreDescription;

    public override IconUsage? Icon => O2JamModIcons.ManiaScore;

    public override ModType Type => ModType.Conversion;

    public override bool RequiresConfiguration => false;

    public override bool Ranked => UsesDefaultConfiguration;

    public override Type[] IncompatibleMods => [];

    public override string ExtendedIconInformation
    {
        get
        {
            if (!IsExactlyOneSettingChanged(OverallDifficulty, DrainRate))
                return string.Empty;

            var difficulty = OverallDifficulty.IsDefault ? DrainRate : OverallDifficulty;
            var acronym = OverallDifficulty.IsDefault
                ? O2LazerStrings.ManiaScoreHealthDrainAcronym
                : O2LazerStrings.ManiaScoreOverallDifficultyAcronym;
            return O2LazerStrings.ManiaScoreDifficultyBadge(acronym, difficulty.Value!.Value).ToString();
        }
    }

    public override IEnumerable<(LocalisableString setting, LocalisableString value)> SettingDescription
    {
        get
        {
            if (!DrainRate.IsDefault)
                yield return (O2LazerStrings.ManiaScoreHealthDrain, O2LazerStrings.ManiaScoreDifficultyValue(DrainRate.Value!.Value));

            if (!OverallDifficulty.IsDefault)
                yield return (O2LazerStrings.ManiaScoreOverallDifficulty, O2LazerStrings.ManiaScoreDifficultyValue(OverallDifficulty.Value!.Value));
        }
    }

    [SettingSource(typeof(O2LazerStrings), nameof(O2LazerStrings.ManiaScoreOverallDifficulty), nameof(O2LazerStrings.ManiaScoreOverallDifficultyDescription),
        LAST_SETTING_ORDER, SettingControlType = typeof(DifficultyAdjustSettingsControl))]
    public override DifficultyBindable OverallDifficulty { get; } = new(DefaultDifficulty)
    {
        Precision = 0.1f,
        MinValue = 0,
        MaxValue = 10,
        ExtendedMaxValue = 15,
        ExtendedMinValue = -15,
        ReadCurrentFromDifficulty = _ => DefaultDifficulty,
    };

    [SettingSource(typeof(O2LazerStrings), nameof(O2LazerStrings.ManiaScoreHealthDrain), nameof(O2LazerStrings.ManiaScoreHealthDrainDescription),
        FIRST_SETTING_ORDER, SettingControlType = typeof(DifficultyAdjustSettingsControl))]
    public new DifficultyBindable DrainRate => base.DrainRate;

    [SettingSource(typeof(O2LazerStrings), nameof(O2LazerStrings.ManiaScoreExtendedLimits), nameof(O2LazerStrings.ManiaScoreExtendedLimitsDescription))]
    public new osu.Framework.Bindables.BindableBool ExtendedLimits => base.ExtendedLimits;

    public O2JamModManiaScore()
    {
        // O2Jam has no compatible source OD/HP, so mania mode owns a stable baseline rather
        // than interpreting the chart level as either difficulty setting.
        DrainRate.Default = DefaultDifficulty;
        DrainRate.SetDefault();
        DrainRate.ReadCurrentFromDifficulty = _ => DefaultDifficulty;
    }

    void IApplicableAfterBeatmapConversion.ApplyToBeatmap(IBeatmap beatmap)
    {
        // This happens before the framework applies difficulty mods, so EZ/HR always transform
        // the MS baseline regardless of the order in which the user selected the mods.
        O2JamManiaScoreBeatmapAdapter.Apply(beatmap,
            OverallDifficulty.Value ?? DefaultDifficulty,
            DrainRate.Value ?? DefaultDifficulty);
    }

    HealthProcessor IApplicableHealthProcessor.CreateHealthProcessor(double drainStartTime) =>
        new ManiaHealthProcessor(drainStartTime);

    protected override void ApplySettings(BeatmapDifficulty difficulty)
    {
        // The settings are applied in IApplicableAfterBeatmapConversion so they precede all
        // ordinary difficulty mods. Display-only adjustment is handled by O2LazerRuleset.
    }
}
