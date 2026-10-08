using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using O2Jam.Core;
using osu.Framework.Audio.Sample;
using osu.Framework.Audio.Track;
using osu.Game.Audio;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.Objects;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamPreviewTrackLifecycleTest
{
    [TestCase(typeof(O2JamModHalfTime), 1, 0.75)]
    [TestCase(typeof(O2JamModDaycore), 0.75, 1)]
    [TestCase(typeof(O2JamModDoubleTime), 1, 1.5)]
    [TestCase(typeof(O2JamModNightcore), 1.5, 1)]
    public void RateModsReachClockBackgroundAndAutomaticSample(Type modType, double frequency, double tempo)
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 1000, 100, 0));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 7, 100, 0, O2JamAudioEventKind.KeySound));
        var resources = new SamplePlaybackResource();
        var clock = new FakeTrack(10_000);
        using var preview = new O2JamPreviewTrack(beatmap, resources, clock);
        var mod = (IApplicableToTrack)Activator.CreateInstance(modType)!;
        mod.ApplyToTrack(preview);
        start(preview);
        assertAdjustments(frequency, tempo);

        // Active voices must follow aggregate changes too, without waiting for another note.
        preview.Frequency.Value = 1.1;
        preview.Tempo.Value = 1.2;
        assertAdjustments(frequency * 1.1, tempo * 1.2);

        stop(preview);
        preview.Seek(2000);
        start(preview);
        Assert.That(resources.Tracks[^1].LastSeek, Is.EqualTo(2000));
        Assert.That(resources.Tracks[^1].AggregateFrequency.Value, Is.EqualTo(frequency * 1.1).Within(0.000001));
        Assert.That(resources.Tracks[^1].AggregateTempo.Value, Is.EqualTo(tempo * 1.2).Within(0.000001));

        void assertAdjustments(double expectedFrequency, double expectedTempo)
        {
            preview.Update();
            var background = resources.Tracks[0];
            var channel = resources.Sample.Channel!;
            background.Update();
            channel.Update();
            Assert.Multiple(() =>
            {
                Assert.That(clock.Rate, Is.EqualTo(expectedFrequency * expectedTempo).Within(0.000001));
                Assert.That(background.AggregateFrequency.Value, Is.EqualTo(expectedFrequency).Within(0.000001));
                Assert.That(background.AggregateTempo.Value, Is.EqualTo(expectedTempo).Within(0.000001));
                Assert.That(channel.AggregateFrequency.Value, Is.EqualTo(expectedFrequency).Within(0.000001));
                Assert.That(channel.AggregateTempo.Value, Is.EqualTo(expectedTempo).Within(0.000001));
            });
        }
    }

    [TestCase(typeof(O2JamModHalfTime), 0.75)]
    [TestCase(typeof(O2JamModDoubleTime), 1.5)]
    public void AdjustPitchSettingReachesClockBackgroundAndAutomaticSample(Type modType, double speed)
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 1000, 100, 0));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 7, 100, 0, O2JamAudioEventKind.KeySound));
        var resources = new SamplePlaybackResource();
        var clock = new FakeTrack(10_000);
        using var preview = new O2JamPreviewTrack(beatmap, resources, clock);
        var mod = (ModRateAdjust)Activator.CreateInstance(modType)!;
        ((IApplicableToTrack)mod).ApplyToTrack(preview);
        start(preview);

        switch (mod)
        {
            case ModHalfTime halfTime:
                halfTime.AdjustPitch.Value = true;
                break;

            case ModDoubleTime doubleTime:
                doubleTime.AdjustPitch.Value = true;
                break;
        }

        preview.Update();
        var background = resources.Tracks[0];
        var channel = resources.Sample.Channel!;
        background.Update();
        channel.Update();

        Assert.Multiple(() =>
        {
            Assert.That(clock.Rate, Is.EqualTo(speed).Within(0.000001));
            Assert.That(background.AggregateFrequency.Value, Is.EqualTo(speed).Within(0.000001));
            Assert.That(background.AggregateTempo.Value, Is.EqualTo(1).Within(0.000001));
            Assert.That(channel.AggregateFrequency.Value, Is.EqualTo(speed).Within(0.000001));
            Assert.That(channel.AggregateTempo.Value, Is.EqualTo(1).Within(0.000001));
        });
    }

#if O2JAM_SYNC_DIAGNOSTICS
    [Test]
    [SetCulture("fr-FR")]
    public void SyncObservationDoesNotCorrectClocksOrRestartBackgroundAudio()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 1000, 80, 0));
        var resources = new FakePlaybackResource();
        var clock = new FakeTrack(10_000);
        using var preview = new O2JamPreviewTrack(beatmap, resources, clock)
        {
            PlaybackMode = O2JamPreviewPlaybackMode.Gameplay,
        };
        var entries = new List<string>();
        var session = new Diagnostics.O2JamSyncSession(entries.Add);
        preview.AttachSyncDiagnostics(session);
        start(preview);
        clock.Seek(100);
        preview.Update();
        var background = resources.Tracks[0];
        background.Seek(140);
        var state = new Diagnostics.O2JamSyncState(true, false, false, false, false, 1);
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        Assert.That(session.TryBeginSample(now, state), Is.True);
        preview.RequestSyncSample(session, new Diagnostics.O2JamSyncGameplaySample(now, preview.SyncEpoch, state,
            90, 90, 100, -10, 1, new Diagnostics.O2JamSyncHitSummary(0, null, null, null)));
        preview.Update();
        preview.DetachSyncDiagnostics(session);
        preview.Update();

        Assert.Multiple(() =>
        {
            Assert.That(preview.CurrentTime, Is.EqualTo(100));
            Assert.That(background.CurrentTime, Is.EqualTo(140));
            Assert.That(background.StartCount, Is.EqualTo(1));
            Assert.That(background.StopCount, Is.Zero);
            Assert.That(resources.Tracks, Has.Count.EqualTo(1));
            Assert.That(preview.IsRunning, Is.True);
            Assert.That(entries, Has.Some.Contains("action=sample"));
            Assert.That(entries, Has.Some.Contains("lead_virtual_ms=40.000"));
            Assert.That(entries, Has.Some.Contains("total_offset_ms=-10.000"));
            Assert.That(entries, Has.Some.Contains("judgement_ms=90.000"));
        });
    }
