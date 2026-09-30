using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamImportServiceTest
{
    private string directory = null!;

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), $"o2lazer-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(directory, true);

    [Test]
    public void SourceCacheFailureDoesNotBecomeWriteFailureAndRetriesOnRefresh()
    {
        var writer = new FakeWriter();
        var fail = true;
        var calls = 0;
        var service = new O2JamImportService(new O2JamImportPlanner(), writer, _ =>
        {
            calls++;
            if (fail)
                throw new IOException("Injected audio cache failure.");
        });
        var path = createSource(0);
        var summary = service.Import([path]);
        Assert.That(summary.Imported, Is.EqualTo(1));
        Assert.That(summary.Failed, Is.Zero);
        Assert.That(summary.PendingNotifications, Is.EqualTo(1));
        fail = false;
        summary = service.Import([]);
        Assert.That(summary.PendingNotifications, Is.Zero);
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(writer.Content, Has.Count.EqualTo(1));
    }

    [Test]
    public void RolledBackBatchAndThrowingObserversDoNotHideLaterCommittedBatch()
    {
        var writer = new FakeWriter { FailFirstBatch = true };
        var service = new O2JamImportService(new O2JamImportPlanner(), writer);
        var paths = Enumerable.Range(0, 9).Select(createSource).ToArray();
        var summary = service.Refresh(paths, new Dictionary<string, O2JamImportedSource>(),
            (_, _) => throw new InvalidOperationException("Injected progress failure."),
            (_, _) => throw new InvalidOperationException("Injected error observer failure."));
        Assert.That(summary.Imported, Is.EqualTo(1));
        Assert.That(summary.Failed, Is.EqualTo(8));
        Assert.That(writer.Content, Has.Count.EqualTo(1));
        Assert.That(writer.MissingChecks, Is.Zero);
    }

    [Test]
    public void InvalidCopiesAreAllFailuresRatherThanClaimingAnExistingEntry()
    {
        var a = Path.Combine(directory, "a.ojn");
        var b = Path.Combine(directory, "b.ojn");
        File.WriteAllBytes(a, [1, 2, 3]);
        File.Copy(a, b);
        var writer = new FakeWriter();
        var summary = new O2JamImportService(new O2JamImportPlanner(), writer).Import([b, a, b]);
        Assert.That(summary.Failed, Is.EqualTo(2));
        Assert.That(summary.AlreadyPresent, Is.Zero);
        Assert.That(writer.Content, Is.Empty);
    }

    [Test]
    public void NotificationRetryUsesLatestValueAndDoesNotRepeatSuccessfulRecipients()
    {
        var queue = new O2JamLibraryNotificationQueue<int, string>();
        var fail = true;
        var delivered = new List<string>();
        var successful = new List<string>();
        Action<string> recipient = value =>
        {
            if (fail)
                throw new IOException("Injected failure.");
            delivered.Add(value);
        };
        queue.Publish(1, "old", [recipient, successful.Add]);
        queue.Publish(1, "new", [recipient, successful.Add]);
        Assert.That(queue.Count, Is.EqualTo(1));
        fail = false;
        queue.Retry([recipient, successful.Add]);
        Assert.That(delivered, Is.EqualTo(new[] { "new" }));
        Assert.That(successful, Is.EqualTo(new[] { "old", "new" }));
        Assert.That(queue.Count, Is.Zero);
        queue.Retry([recipient, successful.Add]);
        Assert.That(delivered, Has.Count.EqualTo(1));
    }

    [Test]
    public void RemovedNotificationRecipientIsNotRetried()
    {
        var queue = new O2JamLibraryNotificationQueue<int, string>();
        var calls = 0;
        Action<string> recipient = _ =>
        {
            calls++;
            throw new IOException("Injected failure.");
        };
        queue.Publish(1, "value", [recipient]);
        queue.Retry([]);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(queue.Count, Is.Zero);
    }

    private string createSource(int index)
    {
        var path = Path.Combine(directory, $"{index:D2}.ojn");
        var bytes = OjnTestData.CreateChart();
        BitConverter.GetBytes(100 + index).CopyTo(bytes, 0);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private sealed class FakeWriter : IO2JamLibraryWriter
    {
        public HashSet<string> Content { get; } = [];
        public bool FailFirstBatch;
        public int MissingChecks;
        public int PendingNotifications => 0;
        public void RetryNotifications() { }
        public O2JamLibraryWriteResult Write(O2JamImportPlan plan) => WriteBatch([new O2JamLibraryWriteRequest(plan)])[0];
        public IReadOnlyList<O2JamLibraryWriteResult> WriteBatch(IReadOnlyList<O2JamLibraryWriteRequest> requests)
        {
            if (FailFirstBatch)
            {
                FailFirstBatch = false;
                throw new IOException("Injected transaction rollback.");
            }
            return requests.Select(request => Content.Add(request.Plan.SourceHash)
                ? O2JamLibraryWriteResult.Imported : O2JamLibraryWriteResult.AlreadyPresent).ToArray();
        }
        public int MarkDeleted(IEnumerable<Guid> setIds)
        {
            MissingChecks++;
            return 0;
        }
    }
}
