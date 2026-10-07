using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
public class O2JamPatchCoordinatorTest
{
    private static string runtimeAssembly => O2JamPatchRollback.HarmonyType.Assembly.GetName().Name!;
    private static MethodInfo target => typeof(O2JamPatchCoordinatorTest).GetMethod(nameof(value), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static MethodInfo postfix(string name) => typeof(O2JamPatchCoordinatorTest).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    [Test]
    public void RollbackPreservesOtherOwnersOnTheSameTarget()
    {
        var first = "O2Lazer.Tests.First." + Guid.NewGuid();
        var second = "O2Lazer.Tests.Second." + Guid.NewGuid();
        try
        {
            Assert.That(O2JamPatchCoordinator.TryPatch(runtimeAssembly, target, null, postfix(nameof(addOne)), first), Is.True);
            Assert.That(O2JamPatchCoordinator.TryPatch(runtimeAssembly, target, null, postfix(nameof(addTen)), second), Is.True);
            var snapshot = O2JamPatchCoordinator.GetDiagnostics().Single(item => item.Owner == first);
            Assert.That(snapshot.InspectionError, Is.Null);
            Assert.That(snapshot.ObservedOwners, Does.Contain(first).And.Contain(second));
            Assert.That(value(), Is.EqualTo(12));
            Assert.That(O2JamPatchCoordinator.Rollback(first), Is.True);
            Assert.That(value(), Is.EqualTo(11));
            Assert.That(O2JamPatchCoordinator.Rollback(first), Is.True);
            Assert.That(value(), Is.EqualTo(11));
            Assert.That(O2JamPatchCoordinator.Rollback(second), Is.True);
            Assert.That(value(), Is.EqualTo(1));
        }
        finally
        {
            O2JamPatchCoordinator.Rollback(first);
            O2JamPatchCoordinator.Rollback(second);
        }
    }

    [Test]
    public void LateRegistrationHandlesAnAlreadyLoadedProvider()
    {
        var owner = "O2Lazer.Tests.Loaded." + Guid.NewGuid();
        var calls = 0;
        try
        {
            O2JamPatchCoordinator.RegisterForLateLoad(runtimeAssembly, target, null, postfix(nameof(addOne)), owner, onPatched: () => calls++);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(value(), Is.EqualTo(2));
        }
        finally
        {
            Assert.That(O2JamPatchCoordinator.Rollback(owner), Is.True);
        }
        Assert.That(value(), Is.EqualTo(1));
    }

    [Test]
    public void UnknownProviderDoesNotFallBackToAnotherRuntime()
    {
        Assert.That(O2JamPatchCoordinator.TryPatch("Missing." + Guid.NewGuid(), target, null, postfix(nameof(addOne)), "O2Lazer.Tests.Unknown"), Is.False);
        Assert.That(value(), Is.EqualTo(1));
    }

    [Test]
    public void PendingProviderIsReportedAndRemovedOnRollback()
    {
        var owner = "O2Lazer.Tests.Pending." + Guid.NewGuid();
        var provider = "Missing." + Guid.NewGuid();
        try
        {
            O2JamPatchCoordinator.RegisterForLateLoad(provider, target, null, postfix(nameof(addOne)), owner);
            var diagnostic = O2JamPatchCoordinator.GetDiagnostics().Single(item => item.Owner == owner);
            Assert.That(diagnostic.State, Is.EqualTo(O2JamPatchRegistrationState.WaitingForProvider));
            Assert.That(diagnostic.ProviderAssembly, Is.EqualTo(provider));
            Assert.That(diagnostic.InspectionError, Is.Null);
            Assert.That(value(), Is.EqualTo(1));
        }
        finally
        {
            O2JamPatchCoordinator.Rollback(owner);
        }
        Assert.That(O2JamPatchCoordinator.GetDiagnostics().Any(item => item.Owner == owner), Is.False);
    }

    [Test]
    public void UnsupportedInspectionProducesEvidenceInsteadOfThrowing()
    {
        var diagnostic = O2JamPatchDiagnostic.Inspect("owner", "unsupported", typeof(object), target, O2JamPatchRegistrationState.Registered);
        Assert.That(diagnostic.InspectionError, Is.Not.Null.And.Not.Empty);
        Assert.That(diagnostic.ObservedOwners, Is.Empty);
        Assert.That(value(), Is.EqualTo(1));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int value() => 1;
    private static void addOne(ref int __result) => __result++;
    private static void addTen(ref int __result) => __result += 10;
}
