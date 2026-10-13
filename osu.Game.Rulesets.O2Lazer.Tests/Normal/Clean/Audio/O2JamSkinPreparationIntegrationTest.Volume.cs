using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using ManagedBass;
using NUnit.Framework;
using O2Jam.Formats.Ojm;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Threading;
using osu.Game.Audio;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamSkinPreparationIntegrationTest
{
    [Test, Combinatorial]
    public void NativeMusicalVoicesFollowGainMatrix([Values("KeySound", "AutomaticSample", "AutomaticTrack")] string route,
                                                  [Values(0, 0.3, 1)] double effect,
                                                  [Values(false, true)] bool beatmapHitsounds)
    {
        Assert.That(O2JamHitSampleLookupPatch.InstallOnce(), Is.True);
        typeof(AudioThread).GetMethod("PreloadBass", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
        var archive = volumeArchive();
        using var fixture = new Fixture(archiveOverride: archive);
        using var provider = new BeatmapSkinProvidingContainer(fixture.Skin);
        provider.BeatmapHitsounds.Value = beatmapHitsounds;
        var inactive = new BindableDouble(1);
        fixture.Manager.AddAdjustment(AdjustableProperty.Volume, inactive);
        fixture.Manager.VolumeSample.Value = effect;
        fixture.Manager.Volume.Value = 0.8;
        fixture.Manager.VolumeTrack.Value = 0.5;
        var voice = createVolumeVoice(fixture, provider, route, out var owner);
        const double voiceVolume = 0.6;
        voice.Volume.Value = voiceVolume;
        pumpVolume(fixture);
        try
        {
            foreach (var (master, music, dimming) in new[]
            {
                (0.8, 0.5, 1.0), (0.8, 0.5, 0.25), (0.0, 0.5, 0.25),
                (0.8, 0.0, 0.25), (1.0, 1.0, 0.25), (0.8, 0.5, 1.0),
            })
            {
                fixture.Manager.Volume.Value = master;
                fixture.Manager.VolumeTrack.Value = music;
                inactive.Value = dimming;
                pumpVolume(fixture);
                var musicalGain = master * music * dimming * voiceVolume;
                Assert.That(voice.AggregateVolume.Value, Is.EqualTo(musicalGain).Within(0.000001));
                Assert.That(nativeVolume(voice), Is.EqualTo(musicalGain).Within(0.000001));
                Assert.That(((AdjustableAudioComponent)fixture.Manager.Samples).AggregateVolume.Value,
                    Is.EqualTo(master * effect * dimming).Within(0.000001), "Ordinary effects keep the native routing.");
                Assert.That(provider.BeatmapHitsounds.Value, Is.EqualTo(beatmapHitsounds));
                Assert.That(fixture.Manager.VolumeSample.Value, Is.EqualTo(effect));
            }
        }
        finally
        {
            fixture.Frame(() => { voice.Dispose(); owner?.Dispose(); owner?.Update(); });
            pumpVolume(fixture);
        }
    }

    [TestCase("KeySound")]
    [TestCase("AutomaticSample")]
    [TestCase("AutomaticTrack")]
    public void VoicesCreatedWhileInactiveKeepDimmingAfterPreparationAndResume(string route)
    {
        Assert.That(O2JamHitSampleLookupPatch.InstallOnce(), Is.True);
        typeof(AudioThread).GetMethod("PreloadBass", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
        using var fixture = new Fixture(archiveOverride: volumeArchive());
        using var provider = new BeatmapSkinProvidingContainer(fixture.Skin);
        provider.BeatmapHitsounds.Value = false;
        var inactive = new BindableDouble(0.25);
        fixture.Manager.AddAdjustment(AdjustableProperty.Volume, inactive);
        fixture.Manager.Volume.Value = 0.8;
        fixture.Manager.VolumeTrack.Value = 0.5;
        fixture.Manager.VolumeSample.Value = 0;
        var voice = createVolumeVoice(fixture, provider, route, out var owner);
        try
        {
            pumpVolume(fixture);
            Assert.That(nativeVolume(voice), Is.EqualTo(0.1).Within(0.000001));
            if (voice is Track track)
            {
                finishVolumeAction(fixture, track.StopAsync());
                finishVolumeAction(fixture, track.StartAsync());
            }
            else { var channel = (SampleChannel)voice; channel.Stop(); pumpVolume(fixture); channel.Play(); }
            pumpVolume(fixture);
            Assert.That(nativeVolume(voice), Is.EqualTo(0.1).Within(0.000001));
            fixture.Manager.VolumeSample.Value = 1;
            pumpVolume(fixture);
            Assert.That(nativeVolume(voice), Is.EqualTo(0.1).Within(0.000001));
            inactive.Value = 1;
            pumpVolume(fixture);
            Assert.That(nativeVolume(voice), Is.EqualTo(0.4).Within(0.000001));
        }
        finally
        {
            fixture.Frame(() => { voice.Dispose(); owner?.Dispose(); owner?.Update(); });
            pumpVolume(fixture);
        }
    }

    private static AdjustableAudioComponent createVolumeVoice(Fixture fixture, BeatmapSkinProvidingContainer provider, string route, out Sample? owner)
    {
        owner = null;
        Assert.That(fixture.Skin.IsReadyForScheduling, Is.True);
        if (route == "AutomaticTrack")
        {
            Assert.That(fixture.Skin.TryGetAutomaticSampleStreaming(2, out var streamed), Is.True);
            Assert.That(streamed, Is.True);
            fixture.Skin.PrefetchBackgroundTrack(2);
            Assert.That(() => { fixture.Frame(); return fixture.Skin.IsBackgroundTrackReady(2); }, Is.True.After(5000, 10));
            var track = fixture.Skin.GetBackgroundTrack(2);
            Assert.That(track, Is.Not.Null);
            track!.Looping = true;
            finishVolumeAction(fixture, track.StartAsync());
            pumpVolume(fixture);
            Assert.That(track.IsLoaded, Is.True);
            return track;
        }

        var id = route == "KeySound" ? 0 : 1;
        Assert.That(fixture.Skin.TryGetAutomaticSampleStreaming(id, out var automaticStreamed), Is.True);
        Assert.That(automaticStreamed, Is.False);
        var sample = route == "KeySound" ? provider.GetSample(new O2JamHitSampleInfo(id, 100, 0))
                                        : fixture.Skin.GetSample(new O2JamHitSampleInfo(id, 100, 0));
        Assert.That(sample, Is.Not.Null);
        // Native DrawableSample normally owns this instance; the audio-only fixture replaces that owner.
        owner = (Sample)sample!;
        Assert.That(() => { fixture.Frame(); return sample!.IsLoaded; }, Is.True.After(5000, 10));
        var channel = sample!.GetChannel();
        channel.Looping = true;
        channel.ManualFree = true;
        channel.Play();
        pumpVolume(fixture);
        Assert.That(channel.Playing, Is.True);
        return channel;
    }

    private static void pumpVolume(Fixture fixture)
    {
        // Native parent bindings and child state changes are queued; drain both directions.
        for (var frame = 0; frame < 3; frame++) fixture.Frame();
    }

    private static void finishVolumeAction(Fixture fixture, Task action)
    {
        // Native synchronous Track methods wait for a running audio thread; this fixture pumps it manually.
        pumpVolume(fixture);
        Assert.That(action.IsCompleted, Is.True, "The native command must drain before awaiting it.");
        action.GetAwaiter().GetResult();
    }

    private static double nativeVolume(AdjustableAudioComponent voice)
    {
        var handle = voice is Track
            ? (int)voice.GetType().GetField("activeStream", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(voice)!
            : (int)voice.GetType().GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(voice)!;
        Assert.That(handle, Is.Not.Zero, "The native gain must be checked on an actual prepared voice.");
        Assert.That(Bass.ChannelGetAttribute(handle, ChannelAttribute.Volume, out var volume), Is.True);
        return volume;
    }

    private static OjmArchive volumeArchive() => new(new Dictionary<int, OjmSample>
    {
        [0] = new OjmSample(0, "key", ".wav", volumeWave(44100)),
        [1] = new OjmSample(1, "automatic", ".wav", volumeWave(44100)),
        [2] = new OjmSample(2, "streamed", ".wav", volumeWave(262144)),
    });

    private static byte[] volumeWave(int frames)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
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
        return memory.ToArray();
    }
}
