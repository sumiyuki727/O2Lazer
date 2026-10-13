using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using O2Jam.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamImportServiceTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void OversizedSourceFailsIndividuallyWithoutMissingSourceCleanup(bool failDuringScan)
    {
        var valid = createSource(0);
        var oversized = createSource(1);
        using (var stream = new FileStream(oversized, FileMode.Append)) stream.WriteByte(0);
        var limits = new OjnReadLimits(372, 4);
        var writer = new FakeWriter();
        var errors = new List<(Exception Failure, string Path)>();
        var progress = new List<(int Processed, int Total)>();
        var service = new O2JamImportService(new O2JamImportPlanner(limits), writer, _ => { },
            path => O2JamSourceSnapshot.ReadHash(path, failDuringScan ? limits : OjnReadLimits.Default));
        var summary = service.Refresh([valid, oversized], new Dictionary<string, O2JamImportedSource>(), (processed, total) => progress.Add((processed, total)),
            (failure, path) => errors.Add((failure, path)));
        Assert.Multiple(() =>
        {
            Assert.That(summary.Imported, Is.EqualTo(1));
            Assert.That(summary.Failed, Is.EqualTo(1));
            Assert.That(writer.Requests, Has.Count.EqualTo(1));
            Assert.That(writer.Requests[0].Plan.SourcePath, Is.EqualTo(valid));
            Assert.That(writer.MissingChecks, Is.Zero);
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0].Path, Is.EqualTo(oversized));
            Assert.That(errors[0].Failure, Is.TypeOf<InvalidDataException>());
            Assert.That(errors[0].Failure.Message, Does.Contain("source byte budget"));
            Assert.That(progress[^1].Processed, Is.EqualTo(progress[^1].Total));
            Assert.That(File.Exists(oversized), Is.True);
        });
    }

    [Test]
    public void ExcessEventsFailDuringPreparationWhileOtherSourcesContinue()
    {
        var valid = createSource(0);
        var crowded = createSource(1);
        var bytes = File.ReadAllBytes(crowded);
        BitConverter.GetBytes((uint)1).CopyTo(bytes, 324);
        File.WriteAllBytes(crowded, bytes);
        var writer = new FakeWriter();
        var errors = new List<Exception>();
        var summary = new O2JamImportService(new O2JamImportPlanner(new OjnReadLimits(372, 4)), writer)
            .Import([valid, crowded], (failure, _) => errors.Add(failure));
        Assert.That(summary.Imported, Is.EqualTo(1));
        Assert.That(summary.Failed, Is.EqualTo(1));
        Assert.That(writer.Requests[0].Plan.SourcePath, Is.EqualTo(valid));
        Assert.That(writer.MissingChecks, Is.Zero);
        Assert.That(errors[0].Message, Does.Contain("materialised event budget"));
    }
}
