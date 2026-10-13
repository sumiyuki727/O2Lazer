using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Audio;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamPreloadBatchTest
{
    [Test]
    public async Task AutomaticOpeningAddedLastStillOvertakesSpeculativeRequests()
    {
        var scheduler = new O2JamPreloadScheduler(1);
        var order = new ConcurrentQueue<int>();
        O2JamPreloadScheduler.Preparation<int> far, imminent;
        using (scheduler.BeginBatch())
        {
            far = scheduler.Schedule(_ => load(7), CancellationToken.None, false);
            imminent = scheduler.Schedule(_ => load(0), CancellationToken.None, true);
            Assert.That(order, Is.Empty);
        }
        await Task.WhenAll(far.Task, imminent.Task).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(order, Is.EqualTo(new[] { 0, 7 }));
        Task<int> load(int id) { order.Enqueue(id); return Task.FromResult(id); }
    }

    [Test]
    public async Task NestedBatchAndRepeatedDisposeDoNotStartTooEarlyOrBlockFutureWork()
    {
        var scheduler = new O2JamPreloadScheduler(1);
        var outer = scheduler.BeginBatch();
        var inner = scheduler.BeginBatch();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = scheduler.Schedule(_ => { started.SetResult(); return Task.FromResult(1); }, CancellationToken.None, true);
        inner.Dispose();
        inner.Dispose();
        Assert.That(started.Task.IsCompleted, Is.False);
        outer.Dispose();
        outer.Dispose();
        Assert.That(await pending.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(1));
        Assert.That(await scheduler.Schedule(_ => Task.FromResult(2), CancellationToken.None, false).Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(2));
    }
}
