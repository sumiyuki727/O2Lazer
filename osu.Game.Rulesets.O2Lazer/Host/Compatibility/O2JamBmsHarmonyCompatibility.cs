using System;
using System.Reflection;

namespace osu.Game.Rulesets.O2Lazer;

/// <summary>
/// Selects the known BMS runtime for overlapping targets; lifecycle and ownership stay in the coordinator.
/// </summary>
internal static class O2JamBmsHarmonyCompatibility
{
    private const string bms_assembly_name = "osu.Game.Rulesets.BmsRuleset";

    internal static bool TryPatch(MethodInfo target, MethodInfo prefix, string harmonyId, int? priority = null) =>
        TryPatch(target, prefix, null, harmonyId, priority);

    internal static bool TryPatch(MethodInfo target, MethodInfo? prefix, MethodInfo? postfix, string harmonyId, int? priority = null) =>
        O2JamPatchCoordinator.TryPatch(bms_assembly_name, target, prefix, postfix, harmonyId, priority);

    internal static void RegisterForLateLoad(
        MethodInfo target, MethodInfo? prefix, MethodInfo? postfix, string harmonyId, int? priority = null, Action? onPatched = null) =>
        O2JamPatchCoordinator.RegisterForLateLoad(bms_assembly_name, target, prefix, postfix, harmonyId, priority, onPatched);
}
