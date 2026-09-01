using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Allocation;
using osu.Framework.Extensions.LocalisationExtensions;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Graphics.UserInterface;
using osu.Game.Localisation;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Screens;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.O2Lazer.UI;

internal static class O2JamEditorAccessPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.EditorAccess";
    private static readonly object installLock = new();
    private static MethodInfo? setSongSelectGetter;

    internal static bool IsInstalled { get; private set; }

    internal static bool InstallOnce()
    {
        lock (installLock)
        {
            if (IsInstalled)
                return true;

            var harmony = new Harmony(harmony_id);
            try
            {
                (MethodInfo? Target, string? Prefix, string? Postfix)[] patches =
                [
                    (AccessTools.Method(typeof(ScreenStack), "Push", [typeof(IScreen), typeof(IScreen)]), nameof(allowScreenPush), null),
                    (AccessTools.Method(typeof(SoloSongSelect), nameof(SoloSongSelect.Edit)), nameof(allowSongSelectEdit), null),
                    (AccessTools.Method(typeof(SoloSongSelect), nameof(SoloSongSelect.GetForwardActions)), null, nameof(disableForwardEdit)),
                    (AccessTools.PropertyGetter(typeof(PanelBeatmapSet), nameof(PanelBeatmapSet.ContextMenuItems)), null, nameof(disableSetEdit)),
                    (AccessTools.Method(typeof(Editor), "CreateNewDifficulty"), nameof(allowNewDifficulty), null),
                    (AccessTools.Method(typeof(Editor), nameof(Editor.SwitchToDifficulty)), nameof(allowDifficultySwitch), null),
                ];

                setSongSelectGetter = AccessTools.PropertyGetter(typeof(PanelBeatmapSet), "songSelect");
                if (patches.Any(patch => patch.Target == null) || setSongSelectGetter == null)
                    throw new MissingMethodException("The native editor entry points have changed.");

                foreach (var patch in patches)
                    harmony.Patch(patch.Target!, prefix: hook(patch.Prefix), postfix: hook(patch.Postfix));

                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                harmony.UnpatchAll(harmony_id);
                Logger.Error(exception, "O2Lazer could not install its editor access adapter.");
                return false;
            }
        }
    }

    private static HarmonyMethod? hook(string? name) => name == null ? null : new HarmonyMethod(AccessTools.Method(typeof(O2JamEditorAccessPatch), name));

    private static bool isO2Lazer(IRulesetInfo? ruleset) => ruleset?.ShortName == O2LazerIdentity.ShortName;

    private static bool blocked(OsuScreen screen, IBeatmapInfo? beatmap) =>
        isO2Lazer(screen.Ruleset?.Value) || isO2Lazer(beatmap?.Ruleset);

    private static bool reject(OsuScreen screen)
    {
        screen.Dependencies?.Get<INotificationOverlay>()?.Post(new SimpleNotification { Text = O2LazerStrings.EditorUnavailable });
        return false;
    }

    private static bool allowScreenPush(IScreen? source, IScreen newScreen)
    {
        // There is no ruleset editor capability flag. Guard before suspension/loading so
        // main-menu and shortcut routes cannot enter a loader or rewrite imported charts.
        if (newScreen is EditorLoader or Editor && source is OsuScreen screen && blocked(screen, screen.Beatmap?.Value?.BeatmapInfo))
            return reject(screen);
        return true;
    }

    private static bool allowSongSelectEdit(SoloSongSelect __instance, BeatmapInfo beatmap) =>
        !blocked(__instance, beatmap) || reject(__instance);

    private static bool allowNewDifficulty(Editor __instance, RulesetInfo rulesetInfo) =>
        !isO2Lazer(rulesetInfo) || reject(__instance);

    private static bool allowDifficultySwitch(Editor __instance, BeatmapInfo nextBeatmap) =>
        !isO2Lazer(nextBeatmap.Ruleset) || reject(__instance);

    private static void disableForwardEdit(SoloSongSelect __instance, BeatmapInfo beatmap, ref IEnumerable<OsuMenuItem> __result)
    {
        if (blocked(__instance, beatmap))
            __result = disableEditItems(__result);
    }

    private static void disableSetEdit(PanelBeatmapSet __instance, MenuItem[] __result)
    {
        if (setSongSelectGetter!.Invoke(__instance, null) is SoloSongSelect screen && blocked(screen, screen.Beatmap?.Value?.BeatmapInfo))
            foreach (var item in __result)
                disableEditItem(item);
    }

    private static IEnumerable<OsuMenuItem> disableEditItems(IEnumerable<OsuMenuItem> items)
    {
        foreach (var item in items)
        {
            disableEditItem(item);
            yield return item;
        }
    }

    private static void disableEditItem(MenuItem item)
    {
        if (item.Text.Value == ButtonSystemStrings.Edit.ToSentence())
            item.Action.Disabled = true;
    }
}
