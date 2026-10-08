using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Audio.Track;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.O2Lazer.Audio;

public sealed partial class O2JamPreviewTrack
{
    private readonly List<(Track Track, O2JamPreviewEvent Event)> restoredKeyTracks = [];
    private readonly Queue<(int SampleId, O2JamPreviewEvent[] Events)> restoreGroups = new();
    private readonly List<(int SampleId, O2JamPreviewEvent[] Events)> durationProbes = [];
    private readonly Queue<(O2JamPreviewEvent Event, double Length)> readyKeyRestores = new();
    private bool restoringReplay;
    private readonly HashSet<int> replayReplacedSamples = [];

    internal void RestoreReplayKeySounds(O2JamPreviewEvent[] hits, double gameplayTime)
    {
        // Judgements use the offset-aware gameplay clock; streams use the underlying song clock.
        var offset = CurrentTime - gameplayTime;
        EnqueueAction(() =>
        {
            if (playbackMode != O2JamPreviewPlaybackMode.Gameplay)
                return;
            clearPendingKeyRestoration();
            restoringReplay = true;
            var active = activeKeyChannels.Select(voice => voice.Event)
                                          .Concat(restoredKeyTracks.Select(voice => voice.Event))
                                          .GroupBy(evt => evt).ToDictionary(group => group.Key, group => group.Count());
            var candidates = new List<O2JamPreviewEvent>();
            foreach (var evt in automaticKeySoundEvents)
            {
                if (evt.Time >= CurrentTime)
                    continue;
                // The song clock keeps running during replay catch-up. Automatic voices
                // started after the seek already have the correct offset and must not duplicate.
                if (active.TryGetValue(evt, out var count) && count > 0)
                {
                    active[evt] = count - 1;
                    continue;
                }
                candidates.Add(evt);
            }
            candidates.AddRange(hits.Select(evt => evt with { Time = evt.Time + offset }));
            enqueueRestoreGroups(candidates);
        });
    }

    internal void ReplaceRestoredReplaySample(int sampleId) => EnqueueAction(() =>
    {
        if (playbackMode != O2JamPreviewPlaybackMode.Gameplay)
            return;
        for (var index = restoredKeyTracks.Count - 1; index >= 0; index--)
        {
            if (restoredKeyTracks[index].Event.SampleId != sampleId || restoredKeyTracks[index].Event.IsAutomatic)
                continue;
            restoredKeyTracks[index].Track.Dispose();
            restoredKeyTracks.RemoveAt(index);
        }
        // A new native hit supersedes a pending restoration as well as a live restored voice.
        replayReplacedSamples.Add(sampleId);
    });

    private void queueKeyRestoration(double time, Dictionary<O2JamPreviewEvent, int>? remaining = null)
    {
        clearPendingKeyRestoration();
        if (playbackMode != O2JamPreviewPlaybackMode.Preview)
            return;
        var candidates = new List<O2JamPreviewEvent>();
        foreach (var evt in automaticKeySoundEvents.Concat(playableKeySoundEvents))
        {
            // Events exactly at the cursor belong to normal scheduling after seek/rebuild.
            if (evt.Time >= time || evt.Volume <= 0)
                continue;
            if (remaining != null)
            {
                if (!remaining.TryGetValue(evt, out var count) || count <= 0)
                    continue;
                remaining[evt] = count - 1;
            }
            candidates.Add(evt);
        }
        // Recent events are most likely still audible; query their metadata before distant history.
        enqueueRestoreGroups(candidates);
    }

    private void enqueueRestoreGroups(IEnumerable<O2JamPreviewEvent> candidates)
    {
        foreach (var group in candidates.Where(evt => evt.Volume > 0).OrderByDescending(evt => evt.Time).GroupBy(evt => evt.SampleId))
            restoreGroups.Enqueue((group.Key, group.ToArray()));
    }

