using System;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using O2Jam.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamSourceBudgetTest
{
    private string directory = null!;

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), $"o2lazer-source-budget-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(directory, true);

    [Test, Combinatorial]
    public void SourceBudgetAppliesBeforeHashSnapshotAndPlanning([Values(-1, 0, 1)] int allowance,
                                                               [Values("Hash", "Snapshot", "Plan")] string entry)
    {
        var path = Path.Combine(directory, "input.ojn");
        var bytes = OjnTestData.CreateChart();
        File.WriteAllBytes(path, bytes);
        var limits = new OjnReadLimits(bytes.Length + allowance, 4);
        object? result = null;
        Action read = () => result = entry switch
        {
            "Hash" => O2JamSourceSnapshot.ReadHash(path, limits),
            "Snapshot" => O2JamSourceSnapshot.Read(path, limits),
            _ => new O2JamImportPlanner(limits).Create(path),
        };
        if (allowance < 0)
            Assert.That(Assert.Throws<InvalidDataException>(read)!.Message, Does.Contain("source byte budget"));
        else
        {
            read();
            var expectedHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            switch (result)
            {
                case string hash:
                    Assert.That(hash, Is.EqualTo(expectedHash));
                    break;
                case O2JamSourceSnapshot snapshot:
                    Assert.That(snapshot.Data, Is.EqualTo(bytes));
                    Assert.That(snapshot.Hash, Is.EqualTo(expectedHash));
                    break;
                case O2JamImportPlan plan:
                    Assert.That(plan.SourceData, Is.EqualTo(bytes));
                    Assert.That(plan.SourceHash, Is.EqualTo(expectedHash));
                    break;
            }
        }
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
    }

    [Test]
    public void CommitGuardRejectsCapturedBytesBeforeHashValidation()
    {
        var path = Path.Combine(directory, "guard.ojn");
        File.WriteAllBytes(path, OjnTestData.CreateChart());
        var plan = new O2JamImportPlanner().Create(path);
        // Deliberately invalid identity distinguishes an early resource rejection from hash validation.
        plan = plan with { SourceHash = "invalid" };
        Assert.That(Assert.Throws<InvalidDataException>(() => O2JamSourceSnapshot.AcquireGuard(plan,
            new OjnReadLimits(plan.SourceData.Length - 1, 4)))!.Message, Does.Contain("source byte budget"));
    }

    [Test]
    public void CommitGuardRejectsGrownSourceAndReleasesItsHandle()
    {
        var path = Path.Combine(directory, "grown.ojn");
        File.WriteAllBytes(path, OjnTestData.CreateChart());
        var plan = new O2JamImportPlanner().Create(path);
        using (var stream = new FileStream(path, FileMode.Append)) stream.WriteByte(0);
        Assert.That(Assert.Throws<InvalidDataException>(() => O2JamSourceSnapshot.AcquireGuard(plan,
            new OjnReadLimits(plan.SourceData.Length, 4)))!.Message, Does.Contain("source byte budget"));
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.That(exclusive.Length, Is.EqualTo(plan.SourceData.Length + 1));
    }

    [Test]
    public void ExactLimitGuardRetainsOriginalSourceIdentity()
    {
        var path = Path.Combine(directory, "exact.ojn");
        File.WriteAllBytes(path, OjnTestData.CreateChart());
        var plan = new O2JamImportPlanner().Create(path);
        using var guard = O2JamSourceSnapshot.AcquireGuard(plan, new OjnReadLimits(plan.SourceData.Length, 4));
        Assert.That(guard.CanRead, Is.True);
        Assert.That(guard.Length, Is.EqualTo(plan.SourceData.Length));
    }
}
