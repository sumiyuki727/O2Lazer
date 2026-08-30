using osu.Framework.Configuration.Tracking;
using osu.Game.Configuration;
using osu.Game.Localisation;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Mania.Configuration;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.O2Lazer.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.UI;

namespace osu.Game.Rulesets.O2Lazer.Configuration;

public sealed class O2JamRulesetConfigManager : RulesetConfigManager<O2JamRulesetSetting>
{
    public const double MinimumScrollSpeed = 1;
    public const double MaximumScrollSpeed = 40;
    public const double DefaultScrollSpeed = 8;

    public O2JamRulesetConfigManager(SettingsStore? settings, RulesetInfo ruleset, int? variant = null)
        : base(settings, ruleset, variant)
    {
        // GetBindable() returns a weakly bound copy. Subscribe to the owned bindable so GC
        // cannot silently detach the runtime projection while the settings UI still updates.
        GetOriginalBindable<bool>(O2JamRulesetSetting.O2JamStyleDroppedHold).BindValueChanged(
            value => O2JamRuntimeOptions.UseO2JamLongNoteMissVisual = value.NewValue, true);
        GetOriginalBindable<bool>(O2JamRulesetSetting.PercyLongNoteBodyRepeat).BindValueChanged(
            value => O2JamRuntimeOptions.UsePercyLongNoteBodyRepeat = value.NewValue, true);
    }

    protected override void InitialiseDefaults()
    {
        base.InitialiseDefaults();

        SetDefault(O2JamRulesetSetting.LastImportPath, string.Empty);
        SetDefault(O2JamRulesetSetting.ScrollSpeed, DefaultScrollSpeed, MinimumScrollSpeed, MaximumScrollSpeed, 0.1);
        SetDefault(O2JamRulesetSetting.ScrollDirection, ManiaScrollingDirection.Down);
        SetDefault(O2JamRulesetSetting.ConstantScrollSpeed, false);
        SetDefault(O2JamRulesetSetting.SyncSourceFolderCollections, false);
        SetDefault(O2JamRulesetSetting.O2JamStyleDroppedHold, false);
        SetDefault(O2JamRulesetSetting.PercyLongNoteBodyRepeat, false);
        SetDefault(O2JamRulesetSetting.MetadataEncoding, OjnMetadataEncoding.Automatic);
    }

    public override TrackedSettings CreateTrackedSettings() => new()
    {
        new TrackedSetting<double>(
            O2JamRulesetSetting.ScrollSpeed,
            speed => new SettingDescription(
                rawValue: speed,
                name: RulesetSettingsStrings.ScrollSpeed,
                value: O2LazerStrings.ScrollSpeedTooltipWithO2JamGrade(
                    RulesetSettingsStrings.ScrollSpeedTooltip((int)O2JamDrawableRuleset.ComputeScrollTime(speed), speed),
                    O2JamDrawableRuleset.GetO2JamSpeedMultiplier(speed)))),
    };
}

public enum O2JamRulesetSetting
{
    // These names intentionally match the previous release so Realm-backed user preferences migrate in place.
    LastImportPath,
    ScrollSpeed,
    ScrollDirection,
    ConstantScrollSpeed,
    SyncSourceFolderCollections,
    O2JamStyleDroppedHold,
    PercyLongNoteBodyRepeat,
    MetadataEncoding,
}
