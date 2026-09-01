using System;
using System.Linq;
using HarmonyLib;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Game.Localisation;
using osu.Game.Overlays.Settings;
using osu.Game.Screens;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.PlayerSettings;

namespace osu.Game.Rulesets.O2Lazer.UI;

internal static class O2JamPlayerSettingsPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.PlayerSettings";
    private static readonly object installLock = new();

    internal static bool IsInstalled { get; private set; }

    internal static bool InstallOnce()
    {
        lock (installLock)
        {
            if (IsInstalled)
                return true;

            try
            {
                var target = AccessTools.Constructor(typeof(PlayerSettingsGroup), [typeof(LocalisableString)]);
                var postfix = AccessTools.Method(typeof(O2JamPlayerSettingsPatch), nameof(attachVisibility));
                if (target == null || postfix == null)
                    throw new MissingMethodException("The native player settings group constructor is unavailable.");

                new Harmony(harmony_id).Patch(target, postfix: new HarmonyMethod(postfix));
                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "O2Lazer could not install its player settings visibility adapter.");
                return false;
            }
        }
    }

    private static void attachVisibility(PlayerSettingsGroup __instance)
    {
        // osu! constructs these groups directly, without a ruleset factory. Its native load
        // event gives us both the final controls and their owning screen with just one hook.
        if (__instance is VisualSettings or AudioSettings or InputSettings)
            __instance.OnLoadComplete += applyToLoadedGroup;
    }

    private static void applyToLoadedGroup(Drawable drawable) =>
        Apply((PlayerSettingsGroup)drawable, drawable.FindClosestParent<OsuScreen>());

    internal static void Apply(PlayerSettingsGroup group, OsuScreen? screen)
    {
        var ruleset = screen switch
        {
            // A replay loader may not yet have applied its score's ruleset to the screen lease.
            ReplayPlayerLoader replay => replay.Score.Ruleset,
            PlayerLoader loader => loader.Ruleset?.Value,
            ReplayPlayer replay => replay.GameplayState?.Ruleset.RulesetInfo ?? replay.Ruleset?.Value,
            _ => null,
        };

        if (ruleset?.ShortName != O2LazerIdentity.ShortName)
            return;

        foreach (var child in group.Children)
        {
            if (child is PlayerCheckbox checkbox && isUnsupportedCheckbox(group, checkbox.LabelText))
                hide(checkbox);
            else if (group is VisualSettings && child is PlayerSliderBar<float> slider
                     && slider.LabelText == GraphicsSettingsStrings.ComboColourNormalisation)
                hide(slider);
        }

        if (!group.Children.Any(child => child.IsPresent))
            group.Hide();
    }

    private static bool isUnsupportedCheckbox(PlayerSettingsGroup group, LocalisableString label) => group switch
    {
        VisualSettings => label == GraphicsSettingsStrings.StoryboardVideo
                          || label == SkinSettingsStrings.BeatmapSkins
                          || label == SkinSettingsStrings.BeatmapColours,
        AudioSettings => label == SkinSettingsStrings.BeatmapHitsounds,
        InputSettings => label == MouseSettingsStrings.DisableClicksDuringGameplay
                         || label == TouchSettingsStrings.DisableTapsDuringGameplay,
        _ => false,
    };

    private static void hide<T>(SettingsItem<T> setting)
    {
        setting.CanBeShown.Value = false;
        // PlayerSettingsGroup has no search filter to project CanBeShown into layout presence.
        setting.MatchingFilter = false;
    }
}
