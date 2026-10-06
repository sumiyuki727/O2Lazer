using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
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
    public void OrdinaryRefreshReusesFingerprintWithoutASecondContentRead()
    {
        var path = createSource(0);
        var plan = new O2JamImportPlanner().Create(path);
        var source = new O2JamImportedSource(Guid.NewGuid(), plan.SourceTimestamp, plan.SourceData.Length, true, true, plan.SourceHash,
            CanReuseFiles: true);
        var writer = new FakeWriter();
        var reads = 0;
        var invalidated = new List<string>();
        FileStream? guard = null;
        try
        {
            var service = new O2JamImportService(new O2JamImportPlanner(), writer, invalidated.Add, filename =>
            {
                reads++;
                var hash = O2JamSourceSnapshot.ReadHash(filename);
                guard = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.None);
                return hash;
            });
            var summary = service.Refresh([path], new Dictionary<string, O2JamImportedSource> { [path] = source });
            Assert.Multiple(() =>
            {
                Assert.That(summary.AlreadyPresent, Is.EqualTo(1));
                Assert.That(summary.Failed, Is.Zero);
                Assert.That(reads, Is.EqualTo(1));
                Assert.That(writer.Requests, Is.Empty);
                Assert.That(invalidated, Is.EqualTo(new[] { path }), "Explicit update still invalidates changed OJM archives.");
            });
        }
        finally { guard?.Dispose(); }
    }

    [TestCase(O2JamLibraryRefreshMode.Update)]
    [TestCase(O2JamLibraryRefreshMode.Repair)]
    public void BothRefreshModesDetectReplacementBytesWithAPreservedStamp(O2JamLibraryRefreshMode mode)
    {
        var path = createSource(0);
        var plan = new O2JamImportPlanner().Create(path);
        var source = new O2JamImportedSource(Guid.NewGuid(), plan.SourceTimestamp, plan.SourceData.Length, true, true, plan.SourceHash,
            CanReuseFiles: true);
        var bytes = plan.SourceData.ToArray();
        BitConverter.GetBytes(180f).CopyTo(bytes, 16);
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, plan.SourceTimestamp!.Value.UtcDateTime);
        var writer = new FakeWriter();
        var service = new O2JamImportService(new O2JamImportPlanner(), writer);
        var summary = service.Refresh([path], new Dictionary<string, O2JamImportedSource> { [path] = source }, mode: mode);
        Assert.That(summary.Failed, Is.Zero);
        Assert.That(summary.Imported, Is.EqualTo(1));
        Assert.That(writer.Requests.Single().Plan.SourceHash, Is.Not.EqualTo(plan.SourceHash));
        Assert.That(writer.Requests.Single().KnownSourceSetId, Is.Null);
    }
    [Test]
    public void RegisteredSourceReusesItsSuccessfulScanFingerprint()
    {
        var path = createSource(0);
        var hash = O2JamSourceSnapshot.ReadHash(path);
        var source = new O2JamImportedSource(Guid.NewGuid(), O2JamSourceTimestamp.Read(path), new FileInfo(path).Length, false, true, hash);
        var writer = new FakeWriter();
        var reads = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var service = new O2JamImportService(new O2JamImportPlanner(), writer, _ => { }, filename =>
        {
            reads.AddOrUpdate(filename, 1, (_, count) => count + 1);
            return O2JamSourceSnapshot.ReadHash(filename);
        });
        var summary = service.Refresh([path], new Dictionary<string, O2JamImportedSource> { [path] = source });
        Assert.That(summary.Failed, Is.Zero);
        Assert.That(reads[path], Is.EqualTo(1), "Matching must not repeat an already successful fingerprint read.");
        Assert.That(writer.Requests.Single().KnownSourceSetId, Is.EqualTo(source.SetId));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnscannedRegisteredSourceIsVerifiedBeforeKeepingItsLocation(bool unreadable)
    {
        var registered = createSource(0);
        var copy = Path.Combine(directory, "copy.ojn");
        File.Copy(registered, copy);
        var hash = O2JamSourceSnapshot.ReadHash(registered);
        var source = new O2JamImportedSource(Guid.NewGuid(), O2JamSourceTimestamp.Read(registered), new FileInfo(registered).Length, false, true, hash);
        var writer = new FakeWriter();
        var reads = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var service = new O2JamImportService(new O2JamImportPlanner(), writer, _ => { }, filename =>
        {
            reads.AddOrUpdate(filename, 1, (_, count) => count + 1);
            if (unreadable && filename == registered)
                throw new IOException("Injected unreadable registered source.");
            return O2JamSourceSnapshot.ReadHash(filename);
        });
        var summary = service.Refresh([copy], new Dictionary<string, O2JamImportedSource> { [registered] = source });
        Assert.That(reads[copy], Is.EqualTo(1));
        Assert.That(reads[registered], Is.EqualTo(1));
        if (unreadable)
        {
            Assert.That(summary.Failed, Is.EqualTo(1));
            Assert.That(writer.Requests, Is.Empty, "An unverified registered path must not be replaced by a scanned copy.");
        }
        else
        {
            Assert.That(summary.Failed, Is.Zero);
            Assert.That(writer.Requests.Single().Plan.SourcePath, Is.EqualTo(registered));
            Assert.That(writer.Requests.Single().KnownSourceSetId, Is.EqualTo(source.SetId));
        }
    }

    [Test]
    public void ChangedSourceAfterContentScanIsRejectedBeforeWriting()
    {
        var path = createSource(0);
        var hash = O2JamSourceSnapshot.ReadHash(path);
        var source = new O2JamImportedSource(Guid.NewGuid(), O2JamSourceTimestamp.Read(path), new FileInfo(path).Length, false, true, hash);
        var writer = new FakeWriter();
        var service = new O2JamImportService(new O2JamImportPlanner(), writer);
        var failures = new List<Exception>();
        var summary = service.Refresh([path], new Dictionary<string, O2JamImportedSource> { [path] = source },
            null, (exception, _) => failures.Add(exception), CancellationToken.None, progress =>
            {
                if (progress.Stage == O2JamLibraryStage.MatchingSources && progress.Processed == 0)
                {
                    var bytes = File.ReadAllBytes(path);
                    BitConverter.GetBytes(200).CopyTo(bytes, 0);
                    File.WriteAllBytes(path, bytes);
                }
            });
        Assert.That(summary.Failed, Is.EqualTo(1));
        Assert.That(writer.Requests, Is.Empty);
        Assert.That(failures.Single(), Is.TypeOf<IOException>());
    }

    [Test]
    public void SourceReadingProgressCanCancelBeforeWriting()
    {
        var path = createSource(0);
        var writer = new FakeWriter();
        var progress = new List<O2JamLibraryProgress>();
        var service = new O2JamImportService(new O2JamImportPlanner(), writer);
        using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => service.Refresh([path], new Dictionary<string, O2JamImportedSource>(),
            null, null, cancellation.Token, value =>
            {
                progress.Add(value);
                if (value.Stage == O2JamLibraryStage.ReadingSourceFiles && value.Processed == value.Total)
                    cancellation.Cancel();
            }));
        Assert.That(progress, Does.Contain(new O2JamLibraryProgress(1, 1, O2JamLibraryStage.ReadingSourceFiles)));
        Assert.That(writer.Requests, Is.Empty);
        Assert.That(writer.MissingChecks, Is.Zero);
    }

    [Test]
    public void PreparationObserverFailureDoesNotPreventImport()
    {
        var path = createSource(0);
        var writer = new FakeWriter();
        var service = new O2JamImportService(new O2JamImportPlanner(), writer);
        var summary = service.Refresh([path], new Dictionary<string, O2JamImportedSource>(), null, null, CancellationToken.None,
            _ => throw new InvalidOperationException("Injected preparation progress failure."));
        Assert.That(summary.Imported, Is.EqualTo(1));
        Assert.That(summary.Failed, Is.Zero);
    }
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
        var paths = Enumerable.Range(0, 17).Select(createSource).ToArray();
        var summary = service.Refresh(paths, new Dictionary<string, O2JamImportedSource>(),
            (_, _) => throw new InvalidOperationException("Injected progress failure."),
            (_, _) => throw new InvalidOperationException("Injected error observer failure."));
        Assert.That(summary.Imported, Is.EqualTo(1));
        Assert.That(summary.Failed, Is.EqualTo(16));
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

    [Test]
    public void DuplicatePathsUseTheSameUniqueProgressTotalBeforeAndAfterImport()
    {
        var path = createSource(0);
        var duplicate = Path.Combine(directory, "copy.ojn");
        File.Copy(path, duplicate);
        var plan = new O2JamImportPlanner().Create(path);
        var source = new O2JamImportedSource(Guid.NewGuid(), plan.SourceTimestamp, plan.SourceData.Length, true, true, plan.SourceHash, CanReuseFiles: true);
        foreach (var imported in new[] { false, true })
        {
            var progress = new List<(int Processed, int Total)>();
            var writer = new FakeWriter();
            var summary = new O2JamImportService(new O2JamImportPlanner(), writer).Refresh([path, duplicate],
                imported ? new Dictionary<string, O2JamImportedSource> { [path] = source } : [],
                (processed, total) => progress.Add((processed, total)));
            Assert.That(summary.Failed, Is.Zero);
            Assert.That(progress.Last(), Is.EqualTo((1, 1)));
            Assert.That(progress.All(value => value.Processed <= value.Total), Is.True);
            Assert.That(summary.Imported + summary.AlreadyPresent, Is.EqualTo(2), "File outcome accounting remains separate from unique progress.");
        }
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
        public List<O2JamLibraryWriteRequest> Requests { get; } = [];
        public bool FailFirstBatch;
        public int MissingChecks;
        public int PendingNotifications => 0;
        public void RetryNotifications() { }
        public O2JamLibraryWriteResult Write(O2JamImportPlan plan) => WriteBatch([new O2JamLibraryWriteRequest(plan)])[0];
        public IReadOnlyList<O2JamLibraryWriteResult> WriteBatch(IReadOnlyList<O2JamLibraryWriteRequest> requests, CancellationToken cancellationToken = default)
        {
            if (FailFirstBatch)
            {
                FailFirstBatch = false;
                throw new IOException("Injected transaction rollback.");
            }
            Requests.AddRange(requests);
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
