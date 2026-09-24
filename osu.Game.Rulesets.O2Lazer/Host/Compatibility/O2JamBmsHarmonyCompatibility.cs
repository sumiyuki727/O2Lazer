using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.O2Lazer;

/// <summary>
/// Keeps O2Lazer patches active when BMSRuleset brings a separate bundled Harmony runtime.
/// This is a BMS compatibility adapter, not a general patch ownership service.
/// </summary>
internal static class O2JamBmsHarmonyCompatibility
{
    private const string bms_assembly_name = "osu.Game.Rulesets.BmsRuleset";
    private static readonly object sync = new();
    private static readonly List<LatePatch> latePatches = [];
    private static bool subscribedToAssemblyLoad;

    internal static bool TryPatch(MethodInfo target, MethodInfo prefix, string harmonyId, int? priority = null) =>
        TryPatch(target, prefix, null, harmonyId, priority);

    internal static bool TryPatch(MethodInfo target, MethodInfo? prefix, MethodInfo? postfix, string harmonyId, int? priority = null)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
                                .FirstOrDefault(candidate => candidate.GetName().Name == bms_assembly_name);
        return assembly != null && tryPatch(assembly, target, prefix, postfix, harmonyId, priority);
    }

    internal static void RegisterForLateLoad(
        MethodInfo target, MethodInfo? prefix, MethodInfo? postfix, string harmonyId, int? priority = null, Action? onPatched = null)
    {
        lock (sync)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                                    .FirstOrDefault(candidate => candidate.GetName().Name == bms_assembly_name);
            if (assembly != null && tryPatch(assembly, target, prefix, postfix, harmonyId, priority))
            {
                onPatched?.Invoke();
                return;
            }

            latePatches.Add(new LatePatch(target, prefix, postfix, harmonyId, priority, onPatched));
            if (subscribedToAssemblyLoad)
                return;

            AppDomain.CurrentDomain.AssemblyLoad += onAssemblyLoad;
            subscribedToAssemblyLoad = true;
        }
    }

    internal static void Unregister(string harmonyId)
    {
        lock (sync)
        {
            latePatches.RemoveAll(patch => patch.HarmonyId == harmonyId);
            unsubscribeIfIdle();
        }
    }

    private static void onAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        if (args.LoadedAssembly.GetName().Name != bms_assembly_name)
            return;

        lock (sync)
        {
            foreach (var patch in latePatches.ToArray())
            {
                if (!tryPatch(args.LoadedAssembly, patch.Target, patch.Prefix, patch.Postfix, patch.HarmonyId, patch.Priority))
                    continue;

                patch.OnPatched?.Invoke();
                latePatches.Remove(patch);
            }

            unsubscribeIfIdle();
        }
    }

    private static void unsubscribeIfIdle()
    {
        if (!subscribedToAssemblyLoad || latePatches.Count != 0)
            return;

        AppDomain.CurrentDomain.AssemblyLoad -= onAssemblyLoad;
        subscribedToAssemblyLoad = false;
    }

    private static bool tryPatch(
        Assembly assembly, MethodInfo target, MethodInfo? prefix, MethodInfo? postfix, string harmonyId, int? priority)
    {
        try
        {
            var harmonyType = assembly.GetType("HarmonyLib.Harmony");
            var harmonyMethodType = assembly.GetType("HarmonyLib.HarmonyMethod");
            if (harmonyType == null || harmonyMethodType == null)
                return false;

            var harmony = Activator.CreateInstance(harmonyType, harmonyId);
            var harmonyPrefix = prefix == null ? null : Activator.CreateInstance(harmonyMethodType, prefix);
            var harmonyPostfix = postfix == null ? null : Activator.CreateInstance(harmonyMethodType, postfix);
            if (priority != null)
            {
                var priorityField = harmonyMethodType.GetField("priority", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (harmonyPrefix != null)
                    priorityField?.SetValue(harmonyPrefix, priority.Value);
                if (harmonyPostfix != null)
                    priorityField?.SetValue(harmonyPostfix, priority.Value);
            }

            var patch = harmonyType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                                   .SingleOrDefault(method => method.Name == "Patch"
                                                              && method.GetParameters() is { Length: 5 } parameters
                                                              && parameters[0].ParameterType == typeof(MethodBase));
            if (harmony == null || prefix != null && harmonyPrefix == null || postfix != null && harmonyPostfix == null || patch == null)
                return false;

            patch.Invoke(harmony, [target, harmonyPrefix, harmonyPostfix, null, null]);
            return true;
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not share BMSRuleset's Harmony runtime: {exception.Message}", level: LogLevel.Error);
            return false;
        }
    }

    private sealed record LatePatch(
        MethodInfo Target, MethodInfo? Prefix, MethodInfo? Postfix, string HarmonyId, int? Priority, Action? OnPatched);
}
