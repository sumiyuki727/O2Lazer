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
using osu.Framework.Audio.Sample;
using osu.Framework.Development;
using osu.Framework.IO.Stores;
using osu.Framework.Threading;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
public partial class O2JamSkinPreparationIntegrationTest
{
    [Test]
    public async Task GameplayLookupCanCloneAcquiredSampleBeforePreparationReady()
    {
        using var fixture = new Fixture();
        fixture.Skin.PrefetchSamples([0]);
        var acquisition = fixture.Acquired(0);
        using var privateSample = await acquisition.WaitAsync(TimeSpan.FromSeconds(5));
        using var drawableSample = (Sample)fixture.Skin.GetSample(new O2JamHitSampleInfo(0, 100, 0))!;
        Assert.That(drawableSample, Is.Not.SameAs(privateSample));
        Assert.That(drawableSample.IsLoaded, Is.False);
        Assert.That(fixture.Ready(0).IsCompleted, Is.False);
        fixture.Frame();
        await fixture.Ready(0).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(fixture.Skin.IsSampleReady(0), Is.True);
        Assert.That(drawableSample.IsLoaded, Is.True);
    }

    [Test]
    public void AutomaticOpeningAddedByClassificationStillOccupiesFirstBatch()
    {
        using var fixture = new Fixture();
        List<O2JamPreviewEvent> events = [new(0, 7, 100, 0, true, false), new(0, 0, 100, 0, true, true)];
        events.AddRange(Enumerable.Range(1, 14).Where(id => id != 7).Select(id => new O2JamPreviewEvent(1000, id, 100, 0, true, false)));
        fixture.Skin.PrefetchPreview(new O2JamPreviewSchedule([], events), 10_000);
        Assert.That(() => fixture.FactoryIds.Length, Is.EqualTo(4).After(5000, 10));
        Assert.That(fixture.FactoryIds, Does.Contain(0));
        Assert.That(fixture.FactoryIds, Does.Contain(7));
        Assert.That(fixture.AllReady.All(task => !task.IsCompleted), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingOpeningDoesNotHideLaterAutomaticOpeningFromUrgentBatch(bool deferredArchive)
    {
        using var fixture = new Fixture(deferredArchive);
        List<O2JamPreviewEvent> events = [new(0, 12345, 100, 0, true, false), new(3000, 7, 100, 0, true, false), new(3000, 0, 100, 0, true, true)];
        events.AddRange(Enumerable.Range(1, 14).Where(id => id != 7).Select(id => new O2JamPreviewEvent(4000, id, 100, 0, true, false)));
        fixture.Skin.PrefetchPreview(new O2JamPreviewSchedule([], events), 10_000);
        if (deferredArchive)
        {
            Assert.That(fixture.Skin.IsReadyForScheduling, Is.False);
            fixture.ResolveArchive();
            Assert.That(() => fixture.Skin.IsReadyForScheduling, Is.True.After(5000, 10));
        }
        Assert.That(() => fixture.FactoryIds.Length, Is.EqualTo(4).After(5000, 10));
        Assert.That(fixture.FactoryIds, Does.Contain(0));
        Assert.That(fixture.FactoryIds, Does.Contain(7));
        Assert.That(fixture.FactoryIds, Does.Not.Contain(12345));
    }

    [Test]
    public async Task LongChartLeadInKeepsOpeningPrefetchOnOriginalTimeline()
    {
        using var fixture = new Fixture();
        var events = new[] { new O2JamPreviewEvent(30_000, 0, 100, 0, true, true), new O2JamPreviewEvent(31_000, 7, 100, 0, true, false) };
        fixture.Skin.PrefetchPreview(new O2JamPreviewSchedule([], events), 10_000);
        await fixture.Acquired(0).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(events[0].Time, Is.EqualTo(30_000));
        Assert.That(fixture.AllReady.Length, Is.EqualTo(2));
        Assert.That(fixture.Ready(0).IsCompleted, Is.False);
    }

    [Test]
    public async Task NativeAudioThreadExitDrainsActiveAndQueuedSkinPreparations()
    {
        using var fixture = new Fixture();
        fixture.Skin.PrefetchSamples(Enumerable.Range(0, 15));
        Assert.That(() => fixture.FactoryIds.Length, Is.EqualTo(4).After(5000, 10));
        var ready = fixture.AllReady;
        fixture.NativeExit();
        Assert.That(fixture.Manager.IsDisposed, Is.True);
        // The native exit method performs its final manager update before releasing devices.
        // Late getters and queued jobs must then settle without relying on another audio frame.
        await Task.WhenAll(ready.Select(task => task.ContinueWith(completed => _ = completed.Exception))).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(ready.All(task => task.IsCompleted), Is.True);
        Assert.That(ready.All(task => !task.IsCompletedSuccessfully), Is.True);
        Assert.That(fixture.FactoryIds.Length, Is.EqualTo(4));
    }

    [Test]
    public async Task ClosingSkinCancelsQueuedLookupWithoutWaitingForNativeDecoding()
    {
        using var fixture = new Fixture();
        fixture.Skin.PrefetchSamples(Enumerable.Range(0, 15));
        Assert.That(() => fixture.FactoryIds.Length, Is.EqualTo(4).After(5000, 10));
        var queuedAcquisition = fixture.Acquired(14);
        fixture.Skin.Dispose();
        await queuedAcquisition.ContinueWith(task => _ = task.Exception).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(queuedAcquisition.IsCanceled, Is.True);
        fixture.Frame();
        await Task.WhenAll(fixture.AllReady.Select(task => task.ContinueWith(completed => _ = completed.Exception))).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly AudioThread thread = new();
        private readonly ResourceStore<byte[]> empty = new();
        internal readonly AudioManager Manager;
        internal readonly O2JamBeatmapSkin Skin;
        private readonly OjmArchive archive;
        private readonly TaskCompletionSource<OjmArchive> deferred = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Fixture(bool deferredArchive = false, OjmArchive? archiveOverride = null)
        {
            Assert.That(Bass.Init(0), Is.True);
            Manager = new AudioManager(thread, empty, empty, null);
            var wave = waveBytes();
            archive = archiveOverride ?? new OjmArchive(Enumerable.Range(0, 20).ToDictionary(id => id,
                id => new OjmSample(id, $"sample {id}", ".wav", wave)));
            Skin = deferredArchive ? new O2JamBeatmapSkin(deferred.Task, Manager) : new O2JamBeatmapSkin(archive, Manager);
        }

        internal void ResolveArchive() => deferred.TrySetResult(archive);

        private System.Collections.IDictionary preparers => (System.Collections.IDictionary)typeof(O2JamBeatmapSkin)
            .GetField("prefetchedSamples", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Skin)!;

        internal Task<Sample?> Acquired(int id) => (Task<Sample?>)preparers[id]!.GetType()
            .GetProperty("Acquired", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preparers[id])!;

        internal Task Ready(int id) => (Task)preparers[id]!.GetType()
            .GetProperty("Task", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preparers[id])!;

        internal Task[] AllReady => preparers.Keys.Cast<int>().Select(Ready).ToArray();

        internal int[] FactoryIds
        {
            get
            {
                var samples = (ISampleStore)typeof(O2JamBeatmapSkin).GetField("samples", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Skin)!;
                var factories = (System.Collections.IDictionary)samples.GetType().GetField("factories", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(samples)!;
                lock (factories)
                    return factories.Keys.Cast<string>().Select(name => int.Parse(name["o2jam/".Length..])).ToArray();
            }
        }

        internal void Frame(Action? before = null) => onAudio(() => { before?.Invoke(); Manager.Update(); });

        internal void NativeExit() => onAudio(() => typeof(AudioThread).GetMethod("OnExit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(thread, null));

        public void Dispose()
        {
            Skin.Dispose();
            if (!Manager.IsDisposed)
                NativeExit();
            empty.Dispose();
            Bass.Free();
        }

        private static void onAudio(Action action)
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
}
