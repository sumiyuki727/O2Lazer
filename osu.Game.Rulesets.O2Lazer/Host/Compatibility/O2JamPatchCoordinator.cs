using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.O2Lazer;

/// <summary>
/// Coordinates explicitly selected Harmony runtimes without ruleset-specific discovery.
/// Adapters choose known overlapping targets; this service does not resolve conflicting patch semantics.
/// </summary>
internal static class O2JamPatchCoordinator
{
    private static readonly Dictionary<string, HashSet<Type>> ownerRuntimes = [];
    private static readonly Dictionary<(string Owner, Type Runtime, MethodInfo Target), bool> registrations = [];
    private static readonly object sync = new();
    private static readonly List<LatePatch> latePatches = [];
    private static bool subscribedToAssemblyLoad;

    internal static bool TryPatch(string assemblyName, MethodInfo target, MethodInfo? prefix, MethodInfo? postfix, string harmonyId, int? priority = null)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
                                .FirstOrDefault(candidate => candidate.GetName().Name == assemblyName);
        return assembly != null && tryPatch(assembly, target, prefix, postfix, harmonyId, priority);
    }

    internal static void RegisterForLateLoad(
        string assemblyName, MethodInfo target, MethodInfo? prefix, MethodInfo? postfix, string harmonyId, int? priority = null, Action? onPatched = null)
    {
        lock (sync)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                                    .FirstOrDefault(candidate => candidate.GetName().Name == assemblyName);
            if (assembly != null && tryPatch(assembly, target, prefix, postfix, harmonyId, priority))
            {
                onPatched?.Invoke();
                return;
            }

            latePatches.Add(new LatePatch(assemblyName, target, prefix, postfix, harmonyId, priority, onPatched));
            if (subscribedToAssemblyLoad)
                return;

            AppDomain.CurrentDomain.AssemblyLoad += onAssemblyLoad;
            subscribedToAssemblyLoad = true;
        }
    }

    private static void unregister(string harmonyId)
    {
        lock (sync)
        {
            latePatches.RemoveAll(patch => patch.HarmonyId == harmonyId);
            unsubscribeIfIdle();
        }
    }

    internal static bool Rollback(string harmonyId)
    {
        lock (sync)
        {
            unregister(harmonyId);
            var runtimes = ownerRuntimes.TryGetValue(harmonyId, out var registered)
                ? new HashSet<Type>(registered) : [];
            runtimes.Add(typeof(Harmony));
            var succeeded = true;
            foreach (var runtime in runtimes)
            {
                try
                {
                    var unpatch = runtime.GetMethod("UnpatchAll", [typeof(string)])
                        ?? throw new MissingMethodException(runtime.FullName, "UnpatchAll");
                    unpatch.Invoke(Activator.CreateInstance(runtime, harmonyId), [harmonyId]);
                    registered?.Remove(runtime);
                    foreach (var key in registrations.Keys.Where(key => key.Owner == harmonyId && key.Runtime == runtime).ToArray())
                        registrations.Remove(key);
                }
                catch (Exception exception)
                {
                    succeeded = false;
                    Logger.Error(exception, $"O2Lazer could not roll back {harmonyId} in {runtime.Assembly.GetName().Name}.");
                }
            }

            // Retain failed registrations so a later rollback can retry the same runtime.
            if (registered?.Count == 0)
                ownerRuntimes.Remove(harmonyId);
            return succeeded;
        }
    }

    internal static IReadOnlyList<O2JamPatchDiagnostic> GetDiagnostics()
    {
        (string Owner, string Provider, Type Runtime, MethodInfo Target, O2JamPatchRegistrationState State)[] snapshot;
        lock (sync)
        {
            snapshot = registrations.Select(entry => (
                    entry.Key.Owner, entry.Key.Runtime.Assembly.GetName().Name!, entry.Key.Runtime, entry.Key.Target,
                    entry.Value ? O2JamPatchRegistrationState.Registered : O2JamPatchRegistrationState.RegistrationFailed))
                .Concat(latePatches.Select(patch => (
                    patch.HarmonyId, patch.AssemblyName, typeof(Harmony), patch.Target, O2JamPatchRegistrationState.WaitingForProvider)))
                .ToArray();
        }

        // Pending providers have no runtime to inspect yet; report the native fallback's metadata.
        return snapshot.Select(entry => O2JamPatchDiagnostic.Inspect(
            entry.Owner, entry.Provider, entry.Runtime, entry.Target, entry.State)).ToArray();
    }

    internal static void LogDiagnostics()
    {
        foreach (var diagnostic in GetDiagnostics())
            Logger.Log($"O2Lazer patch registration: owner={diagnostic.Owner}; provider={diagnostic.ProviderAssembly}; " +
                       $"runtime={diagnostic.RuntimeAssembly}; target={diagnostic.Target.DeclaringType?.FullName}.{diagnostic.Target}; " +
                       $"state={diagnostic.State}; observed owners=[{string.Join(", ", diagnostic.ObservedOwners)}]; " +
                       $"inspection error={diagnostic.InspectionError ?? "none"}", level: LogLevel.Verbose);
    }

    private static void onAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        var changed = false;
        lock (sync)
        {
            foreach (var patch in latePatches.Where(patch => patch.AssemblyName == args.LoadedAssembly.GetName().Name).ToArray())
            {
                if (!tryPatch(args.LoadedAssembly, patch.Target, patch.Prefix, patch.Postfix, patch.HarmonyId, patch.Priority))
                    continue;

                patch.OnPatched?.Invoke();
                latePatches.Remove(patch);
                changed = true;
            }

            unsubscribeIfIdle();
        }
        // A late provider changes the observed runtime after the initial installation report.
        if (changed)
            LogDiagnostics();
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
            // Packaging may rename our bundled types; foreign providers retain their own names.
            var ownRuntime = assembly == typeof(Harmony).Assembly;
            var harmonyType = ownRuntime ? typeof(Harmony) : assembly.GetType("HarmonyLib.Harmony");
            var harmonyMethodType = ownRuntime ? typeof(HarmonyMethod) : assembly.GetType("HarmonyLib.HarmonyMethod");
            if (harmonyType == null || harmonyMethodType == null)
                return false;

            var harmony = Activator.CreateInstance(harmonyType, harmonyId);
            var harmonyPrefix = prefix == null ? null : Activator.CreateInstance(harmonyMethodType, prefix);
            var harmonyPostfix = postfix == null ? null : Activator.CreateInstance(harmonyMethodType, postfix);
            if (priority != null)
            {
                var priorityField = harmonyMethodType.GetField("priority", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new MissingFieldException(harmonyMethodType.FullName, "priority");
                if (harmonyPrefix != null)
                    priorityField.SetValue(harmonyPrefix, priority.Value);
                if (harmonyPostfix != null)
                    priorityField.SetValue(harmonyPostfix, priority.Value);
            }

            var patch = harmonyType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                                   .SingleOrDefault(method => method.Name == "Patch"
                                                              && method.GetParameters() is { Length: 5 } parameters
                                                              && parameters[0].ParameterType == typeof(MethodBase));
            if (harmony == null || prefix != null && harmonyPrefix == null || postfix != null && harmonyPostfix == null || patch == null)
                return false;

            // Record before installation so a partially failing runtime can still be rolled back.
            lock (sync)
            {
                if (!ownerRuntimes.TryGetValue(harmonyId, out var runtimes))
                    ownerRuntimes.Add(harmonyId, runtimes = []);
                runtimes.Add(harmonyType);
                var registration = (harmonyId, harmonyType, target);
                registrations[registration] = false;
                patch.Invoke(harmony, [target, harmonyPrefix, harmonyPostfix, null, null]);
                registrations[registration] = true;
            }
            return true;
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not register {harmonyId} in {assembly.GetName().Name}'s Harmony runtime: {exception.Message}", level: LogLevel.Error);
            return false;
        }
    }

    private sealed record LatePatch(
        string AssemblyName, MethodInfo Target, MethodInfo? Prefix, MethodInfo? Postfix, string HarmonyId, int? Priority, Action? OnPatched);
}
