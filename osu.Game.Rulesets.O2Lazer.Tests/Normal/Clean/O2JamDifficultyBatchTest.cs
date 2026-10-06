using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamDifficultyBatchTest
{
    [Test]
    public void TailSharesCacheReadsAndWritesAndReportsOnlyCompletedBatches()
    {
        using var store = new BatchStore(17);
        var counts = new List<int>();
        Assert.That(new O2JamLibraryDifficultyProcessor(store).Process(store.DirectoryPath, progress => counts.Add(progress.Processed), CancellationToken.None), Is.Zero);
        Assert.That(store.ReadBatches, Is.EqualTo(new[] { 16, 1 }));
        Assert.That(store.WriteBatches, Is.EqualTo(new[] { 16, 1 }));
        Assert.That(store.Complete, Has.Count.EqualTo(17));
        Assert.That(counts, Is.EqualTo(new[] { 0, 0, 16, 17 }));
    }

    [Test]
    public void RejectedAndCorruptSourcesDoNotBlockOtherResults()
    {
        using var store = new BatchStore(17);
        store.Rejected = store.Sources[0].Source.SetId;
        store.Corrupt = store.Sources[16].Source.SetId;
        Assert.That(new O2JamLibraryDifficultyProcessor(store).Process(store.DirectoryPath, null, CancellationToken.None), Is.EqualTo(2));
        Assert.That(store.Complete, Has.Count.EqualTo(15));
        Assert.That(store.WriteBatches, Is.EqualTo(new[] { 15 }));
    }

    [Test]
    public void CurrentCachesAreRecheckedBeforeAnySourceRead()
    {
        using var store = new BatchStore(2);
        foreach (var source in store.Sources)
            store.Complete.Add(source.Source.SetId, source.Source.DifficultyIdentities!.Select(identity =>
                new O2JamImportDifficultyCache(identity.Difficulty, 7.5, 42, O2JamManiaStarRating.CacheVersion)).ToArray());
        Assert.That(new O2JamLibraryDifficultyProcessor(store).Process(store.DirectoryPath, null, CancellationToken.None), Is.Zero);
        Assert.That(store.SourceReads, Is.Zero);
        Assert.That(store.WriteBatches, Is.Empty);
    }

    [Test]
    public void CancellationDuringPreparationPublishesNoUncommittedResults()
    {
        using var store = new BatchStore(2);
        using var cancellation = new CancellationTokenSource();
        store.OnRead = source => { if (source.Source.SetId == store.Sources[1].Source.SetId) cancellation.Cancel(); };
        Assert.Throws<OperationCanceledException>(() => new O2JamLibraryDifficultyProcessor(store).Process(store.DirectoryPath, null, cancellation.Token));
        Assert.That(store.Complete, Is.Empty);
        Assert.That(store.WriteBatches, Is.Empty);
    }

    [Test]
    public async Task ConcurrentWorkerDrainsBoundedInputsIntoCacheBatches()
    {
        using var store = new BatchStore(65);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        store.OnRead = source =>
        {
            if (source.Source.SetId != store.Sources[0].Source.SetId) return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
        };
        using var worker = new O2JamLibraryDifficultyPipeline(store, null, CancellationToken.None);
        try
        {
            worker.Offer(store.Sources[0]);
            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            foreach (var source in store.Sources.Skip(1)) worker.Offer(source);
        }
        finally { release.Set(); }
        await Task.Run(worker.Complete).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(worker.Completed, Is.EqualTo(33));
        Assert.That(worker.FailedSources, Is.Empty);
        Assert.That(store.ReadBatches, Is.EqualTo(new[] { 1, 16, 16 }));
        Assert.That(store.WriteBatches, Is.EqualTo(new[] { 1, 16, 16 }));
        Assert.That(store.Complete, Has.Count.EqualTo(33));
    }

    private sealed class BatchStore : IO2JamLibraryDifficultyBatchStore, IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), $"o2lazer-cache-batch-{Guid.NewGuid():N}");
        public O2JamLibraryDifficultySource[] Sources { get; }
        public List<int> ReadBatches { get; } = [];
        public List<int> WriteBatches { get; } = [];
        public Dictionary<Guid, IReadOnlyList<O2JamImportDifficultyCache>> Complete { get; } = [];
        public Guid Rejected, Corrupt;
        public int SourceReads;
        public Action<O2JamLibraryDifficultySource>? OnRead;
        private readonly Dictionary<Guid, byte[]> data = [];

        public BatchStore(int count)
        {
            Directory.CreateDirectory(DirectoryPath);
            Sources = Enumerable.Range(0, count).Select(index =>
            {
                var path = Path.Combine(DirectoryPath, $"{index:D3}.ojn");
                var bytes = OjnTestData.CreateChart();
                BitConverter.GetBytes(index).CopyTo(bytes, 0);
                File.WriteAllBytes(path, bytes);
                var plan = new O2JamImportPlanner().Create(path);
                var source = new O2JamLibraryDifficultySource(path, new O2JamImportedSource(Guid.NewGuid(), plan.SourceTimestamp,
                    bytes.LongLength, true, true, plan.SourceHash, plan.EncodingFallback, [],
                    plan.Charts.Select(chart => new O2JamStoredDifficultyIdentity(chart.Difficulty, chart.Md5Hash)).ToArray()));
                data.Add(source.Source.SetId, bytes);
                return source;
            }).ToArray();
        }

        public IReadOnlyList<O2JamLibraryDifficultySource> GetPendingDifficultySources(string path, CancellationToken token) => Sources;
        public IReadOnlyList<O2JamLibraryDifficultySourceCheck> RefreshDifficultySources(IReadOnlyList<O2JamLibraryDifficultySource> sources, CancellationToken token)
        {
            ReadBatches.Add(sources.Count);
            return sources.Select(source => source.Source.SetId == Rejected
                ? new O2JamLibraryDifficultySourceCheck(source, new InvalidDataException("Injected identity failure."))
                : new O2JamLibraryDifficultySourceCheck(source with { Source = source.Source with { ManiaCache = Complete.GetValueOrDefault(source.Source.SetId, []) } })).ToArray();
        }
        public byte[] ReadDifficultySource(O2JamLibraryDifficultySource source)
        {
            SourceReads++;
            OnRead?.Invoke(source);
            return source.Source.SetId == Corrupt ? [1, 2, 3] : data[source.Source.SetId];
        }
        public IReadOnlyList<O2JamLibraryDifficultyOutcome> WriteDifficultyBatch(IReadOnlyList<O2JamLibraryDifficultyCacheWrite> writes, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            WriteBatches.Add(writes.Count);
            foreach (var write in writes) Complete.Add(write.Source.Source.SetId, write.Difficulties);
            return writes.Select(write => new O2JamLibraryDifficultyOutcome(write.Source, write.Difficulties.Count)).ToArray();
        }
        public O2JamLibraryDifficultySource RefreshDifficultySource(O2JamLibraryDifficultySource source, CancellationToken token)
            => throw new AssertionException("A batch backend must not reopen a cache read per source.");
        public void WriteDifficulties(O2JamLibraryDifficultySource source, IReadOnlyList<O2JamImportDifficultyCache> caches, CancellationToken token)
            => throw new AssertionException("A batch backend must not commit one transaction per source.");
        public void Dispose() => Directory.Delete(DirectoryPath, true);
    }
}