using System;
using System.Collections.Generic;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.O2Lazer;

internal enum O2JamPatchRequirement
{
    RequiredForGameplay,
    Optional,
}

internal readonly record struct O2JamPatchInstaller(string Name, Func<bool> Install, O2JamPatchRequirement Requirement);

internal readonly record struct O2JamPatchInstallationResult(
    IReadOnlyList<string> FailedPatches,
    IReadOnlyList<string> FailedGameplayPatches);

internal static class O2JamPatchInstallationPolicy
{
    internal static O2JamPatchInstallationResult Install(IEnumerable<O2JamPatchInstaller> installers)
    {
        var failed = new List<string>();
        var failedGameplay = new List<string>();

        foreach (var installer in installers)
        {
            var installed = false;

            try
            {
                installed = installer.Install();
            }
            catch (Exception exception)
            {
                Logger.Error(exception, $"O2Lazer could not install {installer.Name}.");
            }

            if (installed)
                continue;

            failed.Add(installer.Name);
            if (installer.Requirement == O2JamPatchRequirement.RequiredForGameplay)
                failedGameplay.Add(installer.Name);
        }

        return new O2JamPatchInstallationResult(failed.ToArray(), failedGameplay.ToArray());
    }
}
