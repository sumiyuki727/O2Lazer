using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Overlays.Mods;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.O2Lazer.UI;

internal static class O2JamModSelectAttributesPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.ModSelectAttributes";
    private static readonly object installLock = new();

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
                // updateValues only schedules this compiler-generated callback; the callback
                // owns the ruleset attribute lookup that must remain scoped to mod select.
                var target = AccessTools.GetDeclaredMethods(typeof(BeatmapAttributesDisplay))
                                        .SingleOrDefault(method => method.Name.StartsWith("<updateValues>b__", StringComparison.Ordinal));
                var transpiler = AccessTools.Method(typeof(O2JamModSelectAttributesPatch), nameof(hideAttributes));
                if (target == null || transpiler == null)
                    throw new MissingMemberException("The native mod-select attribute display has changed.");

                harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                harmony.UnpatchAll(harmony_id);
                Logger.Error(exception, "O2Lazer could not adapt its mod-select difficulty attributes.");
                return false;
            }
        }
    }

    private static IEnumerable<CodeInstruction> hideAttributes(IEnumerable<CodeInstruction> instructions)
    {
        var nativeLookup = AccessTools.Method(typeof(Ruleset), nameof(Ruleset.GetBeatmapAttributesForDisplay));
        var calls = 0;

        foreach (var instruction in instructions)
        {
            if (instruction.Calls(nativeLookup))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(O2JamModSelectAttributesPatch), nameof(getModSelectAttributes));
                calls++;
            }

            yield return instruction;
        }

        if (calls != 1)
            throw new MissingMemberException("The native mod-select attribute lookup has changed.");
    }

    private static IEnumerable<RulesetBeatmapAttribute> getModSelectAttributes(Ruleset ruleset, IBeatmapInfo beatmap,
                                                                               IReadOnlyCollection<Mod> mods) =>
        GetAttributes(ruleset, beatmap, mods);

    internal static IEnumerable<RulesetBeatmapAttribute> GetAttributes(Ruleset ruleset, IBeatmapInfo beatmap,
                                                                         IReadOnlyCollection<Mod> mods)
    {
        var attributes = ruleset.GetBeatmapAttributesForDisplay(beatmap, mods);
        return ruleset.ShortName == O2LazerIdentity.ShortName ? [] : attributes;
    }
}
