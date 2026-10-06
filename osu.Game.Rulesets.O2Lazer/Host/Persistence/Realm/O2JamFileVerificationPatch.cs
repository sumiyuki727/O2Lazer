using System;
using System.Diagnostics;
using HarmonyLib;
using osu.Framework.Logging;
using osu.Game.Database;
using osu.Game.Models;

namespace osu.Game.Rulesets.O2Lazer.Import;

/// <summary>
/// Observes native file checks during this ruleset's writes without changing their outcome.
/// </summary>
internal static class O2JamFileVerificationPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.FileVerification";
    private static readonly object installLock = new();

    [ThreadStatic]
    private static Observation? current;

    internal static bool IsInstalled { get; private set; }

    internal static bool InstallOnce()
    {
        lock (installLock)
        {
            if (IsInstalled)
                return true;
            try
            {
                var target = AccessTools.Method(typeof(RealmFileStore), "checkFileExistsAndMatchesHash", [typeof(RealmFile)]);
                if (target?.ReturnType != typeof(bool))
                    throw new MissingMemberException("The native file verification API has changed.");
                new Harmony(harmony_id).Patch(target,
                    prefix: new HarmonyMethod(typeof(O2JamFileVerificationPatch), nameof(beginCheck)),
                    postfix: new HarmonyMethod(typeof(O2JamFileVerificationPatch), nameof(endCheck)));
                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                O2JamPatchRollback.Unpatch(harmony_id);
                Logger.Error(exception, "O2Lazer could not install its native file verification observer.");
                return false;
            }
        }
    }

    internal static Observation Observe(RealmFileStore store, string hash, Action<RealmFile> damaged, Stopwatch elapsed) =>
        new(store, hash, damaged, elapsed);

    private static void beginCheck(RealmFileStore __instance, RealmFile __0, out Observation? __state)
    {
        __state = current is { } observation && ReferenceEquals(observation.Store, __instance) && observation.Hash == __0.Hash
            ? observation : null;
        __state?.Elapsed.Start();
    }

    private static void endCheck(RealmFile __0, bool __result, Observation? __state)
    {
        if (__state == null)
            return;
        __state.Elapsed.Stop();
        try
        {
            if (!__result)
                __state.Damaged(__0);
            __state.WasObserved = true;
        }
        catch (Exception exception)
        {
            // Native repair must proceed even if observation fails; the writer then takes
            // the conservative invalidation path instead of trusting an incomplete result.
            Logger.Error(exception, "O2Lazer could not observe a native file repair.");
        }
    }

    internal sealed class Observation : IDisposable
    {
        private readonly Observation? previous;
        internal readonly RealmFileStore Store;
        internal readonly string Hash;
        internal readonly Action<RealmFile> Damaged;
        internal readonly Stopwatch Elapsed;
        internal bool WasObserved;

        internal Observation(RealmFileStore store, string hash, Action<RealmFile> damaged, Stopwatch elapsed)
        {
            Store = store;
            Hash = hash;
            Damaged = damaged;
            Elapsed = elapsed;
            previous = current;
            current = this;
        }

        public void Dispose()
        {
            Elapsed.Stop();
            current = previous;
        }
    }
}
