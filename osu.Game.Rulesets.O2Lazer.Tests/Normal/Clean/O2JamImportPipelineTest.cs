using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamImportPipelineTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task NextBatchPreparationOverlapsWriteAndIsJoinedOnCancellation(bool cancel)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"o2lazer-prefetch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        using var enteredWrite = new ManualResetEventSlim();
        using var releaseWrite = new ManualResetEventSlim();
        using var enteredPrepare = new ManualResetEventSlim();
        using var releasePrepare = new ManualResetEventSlim(!cancel);
        using var cancellation = new CancellationTokenSource();
        var paths = Enumerable.Range(0, 17).Select(index =>
        {
            var path = Path.Combine(directory, $"source-{index:D2}.ojn");
            var data = OjnTestData.CreateChart();
            BitConverter.GetBytes((uint)index).CopyTo(data, 0);
            File.WriteAllBytes(path, data);
            return path;
        }).ToArray();
        var writer = new Writer(() => { enteredWrite.Set(); releaseWrite.Wait(); });
        var planner = new O2JamImportPlanner();
        var preparedExited = false;
        var service = new O2JamImportService(planner, writer, _ => { }, prepareSource: (path, previous) =>
        {
            if (path != paths[^1])
                return planner.Create(path, previous);
            if (!enteredWrite.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Preparation did not overlap the previous write.");
            enteredPrepare.Set();
            releasePrepare.Wait();
            try { return planner.Create(path, previous); }
            finally { preparedExited = true; }
        });
        var task = Task.Run(() => service.Refresh(paths, new Dictionary<string, O2JamImportedSource>(),
            null, null, cancellation.Token, null, committed: _ => Assert.That(writer.Calls, Is.GreaterThan(0))));
        try
        {
            Assert.That(enteredWrite.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(enteredPrepare.Wait(TimeSpan.FromSeconds(5)), Is.True);
            if (cancel)
                cancellation.Cancel();
            releaseWrite.Set();
            if (cancel)
            {
                await Task.Delay(50);
                Assert.That(task.IsCompleted, Is.False, "Cancellation must await the in-flight source reader.");
                releasePrepare.Set();
                var exception = Assert.ThrowsAsync<O2JamImportCancelledException>(async () => await task.WaitAsync(TimeSpan.FromSeconds(5)))!;
                Assert.That(exception.Summary.Imported, Is.EqualTo(16));
            }
            else
                Assert.That((await task.WaitAsync(TimeSpan.FromSeconds(5))).Imported, Is.EqualTo(17));
            Assert.That(preparedExited, Is.True);
        }
        finally
        {
            releaseWrite.Set();
            releasePrepare.Set();
            try { await task; } catch (OperationCanceledException) { }
            Directory.Delete(directory, true);
        }
    }

    private sealed class Writer(Action firstWrite) : IO2JamLibraryWriter
    {
        public int Calls;
        public int PendingNotifications => 0;
        public void RetryNotifications() { }
        public int MarkDeleted(IEnumerable<Guid> ids) => 0;
        public O2JamLibraryWriteResult Write(O2JamImportPlan plan) => O2JamLibraryWriteResult.Imported;
        public IReadOnlyList<O2JamLibraryWriteResult> WriteBatch(IReadOnlyList<O2JamLibraryWriteRequest> requests, CancellationToken token = default)
        {
            if (Interlocked.Increment(ref Calls) == 1)
                firstWrite();
            return requests.Select(_ => O2JamLibraryWriteResult.Imported).ToArray();
        }
    }
}