    private void updateKeyRestoration(double time)
    {
        releaseOutgoingKeyVoices();
        if (playbackMode != O2JamPreviewPlaybackMode.Preview && !restoringReplay)
            return;
        // Bound duration jobs and stream creations independently. Historical samples must not
        // enqueue an entire chart of decoders or stall the music clock while they become ready.
        while (durationProbes.Count < 4 && restoreGroups.TryDequeue(out var group))
            durationProbes.Add(group);
        for (var index = durationProbes.Count - 1; index >= 0; index--)
        {
            var probe = durationProbes[index];
            if (!resources.TryGetSampleLength(probe.SampleId, out var length))
                continue;
            durationProbes.RemoveAt(index);
            if (!double.IsFinite(length) || length <= 0)
                continue;
            foreach (var evt in probe.Events)
            {
                if (time - evt.Time < length)
                    readyKeyRestores.Enqueue((evt, length));
            }
        }
        var attempts = Math.Min(4, readyKeyRestores.Count);
        for (var index = 0; index < attempts; index++)
        {
            var pending = readyKeyRestores.Dequeue();
            if (restoringReplay && !pending.Event.IsAutomatic && replayReplacedSamples.Contains(pending.Event.SampleId))
                continue;
            var offset = time - pending.Event.Time;
            if (offset < 0 || offset >= pending.Length)
                continue;
            if (!resources.IsBackgroundTrackReady(pending.Event.SampleId))
            {
                readyKeyRestores.Enqueue(pending);
                continue;
            }
            var track = resources.GetBackgroundTrack(pending.Event.SampleId);
            if (track == null)
                continue;
            track.Volume.Value = pending.Event.Volume / 100d;
            track.Balance.Value = pending.Event.Pan;
            track.BindAdjustments(this);
            if (!track.Seek(offset))
            {
                track.Dispose();
                continue;
            }
            if (clock.IsRunning)
                track.Start();
            restoredKeyTracks.Add((track, pending.Event));
            releaseOutgoingKeyVoices(pending.Event);
            Logger.Log($"O2Lazer {(restoringReplay ? "replay" : "preview")} sample restored: sample={pending.Event.SampleId}; event_ms={pending.Event.Time:F3}; offset_ms={offset:F3}; length_ms={pending.Length:F3}.", level: LogLevel.Verbose);
        }
    }

    private int retainRestoredKeyTracks(Dictionary<O2JamPreviewEvent, int> remaining)
    {
        var retained = 0;
        for (var index = restoredKeyTracks.Count - 1; index >= 0; index--)
        {
            var active = restoredKeyTracks[index];
            if (!active.Track.IsDisposed && !active.Track.HasCompleted && remaining.TryGetValue(active.Event, out var count) && count > 0)
            {
                remaining[active.Event] = count - 1;
                retained++;
                continue;
            }
            unmatchedKeyVoices.Add((null, active.Track, active.Event));
            restoredKeyTracks.RemoveAt(index);
        }
        return retained;
    }

    private void clearPendingKeyRestoration()
    {
        restoreGroups.Clear();
        durationProbes.Clear();
        readyKeyRestores.Clear();
        replayReplacedSamples.Clear();
        restoringReplay = false;
    }

    private void synchroniseRestoredKeyTracks(double time)
    {
        for (var index = restoredKeyTracks.Count - 1; index >= 0; index--)
        {
            var active = restoredKeyTracks[index];
            var offset = time - active.Event.Time;
            // Resume uses the same chart position as BGM, rather than accumulating mixer pause latency.
            if (!active.Track.IsDisposed && offset >= 0 && offset < active.Track.Length && active.Track.Seek(offset))
                continue;
            active.Track.Dispose();
            restoredKeyTracks.RemoveAt(index);
        }
    }

    private void stopRestoredKeyTracks(bool playableOnly = false)
    {
        for (var index = restoredKeyTracks.Count - 1; index >= 0; index--)
        {
            var active = restoredKeyTracks[index];
            if (playableOnly && active.Event.IsAutomatic)
                continue;
            active.Track.Dispose();
            restoredKeyTracks.RemoveAt(index);
        }
    }
}
