using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Audio;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamPreloadSchedulerTest
{
    [Test]
    public async Task ImminentAudioOvertakesLookaheadWithoutLoadingTwice()
    {
        var scheduler = new O2JamPreloadScheduler(1);
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = scheduler.Schedule(_ => gate.Task, CancellationToken.None, false);
        var order = new ConcurrentQueue<int>();
        var speculative = scheduler.Schedule(_ => load(1), CancellationToken.None, false);
        var imminent = scheduler.Schedule(_ => load(2), CancellationToken.None, false);
        imminent.Prioritise();
        imminent.Prioritise();
        gate.SetResult(0);

        await Task.WhenAll(first.Task, speculative.Task, imminent.Task).WaitAsync(System.TimeSpan.FromSeconds(5));

        Assert.That(order, Is.EqualTo(new[] { 2, 1 }));

        Task<int> load(int id)
        {
            order.Enqueue(id);
            return Task.FromResult(id);
        }
    }

    [Test]
    public async Task CancelledAndFailedJobsDoNotBlockNewSelection()
    {
        var scheduler = new O2JamPreloadScheduler(1);
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = scheduler.Schedule(_ => gate.Task, CancellationToken.None, false);
        using var cancellation = new CancellationTokenSource();
        var cancelled = scheduler.Schedule(_ => Task.FromResult(1), cancellation.Token, false);
        var failed = scheduler.Schedule<int>(_ => throw new System.InvalidOperationException(), CancellationToken.None, false);
        var next = scheduler.Schedule(_ => Task.FromResult(2), CancellationToken.None, false);
        cancellation.Cancel();
        gate.SetResult(0);

        await first.Task.WaitAsync(System.TimeSpan.FromSeconds(5));
        Assert.That(await next.Task.WaitAsync(System.TimeSpan.FromSeconds(5)), Is.EqualTo(2));
        Assert.That(cancelled.Task.IsCanceled, Is.True);
        Assert.ThrowsAsync<System.InvalidOperationException>(async () => await failed.Task);
    }
}
