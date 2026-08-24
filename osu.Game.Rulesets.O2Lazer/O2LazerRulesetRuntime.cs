using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.Configuration;

namespace osu.Game.Rulesets.O2Lazer;

internal static class O2LazerRulesetRuntime
{
    internal static O2LazerRulesetConfigManager? ConfigManager { get; set; }

    internal static O2LazerVisualOffsetSuggestionStore VisualOffsetSuggestions { get; } = new();


    internal static bool CanAwardPerformancePoints(IReadOnlyList<Mod> mods) =>
        mods.Any(mod => mod is IO2LazerPerformanceScoringMod) && mods.All(mod => mod.Ranked);

    internal static bool SyncSourceFolderCollections =>
        ConfigManager?.Get<bool>(O2LazerRulesetSetting.SyncSourceFolderCollections) ?? false;

    internal static bool PreviewPlayKeysounds =>
        ConfigManager?.Get<bool>(O2LazerRulesetSetting.PreviewPlayKeysounds) ?? true;

    internal static bool AutoPlayKeysounds =>
        ConfigManager?.Get<bool>(O2LazerRulesetSetting.AutoPlayKeysounds) ?? false;
}







