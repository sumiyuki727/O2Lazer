using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ManagedBass;
using NUnit.Framework;
using O2Jam.Formats.Ojm;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Framework.Audio.Sample;
using osu.Framework.Audio.Track;
using osu.Framework.Development;
using osu.Framework.IO.Stores;
using osu.Framework.Threading;
using osu.Game.Rulesets.O2Lazer.Audio;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
public class O2JamNativePreloadCompletionTest
{
    [TestCase(0)]
    [TestCase(1)]
    public async Task AcquisitionRemainsAvailableBeforeNativePreparation(int id)
    {
        using var fixture = new Fixture();
        var acquired = source<Sample?>();
        var ready = fixture.Host.PrepareSampleAsync($"o2jam/{id}", CancellationToken.None, acquired);
        using var sample = await acquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(sample, Is.Not.Null);
        Assert.That(sample!.IsLoaded, Is.False);
        Assert.That(ready.IsCompleted, Is.False);
        var independentlyOwned = fixture.Samples.Get(sample.Name);
        Assert.That(independentlyOwned, Is.Not.SameAs(sample));
        fixture.Frame();
        Assert.That(await ready.WaitAsync(TimeSpan.FromSeconds(5)), Is.SameAs(sample));
        Assert.That(sample.IsLoaded, Is.True);
        independentlyOwned.Dispose();
        Fixture.OnAudio(independentlyOwned.Update);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InvalidSampleAndNoDeviceFinishAfterAttemptRatherThanWaitingForever(bool noDevice)
    {
        using var fixture = new Fixture();
        if (noDevice)
            Bass.Free();
        var acquired = source<Sample?>();
        var ready = fixture.Host.PrepareSampleAsync(noDevice ? "o2jam/0" : "o2jam/99", CancellationToken.None, acquired);
        using var sample = await acquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(ready.IsCompleted, Is.False);
        fixture.Frame();
        Assert.That(await ready.WaitAsync(TimeSpan.FromSeconds(5)), Is.SameAs(sample));
        Assert.That(sample!.IsLoaded, Is.False);
        if (noDevice)
        {
            Assert.That(Bass.Init(0), Is.True);
            Fixture.OnAudio(() => fixture.Samples.GetType().GetMethod("UpdateDevice", BindingFlags.Instance | BindingFlags.NonPublic)!
                                                    .Invoke(fixture.Samples, [0]));
            Assert.That(sample.IsLoaded, Is.True, "Native device recovery retains the original sample factory.");
        }
    }

    [Test]
    public async Task MissingSampleDoesNotBorrowIdZeroOrNeedAudioUpdate()
    {
        using var fixture = new Fixture();
        var acquired = source<Sample?>();
        var ready = fixture.Host.PrepareSampleAsync("o2jam/12345", CancellationToken.None, acquired);
        Assert.That(await acquired.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.Null);
        Assert.That(await ready.WaitAsync(TimeSpan.FromSeconds(5)), Is.Null);
        Assert.That(fixture.FactoryCount, Is.Zero);
    }

    [Test]
    public async Task NativeSamplesAcrossTwoHostsRemainWithinSharedPreloadSlots()
    {
        using var first = new Fixture();
        using var second = new Fixture(initialiseBass: false);
        var scheduler = new O2JamPreloadScheduler(4);
        var acquired = Enumerable.Range(0, 12).Select(_ => source<Sample?>()).ToArray();
        var ready = Enumerable.Range(0, 12).Select(index => scheduler.Schedule(
            token => (index % 2 == 0 ? first : second).Host.PrepareSampleAsync($"o2jam/{index}", token, acquired[index]),
            CancellationToken.None, false)).ToArray();
        await Task.WhenAll(acquired.Take(4).Select(item => item.Task)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(first.FactoryCount + second.FactoryCount, Is.EqualTo(4));
        Assert.That(acquired.Skip(4).All(item => !item.Task.IsCompleted), Is.True);
        Assert.That(ready.All(item => !item.Task.IsCompleted), Is.True);
        var all = Task.WhenAll(ready.Select(item => item.Task));
        Assert.That(() =>
        {
            first.Frame();
            second.Frame();
            var factories = first.Factories.Concat(second.Factories).ToArray();
            Assert.That(factories.Count(sample => !(bool)sample.GetType().GetProperty("IsLoaded")!.GetValue(sample)!), Is.LessThanOrEqualTo(4));
            return all.IsCompleted;
        }, Is.True.After(5000, 10));
        Assert.That(await all, Has.Length.EqualTo(12));
    }

    [Test]
    public async Task CancellationSettlesConsumerButKeepsSlotUntilNativeAttemptEnds()
    {
        using var fixture = new Fixture();
        var scheduler = new O2JamPreloadScheduler(1);
        using var cancellation = new CancellationTokenSource();
        var acquired = source<Sample?>();
        var started = source<Task<Sample?>>();
        var first = scheduler.Schedule(token =>
        {
            var full = fixture.Host.PrepareSampleAsync("o2jam/1", token, acquired);
            started.SetResult(full);
            return full;
        }, cancellation.Token, false);
        var nextAcquired = source<Sample?>();
        var next = scheduler.Schedule(token => fixture.Host.PrepareSampleAsync("o2jam/2", token, nextAcquired), CancellationToken.None, false);
        var fullReady = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var sample = await acquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        Assert.That(first.Task.IsCanceled, Is.True);
        Assert.That(fullReady.IsCompleted, Is.False);
        Assert.That(nextAcquired.Task.IsCompleted, Is.False);
        fixture.Frame();
        await fullReady.WaitAsync(TimeSpan.FromSeconds(5));
        await nextAcquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(next.Task.IsCompleted, Is.False);
        fixture.Frame();
        Assert.That(await next.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.Not.Null);
    }

    [Test]
    public async Task TrackCancellationKeepsSlotUntilNativePublicFenceEnds()
    {
        using var fixture = new Fixture();
        var scheduler = new O2JamPreloadScheduler(1);
        using var cancellation = new CancellationTokenSource();
        var started = source<Task<Track?>>();
        var first = scheduler.Schedule(token =>
        {
            var full = fixture.Host.PrepareTrackAsync("o2jam/1", token);
            started.SetResult(full);
            return full;
        }, cancellation.Token, false);
        var fullReady = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(() => fixture.PublishedCount, Is.EqualTo(1).After(5000, 10));
        var nextAcquired = source<Sample?>();
        var next = scheduler.Schedule(token => fixture.Host.PrepareSampleAsync("o2jam/2", token, nextAcquired), CancellationToken.None, false);
        cancellation.Cancel();
        Assert.That(first.Task.IsCanceled, Is.True);
        Assert.That(fullReady.IsCompleted, Is.False);
        Assert.That(nextAcquired.Task.IsCompleted, Is.False);
        fixture.Frame();
        await fullReady.WaitAsync(TimeSpan.FromSeconds(5));
        await nextAcquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Frame();
        Assert.That(await next.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.Not.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task GetterFailureIsObservedWithoutBlockingRetry(bool track)
    {
        using var fixture = new Fixture();
        fixture.Resources.FailGet = true;
        var acquired = source<Sample?>();
        Task ready = track ? fixture.Host.PrepareTrackAsync("o2jam/1", CancellationToken.None)
                           : fixture.Host.PrepareSampleAsync("o2jam/1", CancellationToken.None, acquired);
        if (!track)
            Assert.ThrowsAsync<IOException>(async () => await acquired.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.ThrowsAsync<IOException>(async () => await ready.WaitAsync(TimeSpan.FromSeconds(5)));
        fixture.Resources.FailGet = false;
        var nextAcquired = source<Sample?>();
        var nextReady = fixture.Host.PrepareSampleAsync("o2jam/0", CancellationToken.None, nextAcquired);
        await nextAcquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Frame();
        Assert.That((await nextReady.WaitAsync(TimeSpan.FromSeconds(5)))!.IsLoaded, Is.True);
    }

    [Test]
    public async Task GetterCompletingDuringStoreUpdateCannotPassThatCyclesFence()
    {
        using var fixture = new Fixture(blockGetter: true);
        var blocker = new UpdateBlocker();
        ((AudioCollectionManager<AdjustableAudioComponent>)fixture.Samples).AddItem(blocker);
        var acquired = source<Sample?>();
        var ready = fixture.Host.PrepareSampleAsync("o2jam/0", CancellationToken.None, acquired);
        await fixture.Resources.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var frame = Task.Run(() =>
        {
            Bass.CurrentDevice = 0;
            fixture.Frame();
        });
        try
        {
            await blocker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Resources.Release.Set();
            Assert.That(() => fixture.FactoryCount, Is.EqualTo(1).After(5000, 10));
        }
        finally
        {
            blocker.Release.Set();
        }
        await frame.WaitAsync(TimeSpan.FromSeconds(5));
        using var sample = await acquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(sample!.IsLoaded, Is.False, "AddItem was queued after this cycle drained native pending actions.");
        Assert.That(ready.IsCompleted, Is.False);
        fixture.Frame();
        await ready.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(sample.IsLoaded, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task GracefulCloseDrainsAcquiredWorkAndRejectsNewWork(bool track)
    {
        using var fixture = new Fixture();
        var acquired = source<Sample?>();
        Task ready = track ? fixture.Host.PrepareTrackAsync("o2jam/1", CancellationToken.None)
                           : fixture.Host.PrepareSampleAsync("o2jam/1", CancellationToken.None, acquired);
        if (track)
            Assert.That(() => fixture.PublishedCount, Is.EqualTo(1).After(5000, 10));
        else
            await acquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Host.Close();
        Assert.That(ready.IsCompleted, Is.False);
        Assert.Throws<ObjectDisposedException>(() => fixture.Host.PrepareSampleAsync("o2jam/2", CancellationToken.None, source<Sample?>()));
        fixture.Frame();
        await ready.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Frame();
        Assert.That(fixture.Host.IsDisposed, Is.True);
        Assert.That(((AudioComponent)fixture.Samples).IsDisposed, Is.True);
        Assert.That(((AudioComponent)fixture.Tracks).IsDisposed, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NativeShutdownWithBlockedGetterSettlesAfterGetterReturns(bool track)
    {
        using var fixture = new Fixture(blockGetter: true);
        var acquired = source<Sample?>();
        Task ready = track ? fixture.Host.PrepareTrackAsync("o2jam/1", CancellationToken.None)
                           : fixture.Host.PrepareSampleAsync("o2jam/1", CancellationToken.None, acquired);
        await fixture.Resources.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Host.Dispose();
        fixture.Frame();
        Assert.That(fixture.Host.IsDisposed, Is.True);
        Assert.That(ready.IsCompleted, Is.False, "An executing getter still occupies its preparation slot.");
        if (!track)
            Assert.That(acquired.Task.IsCanceled, Is.True);
        fixture.Resources.Release.Set();
        Assert.That(() => ready.IsCompleted, Is.True.After(5000, 10));
        Assert.That(ready.IsCanceled, Is.True);
    }

    [Test]
    public async Task RealTrackReadyIncludesNativeDecoderAndMixerAttachment()
    {
        using var fixture = new Fixture();
        var ready = fixture.Host.PrepareTrackAsync("o2jam/0", CancellationToken.None);
        Assert.That(() => fixture.PublishedCount, Is.EqualTo(1).After(5000, 10));
        Assert.That(ready.IsCompleted, Is.False);
        fixture.Frame();
        var track = await ready.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(track, Is.Not.Null);
        Assert.That(track!.IsLoaded, Is.True);
        Fixture.OnAudio(() => Assert.DoesNotThrow(track.Stop));
    }

    [Test]
    public async Task InvalidTrackFinishesWithoutBlockingFollowingSamples()
    {
        using var fixture = new Fixture();
        var trackReady = fixture.Host.PrepareTrackAsync("o2jam/99", CancellationToken.None);
        Assert.That(() => fixture.PublishedCount, Is.EqualTo(1).After(5000, 10));
        fixture.Frame();
        Assert.That(await trackReady.WaitAsync(TimeSpan.FromSeconds(5)), Is.Null);
        var acquired = source<Sample?>();
        var sampleReady = fixture.Host.PrepareSampleAsync("o2jam/0", CancellationToken.None, acquired);
        await acquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Frame();
        Assert.That((await sampleReady.WaitAsync(TimeSpan.FromSeconds(5)))!.IsLoaded, Is.True);
    }

    private static TaskCompletionSource<T> source<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Fixture : IDisposable
    {
        internal readonly AudioMixer Mixer;
        internal readonly ISampleStore Samples;
        internal readonly ITrackStore Tracks;
        internal readonly O2JamAudioPreparationHost Host;
        internal readonly BlockingResources Resources;
        private readonly bool ownsBass;

        internal Fixture(bool initialiseBass = true, bool blockGetter = false)
        {
            ownsBass = initialiseBass;
            typeof(AudioThread).GetMethod("PreloadBass", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
            if (initialiseBass)
                Assert.That(Bass.Init(0), Is.True);
            var assembly = typeof(Sample).Assembly;
            Mixer = (AudioMixer)Activator.CreateInstance(assembly.GetType("osu.Framework.Audio.Mixing.Bass.BassAudioMixer")!,
                [null, null, "native preparation fixture"] )!;
            var wave = waveBytes();
            var archive = new OjmArchive(Enumerable.Range(0, 20).ToDictionary(id => id, id => new OjmSample(id, $"sample {id}", ".wav", wave)));
            var items = archive.Samples.ToDictionary(pair => pair.Key, pair => pair.Value);
            items[99] = new OjmSample(99, "invalid", ".wav", [1, 2, 3]);
            Resources = new BlockingResources(new O2JamArchiveResourceStore(new OjmArchive(items)), blockGetter);
            Samples = (ISampleStore)Activator.CreateInstance(assembly.GetType("osu.Framework.Audio.Sample.SampleStore")!,
                BindingFlags.Instance | BindingFlags.NonPublic, null, [Resources, Mixer], null)!;
            Tracks = (ITrackStore)Activator.CreateInstance(assembly.GetType("osu.Framework.Audio.Track.TrackStore")!,
                BindingFlags.Instance | BindingFlags.NonPublic, null, [Resources, Mixer], null)!;
            Host = new O2JamAudioPreparationHost(Samples, Tracks);
        }

        internal object[] Factories
        {
            get
            {
                var factories = (System.Collections.IDictionary)Samples.GetType().GetField("factories", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Samples)!;
                lock (factories)
                    return factories.Values.Cast<object>().Where(factory => factory != null).ToArray();
            }
        }

        internal int FactoryCount => Factories.Length;

        internal int PublishedCount
        {
            get
            {
                var gate = typeof(O2JamAudioPreparationHost).GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Host)!;
                lock (gate)
                {
                    var requests = (System.Collections.IEnumerable)typeof(O2JamAudioPreparationHost).GetField("pending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Host)!;
                    return requests.Cast<object>().Count(request => (bool)request.GetType().GetProperty("Published", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(request)!);
                }
            }
        }

        internal void Frame() => OnAudio(() =>
        {
            Mixer.Update();
            if (((AudioComponent)Tracks).IsAlive)
                ((AudioComponent)Tracks).Update();
            if (Host.IsAlive)
                Host.Update();
            Mixer.Update();
        });

        public void Dispose()
        {
            Resources.Release.Set();
            OnAudio(() =>
            {
                if (!Host.IsDisposed)
                {
                    Host.Dispose();
                    Host.Update();
                }
                if (!Mixer.IsDisposed)
                {
                    Mixer.Dispose();
                    Mixer.Update();
                }
            });
            Resources.Dispose();
            if (ownsBass)
                Bass.Free();
        }

        internal static void OnAudio(Action action)
        {
            var property = typeof(ThreadSafety).GetProperty(nameof(ThreadSafety.IsAudioThread))!;
            var previous = ThreadSafety.IsAudioThread;
            property.SetValue(null, true);
            try { action(); }
            finally { property.SetValue(null, previous); }
        }

        private static byte[] waveBytes()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            const int frames = 256;
            writer.Write("RIFF"u8);
            writer.Write(36 + frames * 2);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(44100);
            writer.Write(88200);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(frames * 2);
            writer.Write(new byte[frames * 2]);
            return stream.ToArray();
        }
    }

    private sealed class BlockingResources(IResourceStore<byte[]> inner, bool block) : IResourceStore<byte[]>
    {
        internal volatile bool FailGet;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ManualResetEventSlim Release = new();
        public byte[] Get(string name) { wait(); return inner.Get(name); }
        public Stream GetStream(string name) { wait(); return inner.GetStream(name); }
        private void wait()
        {
            if (FailGet)
                throw new IOException("Controlled fixture source failure.");
            if (!block)
                return;
            Entered.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Fixture getter was not released.");
        }
        public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default) => Task.Run(() => Get(name), cancellationToken);
        public IEnumerable<string> GetAvailableResources() => inner.GetAvailableResources();
        public void Dispose() => inner.Dispose();
    }

    private sealed class UpdateBlocker : AdjustableAudioComponent
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ManualResetEventSlim Release = new();
        private bool completed;
        public override bool IsAlive => base.IsAlive && !completed;
        protected override void UpdateState()
        {
            Entered.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Fixture native update was not released.");
            completed = true;
        }
    }
}
