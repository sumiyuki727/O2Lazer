using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ManagedBass;
using NUnit.Framework;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Framework.Audio.Track;
using osu.Framework.Development;
using osu.Framework.Threading;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Formats.Ojm;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
public class O2JamNativeTrackPreparationTest
{
    private AudioMixer mixer = null!;
    private ITrackStore store = null!;
    private O2JamArchiveResourceStore resources = null!;

    [SetUp]
    public void SetUp()
    {
        // The framework keeps its deviceless mixer/store constructors internal. Reflection here
        // exercises the actual installed API queue semantics without starting a window or sound.
        typeof(AudioThread).GetMethod("PreloadBass", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
        Assert.That(Bass.Init(0), Is.True);
        var assembly = typeof(Track).Assembly;
        mixer = (AudioMixer)Activator.CreateInstance(assembly.GetType("osu.Framework.Audio.Mixing.Bass.BassAudioMixer")!,
            [null, null, "O2Jam test mixer"])!;
        resources = new O2JamArchiveResourceStore(new OjmArchive(new Dictionary<int, OjmSample>
        {
            [7] = new OjmSample(7, "test", ".wav", createWave()),
            [8] = new OjmSample(8, "invalid", ".wav", [1, 2, 3]),
        }));
        store = (ITrackStore)Activator.CreateInstance(assembly.GetType("osu.Framework.Audio.Track.TrackStore")!,
            BindingFlags.Instance | BindingFlags.NonPublic, null, [resources, mixer], null)!;
    }

    [TearDown]
    public void TearDown()
    {
        onAudioThread(() =>
        {
            ((AudioComponent)store).Dispose();
            ((AudioComponent)store).Update();
            mixer.Dispose();
            mixer.Update();
        });
        resources.Dispose();
        Bass.Free();
    }

    [Test]
    public void RawTrackTaskCompletionDoesNotMeanMixerIsAttached()
    {
        var track = store.GetAsync("o2jam/7").GetAwaiter().GetResult();

        Assert.That(track.IsLoaded, Is.False);
        onAudioThread(() => Assert.Throws<NullReferenceException>(() => track.Start()));
        pumpUntil(() => track.IsLoaded);
    }

    [Test]
    public void PreparedNativeTrackCanStartStopAndSeekOnAudioThread()
    {
        var observing = new ObservingStore(store);
        var prepared = O2JamTrackPreparation.LoadAsync(observing, "o2jam/7", CancellationToken.None);
        var created = observing.Created.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(created.IsLoaded, Is.False);
            Assert.That(prepared.IsCompleted, Is.False);
        });

        pumpUntil(() => prepared.IsCompleted);
        var track = prepared.GetAwaiter().GetResult();
        Assert.That(track, Is.SameAs(created));
        onAudioThread(() => Assert.DoesNotThrow(() =>
        {
            track!.Start();
            track.Stop();
            Assert.That(track.Seek(100), Is.True);
            track.Start();
            track.Stop();
        }));
    }

    [Test]
    public void FailedNativeDecoderDoesNotLeavePreparationPendingForever()
    {
        var prepared = O2JamTrackPreparation.LoadAsync(store, "o2jam/8", CancellationToken.None);
        pumpUntil(() => prepared.IsCompleted);
        Assert.That(prepared.GetAwaiter().GetResult(), Is.Null);
    }

    [Test]
    public void CancellationDisposesTrackAfterItsQueuedInitialization()
    {
        var observing = new ObservingStore(store);
        using var cancellation = new CancellationTokenSource();
        var prepared = O2JamTrackPreparation.LoadAsync(observing, "o2jam/7", cancellation.Token);
        var created = observing.Created.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        cancellation.Cancel();
        pumpUntil(() => prepared.IsCompleted && created.IsDisposed);
        Assert.That(prepared.IsCanceled, Is.True);
    }

    private void pumpUntil(Func<bool> completed)
    {
        var started = Stopwatch.StartNew();
        while (!completed() && started.Elapsed < TimeSpan.FromSeconds(5))
        {
            onAudioThread(() =>
            {
                mixer.Update();
                ((AudioComponent)store).Update();
            });
            Thread.Sleep(1);
        }

        Assert.That(completed(), Is.True, "Native audio preparation did not finish.");
    }

    private static void onAudioThread(Action action)
    {
        var property = typeof(ThreadSafety).GetProperty(nameof(ThreadSafety.IsAudioThread))!;
        var previous = ThreadSafety.IsAudioThread;
        property.SetValue(null, true);
        try
        {
            action();
        }
        finally
        {
            property.SetValue(null, previous);
        }
    }

    private static byte[] createWave()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        const int sampleCount = 44100;
        writer.Write("RIFF"u8);
        writer.Write(36 + sampleCount * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(44100);
        writer.Write(88200);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(sampleCount * 2);
        writer.Write(new byte[sampleCount * 2]);
        return stream.ToArray();
    }

    private sealed class ObservingStore(ITrackStore inner) : AdjustableAudioComponent, ITrackStore
    {
        public TaskCompletionSource<Track> Created { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Track Get(string name)
        {
            var track = inner.Get(name);
            Created.TrySetResult(track);
            return track;
        }
        public Task<Track> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Get(name));
        public Track GetVirtual(double length = double.PositiveInfinity, string name = "virtual") => inner.GetVirtual(length, name);
        public Stream GetStream(string name) => inner.GetStream(name);
        public IEnumerable<string> GetAvailableResources() => inner.GetAvailableResources();
    }
}
