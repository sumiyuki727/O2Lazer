using System.Linq;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Input.Events;
using osu.Framework.Logging;
using osu.Game.Input.Bindings;
using osu.Game.Overlays.Mods;

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

public static class O2LazerModSelectDeselectAllPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.ModSelectDeselectAll";

    private static readonly object install_lock = new();
    private static FieldInfo? customisationPanelField;

    public static bool IsInstalled { get; private set; }

    public static void InstallOnce()
    {
        lock (install_lock)
        {
            if (IsInstalled)
                return;

            var target = AccessTools.Method(
                typeof(ModSelectOverlay),
                nameof(ModSelectOverlay.OnPressed),
                [typeof(KeyBindingPressEvent<GlobalAction>)]);
            var prefixMethod = AccessTools.Method(typeof(O2LazerModSelectDeselectAllPatcher), nameof(prefix));
            customisationPanelField = AccessTools.Field(typeof(ModSelectOverlay), "customisationPanel");

            var missing = new[]
            {
                (name: "ModSelectOverlay.OnPressed", member: (MemberInfo?)target),
                (name: "O2LazerModSelectDeselectAllPatcher.prefix", member: prefixMethod),
            }.Where(m => m.member == null).Select(m => m.name).ToArray();

            if (missing.Length > 0)
            {
                O2LazerLogger.Log("O2LAZER ModSelectDeselectAllPatcher: Cannot install Harmony patch. Missing: " + string.Join(", ", missing), level: LogLevel.Error);
                return;
            }

            new Harmony(harmony_id).Patch(target, prefix: new HarmonyMethod(prefixMethod));
            IsInstalled = true;
        }
    }

    // ReSharper disable once InconsistentNaming
    private static bool prefix(ModSelectOverlay __instance, KeyBindingPressEvent<GlobalAction> e, ref bool __result)
    {
        if (e.Repeat || e.Action != GlobalAction.DeselectAllMods)
            return true;

        // Typing in the search box owns Backspace; clearing mods must not fight it.
        if (__instance.SearchTextBox.HasFocus)
            return true;

        // When the customisation panel is expanded, Backspace is reserved for collapsing it first.
        if (customisationPanelField?.GetValue(__instance) is ModCustomisationPanel panel)
        {
            if (panel.ExpandedState.Value != ModCustomisationPanel.ModCustomisationPanelState.Collapsed)
                return true;
        }

        __instance.DeselectAll();
        __result = true;
        return false;
    }
    // ReSharper restore InconsistentNaming
}