#endif

    [Test]
    public void BackgroundLayerPausesResumesAndSeeksWithoutBecomingAKeysound()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 1000, 80, -0.25f));
        var resources = new FakePlaybackResource();
        var clock = new FakeTrack(10_000);
        var preview = new O2JamPreviewTrack(beatmap, resources, clock);

        start(preview);
        preview.Update();
        var initialLayer = resources.Tracks[0];
        foreach (var time in new[] { 400d, 800d, 1200d })
        {
            clock.Seek(time);
            preview.Update();
        }

        stop(preview);
        preview.Update();
        start(preview);
        preview.Update();

        Assert.Multiple(() =>
        {
            Assert.That(initialLayer.StopCount, Is.EqualTo(1));
            Assert.That(initialLayer.StartCount, Is.EqualTo(2));
            Assert.That(initialLayer.LastSeek, Is.EqualTo(1200));
            Assert.That(resources.SampleRequests, Is.Zero);
        });

        preview.Seek(2500);
        preview.Update();
        var restoredLayer = resources.Tracks[1];

        Assert.Multiple(() =>
        {
            Assert.That(restoredLayer.LastSeek, Is.EqualTo(2500));
            Assert.That(restoredLayer.StartCount, Is.EqualTo(1));
            Assert.That(restoredLayer.Volume.Value, Is.EqualTo(0.8).Within(0.001));
            Assert.That(restoredLayer.Balance.Value, Is.EqualTo(-0.25).Within(0.001));
        });
    }

    [Test]
    public void ActiveAutomaticSamplePausesInPlaceAndResumes()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 7, 100, 0, O2JamAudioEventKind.KeySound));
        var resources = new SamplePlaybackResource();
        using var preview = new O2JamPreviewTrack(beatmap, resources, new FakeTrack(10_000));

        start(preview);
        preview.Update();
        var channel = resources.Sample.Channel!;
        channel.Update();
        stop(preview);
        preview.Update();
        channel.Update();

        Assert.Multiple(() =>
        {
            Assert.That(channel.StopCount, Is.Zero);
            Assert.That(channel.Playing, Is.True);
            Assert.That(channel.AggregateFrequency.Value, Is.Zero);
        });

        start(preview);
        preview.Update();
        channel.Update();

        Assert.Multiple(() =>
        {
            Assert.That(channel.StopCount, Is.Zero);
            Assert.That(channel.Playing, Is.True);
            Assert.That(channel.AggregateFrequency.Value, Is.EqualTo(1));
        });
    }

    [Test]
    public void LateBackgroundPreparationDoesNotBlockReadyAutomaticKeysound()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(50, 1000, 100, 0));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(75, 7, 100, 0, O2JamAudioEventKind.KeySound));
        var resources = new DeferredBackgroundOnlyResource();
        var clock = new FakeTrack(10_000);
        using var preview = new O2JamPreviewTrack(beatmap, resources, clock)
        {
            PlaybackMode = O2JamPreviewPlaybackMode.Gameplay,
        };

        start(preview);
        preview.Update();
        clock.Seek(100);
        preview.Update();
        Assert.That(resources.PlaybackRequests, Is.EqualTo(new[] { 7 }));

        resources.BackgroundReady = true;
        preview.Update();
        Assert.Multiple(() =>
        {
            Assert.That(resources.PlaybackRequests, Is.EqualTo(new[] { 7, 1000 }));
            Assert.That(resources.Tracks[0].LastSeek, Is.EqualTo(50));
        });
    }

    [Test]
    public void MissingZeroKeysoundDoesNotDropFollowingAudio()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 0, 100, 0, O2JamAudioEventKind.KeySound));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 7, 100, 0, O2JamAudioEventKind.KeySound));
        var resources = new MissingZeroPlaybackResource();
        using var preview = new O2JamPreviewTrack(beatmap, resources, new FakeTrack(10_000));

        start(preview);
        preview.Update();

        Assert.Multiple(() =>
        {
            Assert.That(resources.PlaybackRequests, Is.EqualTo(new[] { 0, 7 }));
            Assert.That(resources.Sample.Channels, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void PreviewDispatchesBackgroundAndKeysoundsInChartOrder()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 1000, 100, 0));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(50, 7, 100, 0, O2JamAudioEventKind.KeySound));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(100, 1001, 100, 0));
        beatmap.HitObjects.Add(new O2JamNote { StartTime = 75, Samples = [new O2JamHitSampleInfo(8, 100, 0)] });
        var resources = new FakePlaybackResource();
        var clock = new FakeTrack(10_000);
        using var preview = new O2JamPreviewTrack(beatmap, resources, clock);

        start(preview);
        clock.Seek(100);
        preview.Update();

        Assert.That(resources.PlaybackRequests, Is.EqualTo(new[] { 1000, 7, 8, 1001 }));
    }

    [Test]
    public void PauseSuspendsBackgroundAndAutomaticKeysoundTogether()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 1000, 100, 0));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 7, 100, 0, O2JamAudioEventKind.KeySound));
        var resources = new SamplePlaybackResource();
        using var preview = new O2JamPreviewTrack(beatmap, resources, new FakeTrack(10_000));

        start(preview);
        preview.Update();
        var background = resources.Tracks[0];
        var channel = resources.Sample.Channel!;
        channel.Update();

        stop(preview);
        preview.Update();
        channel.Update();
        Assert.Multiple(() =>
        {
            Assert.That(background.StopCount, Is.EqualTo(1));
            Assert.That(channel.StopCount, Is.Zero);
            Assert.That(channel.AggregateFrequency.Value, Is.Zero);
        });

        start(preview);
        preview.Update();
        channel.Update();
        Assert.Multiple(() =>
        {
            Assert.That(resources.Tracks[0], Is.SameAs(background));
            Assert.That(background.StartCount, Is.EqualTo(2));
            Assert.That(channel.AggregateFrequency.Value, Is.EqualTo(1));
        });
    }

    [Test]
    public void SeekStopsOldAutomaticKeysoundAndReplaysItOnlyAfterRewind()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 7, 100, 0, O2JamAudioEventKind.KeySound));
        var resources = new SamplePlaybackResource();
        using var preview = new O2JamPreviewTrack(beatmap, resources, new FakeTrack(10_000));

        start(preview);
        preview.Update();
        var oldChannel = resources.Sample.Channel!;

        preview.Seek(2000);
        preview.Update();
        Assert.Multiple(() =>
        {
            Assert.That(oldChannel.StopCount, Is.EqualTo(1));
            Assert.That(resources.Sample.Channel, Is.SameAs(oldChannel));
        });

        preview.Seek(0);
        preview.Update();
        Assert.That(resources.Sample.Channel, Is.Not.SameAs(oldChannel));
    }

    [Test]
    public void GameplayAutomaticPolicyPlaysChartedKeysoundsWithoutAVisibleMod()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 7, 100, 0, O2JamAudioEventKind.KeySound));
        beatmap.HitObjects.Add(new O2JamNote { StartTime = 0, Samples = [new O2JamHitSampleInfo(8, 100, 0)] });
        var resources = new FakePlaybackResource();
        using var preview = new O2JamPreviewTrack(beatmap, resources, new FakeTrack(10_000))
        {
            PlaybackMode = O2JamPreviewPlaybackMode.GameplayAutomatic,
        };

        start(preview);
        preview.Update();

        Assert.That(resources.PlaybackRequests, Is.EqualTo(new[] { 7, 8 }));
    }

    [Test]
    public void EnteringJudgementGameplayKeepsAutomaticTailButStopsPreviewOnlyVoice()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 7, 100, 0, O2JamAudioEventKind.KeySound));
        beatmap.HitObjects.Add(new O2JamNote { StartTime = 0, Samples = [new O2JamHitSampleInfo(8, 100, 0)] });
        var resources = new SamplePlaybackResource();
        using var preview = new O2JamPreviewTrack(beatmap, resources, new FakeTrack(10_000));

        start(preview);
        preview.Update();
        Assert.That(resources.Sample.Channels, Has.Count.EqualTo(2));
        var automatic = resources.Sample.Channels[0];
        var playable = resources.Sample.Channels[1];

        preview.PlaybackMode = O2JamPreviewPlaybackMode.Gameplay;
        preview.Update();

        Assert.Multiple(() =>
        {
            Assert.That(automatic.StopCount, Is.Zero);
            Assert.That(playable.StopCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void TransferUsesBackgroundAudioIdentityRatherThanDifficultyTiming()
    {
        var source = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        source.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 1000, 80, 0));
        source.AutomaticAudioEvents.Add(new O2JamAudioEvent(100, 7, 80, 0, O2JamAudioEventKind.KeySound));
        var sameSong = new O2JamBeatmap(O2JamDifficulty.NX, new O2JamTimingMap(120));
        sameSong.AutomaticAudioEvents.Add(new O2JamAudioEvent(12, 1000, 55, -0.5f));
        sameSong.AutomaticAudioEvents.Add(new O2JamAudioEvent(12, 1001, 55, -0.5f));
        sameSong.AutomaticAudioEvents.Add(new O2JamAudioEvent(50, 19, 80, 0, O2JamAudioEventKind.KeySound));
        var differentSong = new O2JamBeatmap(O2JamDifficulty.HX, new O2JamTimingMap(120));
        differentSong.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 2000, 80, 0));
        using var preview = new O2JamPreviewTrack(source, new FakePlaybackResource(), new FakeTrack(10_000));

        Assert.Multiple(() =>
        {
            Assert.That(preview.CanTransferSchedule(sameSong), Is.True);
            Assert.That(preview.CanTransferSchedule(differentSong), Is.False);
        });
    }

    [Test]
    public void ClockWaitsForInitialBackgroundDecoder()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 1000, 80, 0));
        var resources = new DeferredPlaybackResource();
        var clock = new FakeTrack(10_000);
        var preview = new O2JamPreviewTrack(beatmap, resources, clock);

        start(preview);
        preview.Update();

        Assert.Multiple(() =>
        {
            Assert.That(clock.StartCount, Is.Zero);
            Assert.That(resources.Tracks, Is.Empty);
        });

        resources.IsReady = true;
        preview.Update();

        Assert.Multiple(() =>
        {
            Assert.That(clock.StartCount, Is.EqualTo(1));
            Assert.That(resources.Tracks, Has.Count.EqualTo(1));
            Assert.That(resources.Tracks[0].StartCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void PlaybackResourceLeaseOutlivesChildTracks()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 1000, 80, 0));
        var resources = new LeasedPlaybackResource();
        var preview = new O2JamPreviewTrack(beatmap, resources, new FakeTrack(10_000));

        start(preview);
        preview.Update();
        preview.Dispose();
        preview.Update();

        Assert.Multiple(() =>
        {
            Assert.That(resources.LeaseReleased, Is.True);
            Assert.That(resources.TrackStopCountWhenReleased, Is.Zero);
        });

        // AudioComponent disposal is queued. The owning TrackStore performs this update before its
        // own queued disposal because the preview releases its resource lease last.
        resources.Tracks[0].Update();
        Assert.That(resources.Tracks[0].IsDisposed, Is.True);
    }

    [Test]
    public void DisposalDoesNotStopABackgroundTrackWhoseMixerIsShuttingDown()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 1000, 80, 0));
        var resources = new ShutdownPlaybackResource();
        var preview = new O2JamPreviewTrack(beatmap, resources, new FakeTrack(10_000));

        start(preview);
        preview.Update();
        resources.Track.RejectStop = true;

        Assert.That(() =>
        {
            preview.Dispose();
            preview.Update();
        }, Throws.Nothing);
    }

    [Test]
    public void PreviewAlwaysRequestsPlayableKeysounds()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.HitObjects.Add(new O2JamNote
        {
            StartTime = 0,
            Samples = [new O2JamHitSampleInfo(7, 100, 0)],
        });
        var resources = new FakePlaybackResource();
        var preview = new O2JamPreviewTrack(beatmap, resources, new FakeTrack(10_000));

        start(preview);
        preview.Update();

        Assert.That(resources.SampleRequests, Is.EqualTo(1));
    }

    [Test]
    public void PreviewDoesNotPreloadOrTriggerLongNoteTailKeysounds()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.HitObjects.Add(new O2JamHoldNote
        {
            StartTime = 0,
            Duration = 1000,
            NodeSamples =
            [
                [new O2JamHitSampleInfo(7, 100, 0)],
                [new O2JamHitSampleInfo(8, 100, 0)],
            ],
        });
        var resources = new FakePlaybackResource();
        var clock = new FakeTrack(10_000);
        using var preview = new O2JamPreviewTrack(beatmap, resources, clock);

        start(preview);
        preview.Update();
        Assert.That(resources.SampleRequests, Is.EqualTo(1));

        clock.Seek(1000);
        preview.Update();

        Assert.Multiple(() =>
        {
            Assert.That(resources.SampleRequests, Is.EqualTo(1));
            Assert.That(resources.SampleReadinessRequests, Does.Contain(7));
            Assert.That(resources.SampleReadinessRequests, Does.Not.Contain(8));
        });
    }

    [Test]
    public void PreviewPreloadsKeysoundsTenSecondsAhead()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 1000, 100, 0));
        beatmap.HitObjects.Add(new O2JamNote
        {
            StartTime = 9000,
            Samples = [new O2JamHitSampleInfo(7, 100, 0)],
        });
        beatmap.HitObjects.Add(new O2JamNote
        {
            StartTime = 10_001,
            Samples = [new O2JamHitSampleInfo(8, 100, 0)],
        });
        var resources = new FakePlaybackResource();
        var preview = new O2JamPreviewTrack(beatmap, resources, new FakeTrack(20_000));

        start(preview);
        preview.Update();

        Assert.Multiple(() =>
        {
            Assert.That(resources.SampleReadinessRequests, Does.Contain(7));
            Assert.That(resources.SampleReadinessRequests, Does.Not.Contain(8));
        });
    }

    [Test]
    public void GameplayStillPlaysAutomaticKeysoundsAsSamples()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 7, 100, 0, O2JamAudioEventKind.KeySound));
        beatmap.HitObjects.Add(new O2JamNote
        {
            StartTime = 0,
            Samples = [new O2JamHitSampleInfo(8, 100, 0)],
        });
        var resources = new FakePlaybackResource();
        var preview = new O2JamPreviewTrack(beatmap, resources, new FakeTrack(10_000))
        {
            PlaybackMode = O2JamPreviewPlaybackMode.Gameplay,
        };

        start(preview);
        preview.Update();

        Assert.Multiple(() =>
        {
            Assert.That(resources.SampleRequests, Is.EqualTo(1));
            Assert.That(resources.Tracks, Is.Empty);
        });
    }

    [Test]
    public void SongLengthAutomaticKeysoundUsesStreamingTrack()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 1, 100, 0, O2JamAudioEventKind.KeySound));
        var resources = new StreamingPlaybackResource();
        var preview = new O2JamPreviewTrack(beatmap, resources, new FakeTrack(10_000));

        start(preview);
        preview.Update();

        Assert.Multiple(() =>
        {
            Assert.That(resources.Tracks, Has.Count.EqualTo(1));
            Assert.That(resources.SampleRequests, Is.Zero);
        });
    }

    [Test]
    public void DifferentSongLengthAutomaticKeysoundsCannotTransfer()
    {
        var source = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        source.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 1, 100, 0, O2JamAudioEventKind.KeySound));
        var sameSong = new O2JamBeatmap(O2JamDifficulty.NX, new O2JamTimingMap(120));
        sameSong.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 1, 100, 0, O2JamAudioEventKind.KeySound));
        var differentSong = new O2JamBeatmap(O2JamDifficulty.HX, new O2JamTimingMap(120));
        differentSong.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 2, 100, 0, O2JamAudioEventKind.KeySound));
        using var preview = new O2JamPreviewTrack(source, new StreamingPlaybackResource(), new FakeTrack(10_000));

        Assert.Multiple(() =>
        {
            Assert.That(preview.CanTransferSchedule(sameSong), Is.True);
            Assert.That(preview.CanTransferSchedule(differentSong), Is.False);
        });
    }

    [TestCase(O2JamPreviewPlaybackMode.Preview, 2000)]
    [TestCase(O2JamPreviewPlaybackMode.Gameplay, 0)]
    public void OnlyPreviewSkipsChartedLeadInSilence(O2JamPreviewPlaybackMode mode, double expectedTime)
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(2000, 1000, 100, 0));
        var resources = new FakePlaybackResource();
        var clock = new FakeTrack(10_000);
        using var preview = new O2JamPreviewTrack(beatmap, resources, clock) { PlaybackMode = mode };

        start(preview);
        preview.Update();

        Assert.That(preview.CurrentTime, Is.EqualTo(expectedTime));
        Assert.That(resources.Tracks.Count, Is.EqualTo(mode == O2JamPreviewPlaybackMode.Preview ? 1 : 0));
    }

    [Test]
    public void SeekWaitsForRestoredBackgroundInsteadOfDroppingIt()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 1000, 100, 0));
        var resources = new DeferredPlaybackResource { IsReady = true };
        var clock = new FakeTrack(10_000);
        using var preview = new O2JamPreviewTrack(beatmap, resources, clock);
        start(preview);
        preview.Update();
        resources.IsReady = false;

        preview.Seek(2500);
        preview.Update();
        Assert.That(preview.IsRunning, Is.False);
        Assert.That(resources.Tracks, Has.Count.EqualTo(1));

        resources.IsReady = true;
        preview.Update();
        Assert.Multiple(() =>
        {
            Assert.That(preview.IsRunning, Is.True);
            Assert.That(resources.Tracks, Has.Count.EqualTo(2));
            Assert.That(resources.Tracks[1].LastSeek, Is.EqualTo(2500));
            Assert.That(resources.Tracks[1].StartCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void CancellingStartWhileDecoderIsPendingDoesNotResumePlayback()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 1000, 100, 0));
        var resources = new DeferredPlaybackResource();
        using var preview = new O2JamPreviewTrack(beatmap, resources, new FakeTrack(10_000));
        start(preview);
        stop(preview);
        resources.IsReady = true;
        preview.Update();

        Assert.That(preview.IsRunning, Is.False);
        Assert.That(resources.Tracks, Is.Empty);
    }

    [Test]
    public void ChangingDifficultyRetainsClockAndActiveBackground()
    {
        var first = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        first.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 1000, 100, 0));
        var second = new O2JamBeatmap(O2JamDifficulty.NX, new O2JamTimingMap(120));
        second.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 1000, 100, 0));
        var resources = new FakePlaybackResource();
        var clock = new FakeTrack(10_000);
        using var preview = new O2JamPreviewTrack(first, resources, clock);
        start(preview);
        clock.Seek(400);
        preview.Update();
        preview.ReplaceSchedule(second);
        preview.Update();

        Assert.Multiple(() =>
        {
            Assert.That(preview.CurrentTime, Is.EqualTo(400));
            Assert.That(resources.Tracks, Has.Count.EqualTo(1));
            Assert.That(resources.Tracks[0].StartCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void GameplayPreloadsFutureKeysoundsWithoutAutomaticallyPlayingThem()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.HitObjects.Add(new O2JamNote { StartTime = 15000, Samples = [new O2JamHitSampleInfo(7, 100, 0)] });
        var resources = new FakePlaybackResource();
        using var preview = new O2JamPreviewTrack(beatmap, resources, new FakeTrack(20_000))
        {
            PlaybackMode = O2JamPreviewPlaybackMode.Gameplay,
        };
        start(preview);
        preview.Seek(6000);
        preview.Update();

        Assert.That(resources.SampleReadinessRequests, Does.Contain(7));
        Assert.That(resources.SampleRequests, Is.Zero);
    }

    [Test]
    public void EmptyIntroDetectionWaitsForArchiveAndIgnoresMissingSamples()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 999, 100, 0));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(1000, 1000, 0, 0));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(2000, 1000, 100, 0));
        var resources = new DeferredArchiveResource();
        using var preview = new O2JamPreviewTrack(beatmap, resources, new FakeTrack(10_000));
        start(preview);
        preview.Update();
        Assert.That(preview.IsRunning, Is.False);

        resources.ArchiveReady = true;
        preview.Update();
        Assert.That(preview.CurrentTime, Is.EqualTo(2000));
        Assert.That(preview.IsRunning, Is.True);
    }

    [Test]
    public void LateBackgroundDecoderIsRetriedAtTheCorrectOffset()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(100, 1000, 100, 0));
        var resources = new DeferredPlaybackResource();
        var clock = new FakeTrack(10_000);
        using var preview = new O2JamPreviewTrack(beatmap, resources, clock) { PlaybackMode = O2JamPreviewPlaybackMode.Gameplay };
        start(preview);
        clock.Seek(100);
        preview.Update();
        Assert.That(resources.Tracks, Is.Empty);

        clock.Seek(150);
        resources.IsReady = true;
        preview.Update();
        Assert.That(resources.Tracks, Has.Count.EqualTo(1));
        Assert.That(resources.Tracks[0].LastSeek, Is.EqualTo(50));
    }

    [Test]
    public void PausedSeekDoesNotRestartWhenPreparationCompletes()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 1000, 100, 0));
        var resources = new DeferredPlaybackResource { IsReady = true };
        using var preview = new O2JamPreviewTrack(beatmap, resources, new FakeTrack(10_000));
        start(preview);
        stop(preview);
        resources.IsReady = false;
        preview.Seek(2500);
        preview.Update();
        resources.IsReady = true;
        preview.Update();
        Assert.That(preview.IsRunning, Is.False);
        Assert.That(resources.Tracks, Has.Count.EqualTo(1));

        start(preview);
        preview.Update();
        Assert.That(resources.Tracks, Has.Count.EqualTo(2));
        Assert.That(resources.Tracks[1].LastSeek, Is.EqualTo(2500));
    }

    [Test]
    public void UnchangedClockDoesNotRepeatEntireLookahead()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 1000, 100, 0));
        beatmap.HitObjects.Add(new O2JamNote { StartTime = 9000, Samples = [new O2JamHitSampleInfo(7, 100, 0)] });
        var resources = new FakePlaybackResource();
        using var preview = new O2JamPreviewTrack(beatmap, resources, new FakeTrack(10_000));
        start(preview);
        var requests = resources.SampleReadinessRequests.Count;
        for (var frame = 0; frame < 50; frame++)
            preview.Update();

        Assert.That(requests, Is.GreaterThan(0));
        Assert.That(resources.SampleReadinessRequests, Has.Count.EqualTo(requests));
    }

    private static void start(O2JamPreviewTrack preview) => runAudioAction(preview, preview.Start);

    private static void stop(O2JamPreviewTrack preview) => runAudioAction(preview, preview.Stop);

    private static void runAudioAction(O2JamPreviewTrack preview, Action action)
    {
        var task = Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        while (!task.IsCompleted)
        {
            preview.Update();
            Thread.Yield();
        }

        task.GetAwaiter().GetResult();
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    public void DifficultyTransferPreservesOnlyIdenticalActiveSampleEvents(int difference)
    {
        var first = transferBeatmap();
        var second = transferBeatmap(automatic: difference != 5, sampleId: difference == 1 ? 8 : 7,
                                     time: difference == 2 ? 1 : 0, volume: difference == 3 ? 90 : 100,
                                     pan: difference == 4 ? 0.5f : 0);
        var resources = new SamplePlaybackResource();
        var clock = new FakeTrack(10_000);
        using var preview = new O2JamPreviewTrack(first, resources, clock);
        start(preview);
        clock.Seek(400);
        preview.Update();
        var channel = resources.Sample.Channel!;
        preview.ReplaceSchedule(second);
        preview.Update();
        Assert.Multiple(() =>
        {
            Assert.That(preview.CurrentTime, Is.EqualTo(400));
            Assert.That(resources.Sample.Channels, Has.Count.EqualTo(1), "Transfer must not replay past samples.");
            Assert.That(channel.StopCount, Is.EqualTo(difference == 0 ? 0 : 1));
            Assert.That(channel.Playing, Is.EqualTo(difference == 0));
            Assert.That(resources.Tracks, Is.Empty, "Retaining a voice must not create a seekable track.");
        });
    }

    [TestCase(1, 1)]
    [TestCase(2, 1)]
    [TestCase(1, 2)]
    public void DifficultyTransferMatchesDuplicateVoiceCounts(int sourceCount, int targetCount)
    {
        var resources = new SamplePlaybackResource();
        var clock = new FakeTrack(10_000);
        using var preview = new O2JamPreviewTrack(transferBeatmap(count: sourceCount), resources, clock);
        start(preview);
        clock.Seek(400);
        preview.Update();
        preview.ReplaceSchedule(transferBeatmap(count: targetCount));
        preview.Update();
        var retained = 0;
        foreach (var channel in resources.Sample.Channels)
            if (channel.Playing)
                retained++;
        Assert.That(retained, Is.EqualTo(Math.Min(sourceCount, targetCount)));
        Assert.That(resources.Sample.Channels, Has.Count.EqualTo(sourceCount));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void RetainedSampleStaysPausedThenResumesAndStillStopsOnSeek(bool automatic)
    {
        var resources = new SamplePlaybackResource();
        var clock = new FakeTrack(10_000);
        using var preview = new O2JamPreviewTrack(transferBeatmap(automatic: automatic), resources, clock);
        start(preview);
        clock.Seek(400);
        preview.Update();
        var channel = resources.Sample.Channel!;
        stop(preview);
        preview.Update();
        preview.ReplaceSchedule(transferBeatmap(automatic: automatic));
        preview.Update();
        channel.Update();
        Assert.That(channel.StopCount, Is.Zero);
        Assert.That(channel.AggregateFrequency.Value, Is.Zero);
        start(preview);
        preview.Update();
        channel.Update();
        Assert.That(channel.AggregateFrequency.Value, Is.EqualTo(1));
        Assert.That(resources.Sample.Channels, Has.Count.EqualTo(1));
        preview.Seek(450);
        preview.Update();
        Assert.That(channel.StopCount, Is.EqualTo(1));
    }

    [TestCase(O2JamPreviewPlaybackMode.Gameplay)]
    [TestCase(O2JamPreviewPlaybackMode.GameplayAutomatic)]
    public void GameplayScheduleReplacementStillStopsExistingSampleVoices(O2JamPreviewPlaybackMode mode)
    {
        var resources = new SamplePlaybackResource();
        var clock = new FakeTrack(10_000);
        using var preview = new O2JamPreviewTrack(transferBeatmap(), resources, clock) { PlaybackMode = mode };
        start(preview);
        clock.Seek(400);
        preview.Update();
        var channel = resources.Sample.Channel!;
        preview.ReplaceSchedule(transferBeatmap());
        preview.Update();
        Assert.That(channel.StopCount, Is.EqualTo(1));
        Assert.That(resources.Sample.Channels, Has.Count.EqualTo(1));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void TransferRestoresMissingLiveSampleWithoutHoldingClock(bool automatic)
    {
        var resources = new RestorablePlaybackResource { DurationReady = false };
        var clock = new FakeTrack(10_000);
        using var preview = new O2JamPreviewTrack(transferBeatmap(count: 0), resources, clock);
        start(preview);
        clock.Seek(400);
        preview.Update();
        preview.ReplaceSchedule(transferBeatmap(automatic: automatic, time: 100, volume: 80, pan: 0.25f));
        preview.Update();
        Assert.That(clock.IsRunning, Is.True);
        Assert.That(resources.Tracks, Is.Empty);
        clock.Seek(450);
        resources.DurationReady = true;
        preview.Update();
        Assert.That(resources.Tracks, Has.Count.EqualTo(1));
        var restored = resources.Tracks[0];
        Assert.That(restored.LastSeek, Is.EqualTo(350));
        Assert.That(restored.IsRunning, Is.True);
        Assert.That(restored.Volume.Value, Is.EqualTo(0.8));
        Assert.That(restored.Balance.Value, Is.EqualTo(0.25));
        stop(preview);
        Assert.That(restored.IsRunning, Is.False);
        start(preview);
        Assert.That(restored.IsRunning, Is.True);
        preview.ReplaceSchedule(transferBeatmap(automatic: automatic, time: 100, volume: 80, pan: 0.25f));
        preview.Update();
        Assert.That(resources.Tracks, Has.Count.EqualTo(1), "Retain the reconstructed voice rather than duplicate it.");
    }

    [Test]
    public void ExpiredSampleDoesNotCreateRestorationStream()
    {
        var resources = new RestorablePlaybackResource { SampleLength = 100 };
        var clock = new FakeTrack(10_000);
        using var preview = new O2JamPreviewTrack(transferBeatmap(count: 0), resources, clock);
        start(preview);
        clock.Seek(400);
        preview.Update();
        preview.ReplaceSchedule(transferBeatmap());
        preview.Update();
        Assert.That(resources.Tracks, Is.Empty);
    }

    [Test]
    public void SupersedingScheduleCancelsPendingRestoration()
    {
        var resources = new RestorablePlaybackResource { DurationReady = false };
        var clock = new FakeTrack(10_000);
        using var preview = new O2JamPreviewTrack(transferBeatmap(count: 0), resources, clock);
        start(preview);
        clock.Seek(400);
        preview.Update();
        preview.ReplaceSchedule(transferBeatmap());
        preview.Update();
        preview.ReplaceSchedule(transferBeatmap(count: 0));
        preview.Update();
        resources.DurationReady = true;
        preview.Update();
        Assert.That(resources.Tracks, Is.Empty);
    }

    [TestCase(O2JamPreviewPlaybackMode.Gameplay)]
    [TestCase(O2JamPreviewPlaybackMode.GameplayAutomatic)]
    public void GameplayDoesNotReconstructPastPlayableSamples(O2JamPreviewPlaybackMode mode)
    {
        var resources = new RestorablePlaybackResource();
        using var preview = new O2JamPreviewTrack(transferBeatmap(automatic: false), resources, new FakeTrack(10_000)) { PlaybackMode = mode };
        preview.Seek(400);
        start(preview);
        preview.Update();
        Assert.That(resources.Tracks, Is.Empty);
    }

    [Test]
    public void PreviewSeekRestoresPastSampleButDoesNotDuplicateExactCursorEvent()
    {
        var resources = new RestorablePlaybackResource();
        using var preview = new O2JamPreviewTrack(transferBeatmap(time: 100), resources, new FakeTrack(10_000));
        preview.Seek(400);
        preview.Update();
        Assert.That(resources.Tracks, Has.Count.EqualTo(1));
        Assert.That(resources.Tracks[0].LastSeek, Is.EqualTo(300));
        Assert.That(resources.Tracks[0].IsRunning, Is.False);
        preview.Seek(100);
        preview.Update();
        Assert.That(resources.Tracks, Has.Count.EqualTo(1));
        start(preview);
        Assert.That(resources.SampleRequests, Is.EqualTo(1));
    }

    [Test]
    public void RestoredDuplicateEventsRemainIndependentVoices()
    {
        var resources = new RestorablePlaybackResource();
        var clock = new FakeTrack(10_000);
        using var preview = new O2JamPreviewTrack(transferBeatmap(count: 0), resources, clock);
        start(preview);
        clock.Seek(400);
        preview.Update();
        preview.ReplaceSchedule(transferBeatmap(count: 2));
        preview.Update();
        Assert.That(resources.Tracks, Has.Count.EqualTo(2));
        preview.ReplaceSchedule(transferBeatmap(count: 1));
        preview.Update();
        foreach (var track in resources.Tracks)
            track.Update();
        Assert.That(resources.Tracks.FindAll(track => !track.IsDisposed).Count, Is.EqualTo(1));
    }

    [Test]
    public void NearbyRetimedVoiceStopsOnlyAfterReplacementIsReady()
    {
        var resources = new RestorablePlaybackResource { VoiceSample = new CapturingSample(), BackgroundReady = false };
        var clock = new FakeTrack(10_000);
        using var preview = new O2JamPreviewTrack(transferBeatmap(), resources, clock);
        start(preview);
        clock.Seek(400);
        preview.Update();
        var oldChannel = resources.VoiceSample.Channel!;
        preview.ReplaceSchedule(transferBeatmap(time: 15));
        preview.Update();
        Assert.That(oldChannel.StopCount, Is.Zero);
        Assert.That(resources.Tracks, Is.Empty);
        Assert.That(clock.IsRunning, Is.True);
        resources.BackgroundReady = true;
        preview.Update();
        Assert.That(resources.Tracks, Has.Count.EqualTo(1));
        Assert.That(resources.Tracks[0].LastSeek, Is.EqualTo(385));
        Assert.That(oldChannel.StopCount, Is.EqualTo(1));
    }

    [Test]
    public void ReplayRestoresSuppliedHitsWithGameplayOffsetAndPause()
    {
        var resources = new RestorablePlaybackResource();
        using var track = new O2JamPreviewTrack(transferBeatmap(automatic: false), resources, new FakeTrack(10_000)) { PlaybackMode = O2JamPreviewPlaybackMode.Gameplay };
        track.Seek(400);
        track.Update();
        Assert.That(resources.Tracks, Is.Empty);
        track.RestoreReplayKeySounds([new O2JamPreviewEvent(100, 9, 80, 0.25f, true, false)], 350);
        track.Update();
        Assert.That(resources.Tracks, Has.Count.EqualTo(1));
        Assert.That(resources.Tracks[0].LastSeek, Is.EqualTo(250));
        Assert.That(resources.Tracks[0].Volume.Value, Is.EqualTo(0.8));
        Assert.That(resources.Tracks[0].Balance.Value, Is.EqualTo(0.25));
        Assert.That(resources.Tracks[0].IsRunning, Is.False);
        start(track);
        Assert.That(resources.Tracks[0].IsRunning, Is.True);
        stop(track);
        Assert.That(resources.Tracks[0].IsRunning, Is.False);
    }

    [Test]
    public void ReplaySeekCancelsPendingAndActiveRestoration()
    {
        var resources = new RestorablePlaybackResource { DurationReady = false };
        using var track = new O2JamPreviewTrack(transferBeatmap(count: 0), resources, new FakeTrack(10_000)) { PlaybackMode = O2JamPreviewPlaybackMode.Gameplay };
        track.Seek(400);
        track.RestoreReplayKeySounds([new O2JamPreviewEvent(100, 9, 100, 0, true, false)], 400);
        track.Update();
        track.Seek(50);
        track.Update();
        resources.DurationReady = true;
        track.Update();
        Assert.That(resources.Tracks, Is.Empty);
        track.Seek(400);
        track.RestoreReplayKeySounds([new O2JamPreviewEvent(100, 9, 100, 0, true, false)], 400);
        track.Update();
        Assert.That(resources.Tracks, Has.Count.EqualTo(1));
        track.Seek(50);
        track.Update();
        resources.Tracks[0].Update();
        Assert.That(resources.Tracks[0].IsDisposed, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NewNativeHitReplacesRestoredReplayVoice(bool pending)
    {
        var resources = new RestorablePlaybackResource { DurationReady = !pending };
        using var track = new O2JamPreviewTrack(transferBeatmap(count: 0), resources, new FakeTrack(10_000)) { PlaybackMode = O2JamPreviewPlaybackMode.Gameplay };
        track.Seek(400);
        track.RestoreReplayKeySounds([new O2JamPreviewEvent(100, 9, 100, 0, true, false)], 400);
        track.Update();
        track.ReplaceRestoredReplaySample(9);
        resources.DurationReady = true;
        track.Update();
        if (pending)
            Assert.That(resources.Tracks, Is.Empty);
        else
        {
            resources.Tracks[0].Update();
            Assert.That(resources.Tracks[0].IsDisposed, Is.True);
        }
    }
    [Test]
    public void ReplayCatchUpKeepsNewAutomaticVoicesWithoutDuplicates()
    {
        var resources = new RestorablePlaybackResource { VoiceSample = new CapturingSample() };
        var clock = new FakeTrack(10_000);
        using var track = new O2JamPreviewTrack(transferBeatmap(time: 400), resources, clock) { PlaybackMode = O2JamPreviewPlaybackMode.Gameplay };
        track.Seek(400);
        start(track);
        clock.Seek(450);
        track.Update();
        Assert.That(resources.VoiceSample.Channels, Has.Count.EqualTo(1));
        track.RestoreReplayKeySounds([], 450);
        track.Update();
        Assert.That(resources.Tracks, Is.Empty);
        Assert.That(resources.VoiceSample.Channels, Has.Count.EqualTo(1));
    }
    private sealed class RestorablePlaybackResource : FakePlaybackResource, IO2JamPlaybackResource
    {
        public bool DurationReady { get; set; } = true;
        public double SampleLength { get; set; } = 5000;
        public CapturingSample? VoiceSample { get; init; }
        public bool BackgroundReady { get; set; } = true;
        public override ISample? GetSample(ISampleInfo info) => VoiceSample ?? base.GetSample(info);
        public override bool IsBackgroundTrackReady(int id) => BackgroundReady;
        public bool TryGetSampleLength(int sampleId, out double length)
        {
            length = SampleLength;
            return DurationReady;
        }
    }

    private static O2JamBeatmap transferBeatmap(bool automatic = true, int count = 1, int sampleId = 7,
                                               double time = 0, int volume = 100, float pan = 0)
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        for (var index = 0; index < count; index++)
        {
            if (automatic)
                beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(time, sampleId, volume, pan, O2JamAudioEventKind.KeySound));
            else
                beatmap.HitObjects.Add(new O2JamNote { StartTime = time, Samples = [new O2JamHitSampleInfo(sampleId, volume, pan)] });
        }
        return beatmap;
    }

    private class FakePlaybackResource : IO2JamPlaybackResource
    {
        public virtual bool IsReadyForScheduling => true;

        public List<FakeTrack> Tracks { get; } = [];

        public int SampleRequests { get; private set; }

        public List<int> SampleReadinessRequests { get; } = [];

        public List<int> PlaybackRequests { get; } = [];

        public virtual bool ContainsSample(int sampleId) => true;

        public virtual bool TryGetAutomaticSampleStreaming(int sampleId, out bool streamed)
        {
            streamed = false;
            return true;
        }

        public virtual bool IsBackgroundTrackReady(int sampleId) => true;

        public virtual bool IsSampleReady(int sampleId)
        {
            SampleReadinessRequests.Add(sampleId);
            return true;
        }

        public virtual ISample? GetSample(ISampleInfo sampleInfo)
        {
            SampleRequests++;
            PlaybackRequests.Add(((O2JamHitSampleInfo)sampleInfo).SampleId);
            return null;
        }

        public Track GetBackgroundTrack(int sampleId)
        {
            PlaybackRequests.Add(sampleId);
            var track = new FakeTrack(5000);
            Tracks.Add(track);
            return track;
        }
    }

    private sealed class SamplePlaybackResource : FakePlaybackResource
    {
        public CapturingSample Sample { get; } = new();

        public override ISample GetSample(ISampleInfo sampleInfo) => Sample;
    }

    private sealed class CapturingSample : Sample
    {
        public FakeSampleChannel? Channel { get; private set; }

        public List<FakeSampleChannel> Channels { get; } = [];

        public override double Length => 5000;

        public CapturingSample()
            : base("test")
        {
        }

        protected override SampleChannel CreateChannel()
        {
            var channel = new FakeSampleChannel();
            Channels.Add(channel);
            return Channel = channel;
        }
    }

    private sealed class FakeSampleChannel() : SampleChannel("test")
    {
        private bool playing = true;

        public int StopCount { get; private set; }

        public override bool Playing => playing;

        public override void Stop()
        {
            StopCount++;
            playing = false;
        }
    }

    private sealed class LeasedPlaybackResource : FakePlaybackResource, IO2JamPlaybackLeaseSource
    {
        public bool LeaseReleased { get; private set; }

        public int TrackStopCountWhenReleased { get; private set; }

        public IDisposable AcquirePlaybackLease() => new Lease(this);

        private sealed class Lease(LeasedPlaybackResource owner) : IDisposable
        {
            public void Dispose()
            {
                owner.TrackStopCountWhenReleased = owner.Tracks[0].StopCount;
                owner.LeaseReleased = true;
            }
        }
    }

    private sealed class StreamingPlaybackResource : FakePlaybackResource
    {
        public override bool TryGetAutomaticSampleStreaming(int sampleId, out bool streamed)
        {
            streamed = true;
            return true;
        }
    }

    private sealed class MissingZeroPlaybackResource : FakePlaybackResource
    {
        public CapturingSample Sample { get; } = new();

        public override bool ContainsSample(int sampleId) => sampleId != 0;

        public override ISample? GetSample(ISampleInfo sampleInfo)
        {
            var sampleId = ((O2JamHitSampleInfo)sampleInfo).SampleId;
            PlaybackRequests.Add(sampleId);
            return sampleId == 0 ? null : Sample;
        }
    }

    private sealed class DeferredBackgroundOnlyResource : FakePlaybackResource
    {
        public bool BackgroundReady { get; set; }

        public override bool IsBackgroundTrackReady(int sampleId) => BackgroundReady;
    }

    private sealed class DeferredPlaybackResource : FakePlaybackResource
    {
        public bool IsReady { get; set; }

        public override bool IsBackgroundTrackReady(int sampleId) => IsReady;
    }

    private sealed class DeferredArchiveResource : FakePlaybackResource
    {
        public bool ArchiveReady { get; set; }
        public override bool IsReadyForScheduling => ArchiveReady;
        public override bool ContainsSample(int sampleId) => ArchiveReady && sampleId == 1000;
    }

    private sealed class ShutdownPlaybackResource : IO2JamPlaybackResource
    {
        public ShutdownTrack Track { get; } = new();

        public bool ContainsSample(int sampleId) => true;

        public ISample? GetSample(ISampleInfo sampleInfo) => null;

        public Track GetBackgroundTrack(int sampleId) => Track;
    }

    private sealed class ShutdownTrack : FakeTrack
    {
        public bool RejectStop { get; set; }

        public ShutdownTrack()
            : base(5000)
        {
        }

        public override void Stop()
        {
            if (RejectStop)
                throw new InvalidOperationException("The mixer has already shut down.");

            base.Stop();
        }
    }

    private class FakeTrack : Track
    {
        private double currentTime;
        private bool isRunning;

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public double LastSeek { get; private set; }

        public override double CurrentTime => currentTime;

        public override bool IsRunning => isRunning;

        public override bool IsDummyDevice => false;

        public FakeTrack(double length)
            : base("fake")
        {
            Length = length;
        }

        public override bool Seek(double seek)
        {
            LastSeek = seek;
            currentTime = System.Math.Clamp(seek, 0, Length);
            return currentTime == seek;
        }

        public override Task<bool> SeekAsync(double seek) => Task.FromResult(Seek(seek));

        public override void Start()
        {
            isRunning = true;
            StartCount++;
        }

        public override Task StartAsync()
        {
            Start();
            return Task.CompletedTask;
        }

        public override void Stop()
        {
            isRunning = false;
            StopCount++;
        }

        public override Task StopAsync()
        {
            Stop();
            return Task.CompletedTask;
        }
    }
}
