using System;
using System.Linq;
using System.Threading;
using HarmonyLib;
using osu.Framework.Logging;

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
            try
            {
                new Harmony(harmonyId).UnpatchAll(harmonyId);
            }
            catch (Exception exception)
            {
                Interlocked.Exchange(ref failed, 1);
                Logger.Error(exception, $"O2Lazer could not roll back {harmonyId} in its Harmony runtime.");
            }

            // The BMS ruleset can carry a separate Harmony runtime in the same process.
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                                        .FirstOrDefault(candidate => candidate.GetName().Name == "osu.Game.Rulesets.BmsRuleset");
                var harmonyType = assembly?.GetType("HarmonyLib.Harmony");
                var unpatch = harmonyType?.GetMethod("UnpatchAll", [typeof(string)]);
                if (harmonyType != null && unpatch == null)
                    throw new MissingMethodException(harmonyType.FullName, "UnpatchAll");
                if (unpatch != null)
                    unpatch.Invoke(Activator.CreateInstance(harmonyType!, harmonyId), [harmonyId]);
            }
            catch (Exception exception)
            {
                Interlocked.Exchange(ref failed, 1);
                Logger.Error(exception, $"O2Lazer could not roll back {harmonyId} in BMSRuleset's Harmony runtime.");
            }
        }
    }
}
