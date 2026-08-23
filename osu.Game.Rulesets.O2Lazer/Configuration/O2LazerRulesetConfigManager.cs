using osu.Framework.Configuration.Tracking;
using osu.Game.Configuration;
using osu.Game.Localisation;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.UI.Gameplay;

namespace osu.Game.Rulesets.O2Lazer.Configuration;

public class O2LazerRulesetConfigManager(SettingsStore? settings, RulesetInfo ruleset, int? variant = null)
    : RulesetConfigManager<O2LazerRulesetSetting>(settings, ruleset, variant)
{

    public const double MAX_SCROLL_SPEED = 40.0;
    public const double DEFAULT_SCROLL_SPEED = 8.0;
    public const double MIN_VISUAL_OFFSET = -500;
    public const double MAX_VISUAL_OFFSET = 500;

    protected override void InitialiseDefaults()
    {
        base.InitialiseDefaults();

        SetDefault(O2LazerRulesetSetting.LastImportPath, string.Empty);
        SetDefault(O2LazerRulesetSetting.ScrollSpeed, DEFAULT_SCROLL_SPEED, 1.0, MAX_SCROLL_SPEED, 0.1);
        SetDefault(O2LazerRulesetSetting.ConstantScrollSpeed, false);
        SetDefault(O2LazerRulesetSetting.VisualOffset, 0.0, MIN_VISUAL_OFFSET, MAX_VISUAL_OFFSET, 1.0);
        SetDefault(O2LazerRulesetSetting.AutomaticallyAdjustVisualOffset, false);
        SetDefault(O2LazerRulesetSetting.UseDedicatedPreviewAudio, true);
        SetDefault(O2LazerRulesetSetting.AutoPlayKeysounds, false);
        SetDefault(O2LazerRulesetSetting.SyncSourceFolderCollections, false);
        SetDefault(O2LazerRulesetSetting.PreviewPlayKeysounds, true);
        SetDefault(O2LazerRulesetSetting.UnlockFrameRateLimit, false);
    }

    public override TrackedSettings CreateTrackedSettings() => new()
    {
        new TrackedSetting<double>(
            O2LazerRulesetSetting.ScrollSpeed,
            speed => new SettingDescription(
                rawValue: speed,
                name: RulesetSettingsStrings.ScrollSpeed,
                value: O2LazerStrings.ScrollSpeedTooltipWithO2JamGrade(
                    RulesetSettingsStrings.ScrollSpeedTooltip((int)O2LazerGameplayScrollController.ComputeScrollTime(speed), speed),
                    O2LazerGameplayScrollController.GetO2JamSpeedGrade(speed)))),
    };
}

public enum O2LazerRulesetSetting
{
    LastImportPath,
    ScrollSpeed,
    ConstantScrollSpeed,
    VisualOffset,
    AutomaticallyAdjustVisualOffset,
    UseDedicatedPreviewAudio,
    AutoPlayKeysounds,
    SyncSourceFolderCollections,
    PreviewPlayKeysounds,
    UnlockFrameRateLimit,
}


