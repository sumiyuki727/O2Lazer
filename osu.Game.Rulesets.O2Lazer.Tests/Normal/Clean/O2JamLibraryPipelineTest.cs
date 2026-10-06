using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamLibraryPipelineTest
{
    private static O2JamLibraryDifficultySource source() => new("test.ojn", new O2JamImportedSource(Guid.NewGuid(), null, null, true, true));

    [Test]
    public async Task FullStarQueueDoesNotBlockImportAndOneWorkerDrainsIt()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var active = 0;
        var maximum = 0;
        using var work = new O2JamLibraryDifficultyPipeline((_, token) =>
        {
            var count = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, count);
            entered.Set();
            try { release.Wait(token); }
            finally { Interlocked.Decrement(ref active); }
        }, null, CancellationToken.None);
        try
        {
            var first = source();
            work.Offer(first);
            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            work.Offer(first);
            for (var index = 0; index < 64; index++)
                work.Offer(source());
        }
        finally { release.Set(); }
        await Task.Run(work.Complete).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(work.Completed, Is.EqualTo(33), "Only the active source and the bounded 32-source queue were accepted.");
        Assert.That(maximum, Is.EqualTo(1));
        Assert.That(work.FailedSources, Is.Empty);
    }

    [Test]
    public async Task CancellationJoinsStarWorkerBeforeReturningToNextOperation()
    {
        using var entered = new ManualResetEventSlim();
        var exited = false;
        var work = new O2JamLibraryDifficultyPipeline((_, token) =>
        {
            entered.Set();
            try { token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); }
            finally { exited = true; }
        }, null, CancellationToken.None);
        work.Offer(source());
        Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
        await Task.Run(work.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(exited, Is.True);
        Assert.That(work.Completed, Is.Zero);
    }

    [Test]
    public void StarFailureDoesNotStopLaterCommittedSources()
    {
        var first = source();
        using var work = new O2JamLibraryDifficultyPipeline((item, _) =>
        {
            if (item == first)
                throw new IOException("Injected star failure.");
        }, _ => throw new IOException("Injected progress observer failure."), CancellationToken.None);
        work.Offer(first);
        work.Offer(source());
        work.Complete();
        Assert.That(work.Completed, Is.EqualTo(2));
        Assert.That(work.FailedSources, Is.EquivalentTo(new[] { first.Source.SetId }));
    }
}