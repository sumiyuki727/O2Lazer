using System.Linq;
using HarmonyLib;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Configuration;
using osu.Game.Localisation;
using osu.Game.Rulesets;
using osu.Game.Rulesets.O2Lazer;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.PlayerSettings;

namespace osu.Game.Rulesets.O2Lazer.UI;

/// <summary>
/// Removes beatmap-dependent presentation toggles from O2LAZER player settings surfaces,
/// including the pre-gameplay loader and replay overlay.
/// </summary>
public static class O2LazerReplaySettingsPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.ReplaySettings";

    public static bool IsInstalled { get; private set; }

    public static void InstallOnce()
    {
        if (IsInstalled)
            return;

        var visualTarget = AccessTools.Method(typeof(VisualSettings), "load", [typeof(OsuConfigManager)]);
        var audioTarget = AccessTools.Method(typeof(AudioSettings), "load", [typeof(OsuConfigManager), typeof(SessionStatics)]);
        var visualPostfix = AccessTools.Method(typeof(O2LazerReplaySettingsPatcher), nameof(visualSettingsPostfix));
        var audioPostfix = AccessTools.Method(typeof(O2LazerReplaySettingsPatcher), nameof(audioSettingsPostfix));

        var missingMembers = new (string name, object? member)[]
        {
            (name: "VisualSettings.load", member: visualTarget),
            (name: "AudioSettings.load", member: audioTarget),
            (name: "O2LazerReplaySettingsPatcher.visualSettingsPostfix", member: visualPostfix),
            (name: "O2LazerReplaySettingsPatcher.audioSettingsPostfix", member: audioPostfix),
        }.Where(m => m.member == null).Select(m => m.name).ToArray();

        if (missingMembers.Length > 0)
        {
            O2LazerLogger.Log(
                "O2LAZER replay settings patch cannot be installed. Missing: " + string.Join(", ", missingMembers) + ".",
                level: LogLevel.Error);
            return;
        }

        var harmony = new Harmony(harmony_id);
        harmony.Patch(visualTarget, postfix: new HarmonyMethod(visualPostfix));
        harmony.Patch(audioTarget, postfix: new HarmonyMethod(audioPostfix));
        IsInstalled = true;
    }

    // ReSharper disable once InconsistentNaming
    private static void visualSettingsPostfix(VisualSettings __instance)
    {
        if (!shouldTrim(__instance, __instance.Dependencies))
            return;

        trimVisualSettings(__instance);
    }

    // ReSharper disable once InconsistentNaming
    private static void audioSettingsPostfix(AudioSettings __instance)
    {
        if (!shouldTrim(__instance, __instance.Dependencies))
            return;

        trimAudioSettings(__instance);
    }

    private static void trimVisualSettings(VisualSettings settings)
    {
        var toRemove = settings.Children.Where(child =>
            (child is PlayerCheckbox checkbox
             && (checkbox.LabelText == GraphicsSettingsStrings.StoryboardVideo
                 || checkbox.LabelText == SkinSettingsStrings.BeatmapSkins
                 || checkbox.LabelText == SkinSettingsStrings.BeatmapColours))
            || (child is PlayerSliderBar<float> slider
                && slider.LabelText == GraphicsSettingsStrings.ComboColourNormalisation)).ToArray();

        foreach (var drawable in toRemove)
            settings.Remove(drawable, false);
    }

    private static void trimAudioSettings(AudioSettings settings)
    {
        var toRemove = settings.Children.Where(child =>
            child is PlayerCheckbox checkbox && checkbox.LabelText == SkinSettingsStrings.BeatmapHitsounds).ToArray();

        foreach (var drawable in toRemove)
            settings.Remove(drawable, false);
    }

    private static bool shouldTrim(PlayerSettingsGroup settings, IReadOnlyDependencyContainer dependencies)
    {
        if (dependencies.TryGet<DrawableRuleset>(out var ruleset))
            return ruleset.Ruleset.RulesetInfo.ShortName == Constant.SHORT_NAME;

        if (dependencies.TryGet<Bindable<RulesetInfo>>(out var selectedRuleset))
            return selectedRuleset.Value.ShortName == Constant.SHORT_NAME;

        if (settings.FindClosestParent<PlayerLoader>() is { } loader)
            return loader.Ruleset.Value.ShortName == Constant.SHORT_NAME;

        return false;
    }
}
