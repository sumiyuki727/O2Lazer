using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using static osu.Game.Rulesets.O2Lazer.O2JamPatchRequirement;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamPatchInstallationTest
{
    [Test]
    public void OptionalFailureDoesNotBlockGameplay()
    {
        var result = O2JamPatchInstallationPolicy.Install(
        [
            new("required", () => true, RequiredForGameplay),
            new("optional", () => false, Optional),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(result.FailedPatches, Is.EqualTo(new[] { "optional" }));
            Assert.That(result.FailedGameplayPatches, Is.Empty);
        });
    }

    [Test]
    public void FailedRequiredPatchDoesNotHideLaterFailures()
    {
        var laterInstallerRan = false;
        var result = O2JamPatchInstallationPolicy.Install(
        [
            new("required", () => false, RequiredForGameplay),
            new("optional", () =>
            {
                laterInstallerRan = true;
                return false;
            }, Optional),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(laterInstallerRan, Is.True);
            Assert.That(result.FailedPatches, Is.EqualTo(new[] { "required", "optional" }));
            Assert.That(result.FailedGameplayPatches, Is.EqualTo(new[] { "required" }));
        });
    }

    [Test]
    public void ThrownInstallerIsReportedAsFailure()
    {
        var result = O2JamPatchInstallationPolicy.Install(
        [
            new("required", () => throw new InvalidOperationException("incompatible host"), RequiredForGameplay),
        ]);

        Assert.That(result.FailedGameplayPatches, Is.EqualTo(new[] { "required" }));
    }

    [Test]
    public void RollbackRemovesAlreadyInstalledHarmonyPatch()
    {
        const string harmonyId = "osu.Game.Rulesets.O2Lazer.Tests.PatchRollback";
        var original = typeof(O2JamPatchInstallationTest).GetMethod(nameof(originalValue), BindingFlags.NonPublic | BindingFlags.Static)!;
        var postfix = typeof(O2JamPatchInstallationTest).GetMethod(nameof(replaceValue), BindingFlags.NonPublic | BindingFlags.Static)!;
        var harmonyType = O2JamPatchRollback.HarmonyType;
        var harmonyMethodType = O2JamPatchRollback.HarmonyMethodType;
        var harmony = Activator.CreateInstance(harmonyType, harmonyId)!;
        var patch = harmonyType.GetMethods().Single(method => method.Name == "Patch"
                                                               && method.GetParameters() is { Length: 5 } parameters
                                                               && parameters[0].ParameterType == typeof(MethodBase));

        try
        {
            patch.Invoke(harmony, [original, null, Activator.CreateInstance(harmonyMethodType, postfix), null, null]);
            Assert.That(originalValue(), Is.EqualTo(2));

            O2JamPatchRollback.Unpatch(harmonyId);
            Assert.That(originalValue(), Is.EqualTo(1));
        }
        finally
        {
            harmonyType.GetMethod("UnpatchAll", [typeof(string)])!.Invoke(harmony, [harmonyId]);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int originalValue() => 1;

    private static void replaceValue(ref int __result) => __result = 2;
}
