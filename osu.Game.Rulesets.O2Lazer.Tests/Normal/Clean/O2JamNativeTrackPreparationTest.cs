using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ManagedBass;
using ManagedBass.Mix;
using NUnit.Framework;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Framework.Audio.Sample;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Audio;
using osu.Framework.Graphics.Containers;
using osu.Framework.Development;
using osu.Framework.Threading;
using osu.Game.Audio;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Core;
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
    public void DetachedGameplayKeySoundPausesAndResumesButStopsOnSeek()
    {
        Assert.That(O2JamHitSampleLookupPatch.InstallOnce(), Is.True);
        var samples = (ISampleStore)Activator.CreateInstance(typeof(Sample).Assembly.GetType("osu.Framework.Audio.Sample.SampleStore")!,
            BindingFlags.Instance | BindingFlags.NonPublic, null, [resources, mixer], null)!;
        var sampleManager = (AudioComponent)samples;
        var paused = new BindableBool();
        var adjustments = new O2JamHitSoundRateAdjustments();
        adjustments.Configure([]);
        adjustments.BindPlaybackDisabled(paused);
        using var container = new AudioContainer<DrawableSample>();
        adjustments.Bind(container);
        try
        {
            var sample = samples.Get("o2jam/7");
            onAudioThread(() =>
            {
                mixer.Update();
                sampleManager.Update();
            });
            using var drawable = new DrawableSample(sample, disposeSampleOnDisposal: false);
            container.Add(drawable);
            // This audio-only fixture does not load the drawable tree. New framework versions
            // assign Parent on load, so reproduce that attachment before testing ancestor lookup.
            var innerContainer = typeof(AudioContainer<DrawableSample>).GetField("container", BindingFlags.Instance | BindingFlags.NonPublic)!
                                                                      .GetValue(container)!;
            var parentProperty = typeof(osu.Framework.Graphics.Drawable).GetProperty(nameof(osu.Framework.Graphics.Drawable.Parent))!;
            parentProperty.SetValue(innerContainer, container);
            parentProperty.SetValue(drawable, innerContainer);
            var channel = drawable.GetChannel();
            channel.Frequency.Value = 1.5;
            onAudioThread(() =>
            {
                mixer.Update();
                channel.Update();
                channel.Play();
                sampleManager.Update();
                mixer.Update();
                channel.Update();
            });
            container.Remove(drawable, false);

            var channelHandle = (int)channel.GetType().GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(channel)!;
            var mixerHandle = (int)mixer.GetType().GetProperty("Handle")!.GetValue(mixer)!;
            var buffer = new float[2048];
            void render() => onAudioThread(() =>
            {
                sampleManager.Update();
                mixer.Update();
                channel.Update();
                Assert.That(Bass.ChannelGetData(mixerHandle, buffer, buffer.Length * sizeof(float)), Is.GreaterThanOrEqualTo(0));
            });

            render();
            // The output mixer advances asynchronously, including the first buffer.
            var initialTimeout = Stopwatch.StartNew();
            while (Bass.ChannelGetPosition(channelHandle) <= 0 && initialTimeout.ElapsedMilliseconds < 500)
            {
                Thread.Sleep(10);
                render();
            }
            var initialPosition = Bass.ChannelGetPosition(channelHandle);
            Assert.That(initialPosition, Is.GreaterThan(0));
            for (var i = 0; i < 3; i++)
            {
                paused.Value = true;
                render();
                Assert.That(BassMix.ChannelHasFlag(channelHandle, BassFlags.MixerChanPause), Is.True, $"position={Bass.ChannelGetPosition(channelHandle)}, frequency={channel.AggregateFrequency.Value}, playing={channel.Playing}");
                var pausedPosition = Bass.ChannelGetPosition(channelHandle);
                Thread.Sleep(30);
                render();
                Assert.That(Bass.ChannelGetPosition(channelHandle), Is.EqualTo(pausedPosition));
                Assert.That(channel.IsDisposed, Is.False);
                Assert.That(channel.Playing, Is.True, "Zero-frequency suspension must not let the sample store reclaim a musical tail.");
                paused.Value = false;
                render();
                Assert.That(BassMix.ChannelHasFlag(channelHandle, BassFlags.MixerChanPause), Is.False);
                Assert.That(channel.AggregateFrequency.Value, Is.EqualTo(1.5), "Resume must preserve pitch/rate rather than multiply it twice.");
                // This fixture uses a native non-decoding output mixer. Reading its buffer
                // does not advance playback synchronously, even on the no-sound device.
                var timeout = Stopwatch.StartNew();
                while (Bass.ChannelGetPosition(channelHandle) <= pausedPosition && timeout.ElapsedMilliseconds < 500)
                {
                    Thread.Sleep(10);
                    render();
                }
                Assert.That(Bass.ChannelGetPosition(channelHandle), Is.GreaterThan(pausedPosition));
                Assert.That((int)channel.GetType().GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(channel)!,
                    Is.EqualTo(channelHandle), "Resume must keep the original voice rather than restart or replace it.");
            }
            adjustments.StopActiveChannels();
            onAudioThread(() =>
            {
                sampleManager.Update();
                mixer.Update();
            });
            Assert.That(channel.Playing, Is.False, "A replay seek must discard the previous position's musical tail.");
            paused.Value = true;
            paused.Value = false;
            onAudioThread(() =>
            {
                sampleManager.Update();
                mixer.Update();
            });
            Assert.That(channel.Playing, Is.False, "Pause and resume must not revive a voice discarded by seek.");
        }
        finally
        {
            adjustments.UnbindAll();
            onAudioThread(() =>
            {
                sampleManager.Dispose();
                sampleManager.Update();
            });
        }
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
    public void PreviewTrackResumesPreparedNativeBackground()
    {
        var prepared = O2JamTrackPreparation.LoadAsync(store, "o2jam/7", CancellationToken.None);
        pumpUntil(() => prepared.IsCompleted);
        var background = prepared.GetAwaiter().GetResult()!;
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 7, 100, 0));
        var resource = new SingleTrackPlaybackResource(background);
        using var preview = new O2JamPreviewTrack(beatmap, resource, store.GetVirtual(10_000));

        onAudioThread(preview.Start);
        pumpUntil(preview, () => preview.IsRunning && background.IsRunning);
        onAudioThread(preview.Stop);
        pumpUntil(preview, () => !preview.IsRunning && !background.IsRunning);
        onAudioThread(preview.Start);
        pumpUntil(preview, () => preview.IsRunning && background.IsRunning);

        Assert.That(resource.TrackRequests, Is.EqualTo(1));
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

    private void pumpUntil(O2JamPreviewTrack preview, Func<bool> completed)
    {
        var started = Stopwatch.StartNew();
        while (!completed() && started.Elapsed < TimeSpan.FromSeconds(5))
        {
            onAudioThread(() =>
            {
                preview.Update();
                mixer.Update();
                ((AudioComponent)store).Update();
            });
            Thread.Sleep(1);
        }

        Assert.That(completed(), Is.True, "O2Jam preview background did not reach the expected playback state.");
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

    private sealed class SingleTrackPlaybackResource(Track track) : IO2JamPlaybackResource
    {
        public int TrackRequests { get; private set; }

        public bool ContainsSample(int sampleId) => sampleId == 7;

        public ISample? GetSample(ISampleInfo sampleInfo) => null;

        public Track GetBackgroundTrack(int sampleId)
        {
            TrackRequests++;
            return track;
        }
    }
}
