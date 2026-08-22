using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps.Drawables;

namespace osu.Game.Rulesets.O2Lazer.UI.Icons;

public static class O2LazerDifficultyIconPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.DifficultyIcon";
    private static readonly object install_lock = new();

    public static bool IsInstalled { get; private set; }

    public static void InstallOnce()
    {
        lock (install_lock)
        {
            if (IsInstalled)
                return;

            var target = AccessTools.Method(typeof(DifficultyIcon), "getRulesetIcon");
            var prefixMethod = AccessTools.Method(typeof(O2LazerDifficultyIconPatcher), nameof(prefix));

            var missingMembers = new (string name, MemberInfo? member)[]
            {
                ("DifficultyIcon.getRulesetIcon", target),
                ("O2LazerDifficultyIconPatcher.prefix", prefixMethod),
            }.Where(m => m.member == null).Select(m => m.name).ToArray();

            if (missingMembers.Length > 0)
            {
                O2LazerLogger.Log("O2Jam DifficultyIconPatcher: Cannot install Harmony patch. Missing: " + string.Join(", ", missingMembers), level: LogLevel.Error);
                return;
            }

            try
            {
                new Harmony(harmony_id).Patch(target, prefix: new HarmonyMethod(prefixMethod));
                IsInstalled = true;
            }
            catch (Exception ex)
            {
                O2LazerLogger.Error(ex, "O2Jam DifficultyIconPatcher: Failed to install Harmony patch. Difficulty icons may use osu!'s fallback icon.");
            }
        }
    }

    // ReSharper disable InconsistentNaming
    private static bool prefix(IRulesetInfo ___ruleset, ref Drawable __result)
    {
        if (___ruleset.ShortName != Constant.SHORT_NAME)
            return true;

        // Community rulesets share the negative fallback OnlineID, so osu! cannot resolve their
        // custom icon through the ruleset store even when the score retains the correct ruleset.
        __result = new O2LazerRulesetIcon();
        return false;
    }
}
