using System;
using System.Threading;
using HarmonyLib;

namespace osu.Game.Rulesets.O2Lazer;

internal static class O2JamPatchRollback
{
    private static int failed;

    internal static bool HasFailed => Volatile.Read(ref failed) != 0;
    internal static Type HarmonyType => typeof(Harmony);
    internal static Type HarmonyMethodType => typeof(HarmonyMethod);

    internal static void Unpatch(params string[] harmonyIds)
    {
        foreach (var harmonyId in harmonyIds)
        {
            if (!O2JamPatchCoordinator.Rollback(harmonyId))
                Interlocked.Exchange(ref failed, 1);
        }
    }
}
