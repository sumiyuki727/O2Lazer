using System;
using System.Collections.Generic;
using System.Diagnostics;
using osu.Framework.Audio.Sample;
using osu.Framework.Audio.Track;

namespace osu.Game.Rulesets.O2Lazer.Audio;

public sealed partial class O2JamPreviewTrack
{
    private readonly List<(SampleChannel? Channel, Track? Track, O2JamPreviewEvent Replacement, long Deadline)> outgoingKeyVoices = [];
    private readonly Dictionary<O2JamPreviewEvent, int> handoffReservations = [];
    private readonly List<(SampleChannel? Channel, Track? Track, O2JamPreviewEvent Event)> unmatchedKeyVoices = [];

    private void prepareKeyHandoffs(Dictionary<O2JamPreviewEvent, int> remaining)
    {
        var candidates = new Dictionary<int, List<KeyValuePair<O2JamPreviewEvent, int>>>();
        foreach (var candidate in remaining)
        {
            if (candidate.Value <= 0 || candidate.Key.Time >= CurrentTime)
                continue;
            if (!candidates.TryGetValue(candidate.Key.SampleId, out var events))
                candidates[candidate.Key.SampleId] = events = [];
            events.Add(candidate);
        }
        // Exact matches in both native voice types consume their occurrences before approximate
        // handoffs reserve anything, so a temporary bridge cannot steal an already playing voice.
        foreach (var active in unmatchedKeyVoices)
        {
            if (candidates.TryGetValue(active.Event.SampleId, out var events)
                && tryDeferKeyVoice(active.Channel, active.Track, active.Event, events))
                continue;
            active.Channel?.Stop();
            active.Track?.Dispose();
        }
        unmatchedKeyVoices.Clear();
        handoffReservations.Clear();
    }

    private bool tryDeferKeyVoice(SampleChannel? channel, Track? track, O2JamPreviewEvent source,
                                 IReadOnlyList<KeyValuePair<O2JamPreviewEvent, int>> remaining)
    {
        if (track is { IsDisposed: true } || track is { HasCompleted: true })
            return false;
        O2JamPreviewEvent? replacement = null;
        var distance = double.MaxValue;
        foreach (var (candidate, count) in remaining)
        {
            handoffReservations.TryGetValue(candidate, out var reserved);
            var difference = Math.Abs(candidate.Time - source.Time);
            if (count <= reserved || candidate.Time >= CurrentTime || difference > 100 || difference >= distance
                || candidate.SampleId != source.SampleId || candidate.Volume != source.Volume
                || candidate.Pan != source.Pan)
                continue;
            replacement = candidate;
            distance = difference;
        }
        if (replacement is not { } evt)
            return false;
        var length = track?.Length ?? 0;
        var lengthKnown = track != null || resources.TryGetSampleLength(evt.SampleId, out length);
        if (lengthKnown && (length <= 0 || CurrentTime - evt.Time >= length))
            return false;
        handoffReservations.TryGetValue(evt, out var reservations);
        handoffReservations[evt] = reservations + 1;
        // Preserve a nearby outgoing voice only until its correctly positioned replacement is
        // ready. A bounded wall-clock deadline prevents failed decoding from retaining stale audio.
        outgoingKeyVoices.Add((channel, track, evt, Stopwatch.GetTimestamp()));
        return true;
    }

    private void releaseOutgoingKeyVoices(O2JamPreviewEvent? replacement = null, bool all = false, bool playableOnly = false)
    {
        for (var index = outgoingKeyVoices.Count - 1; index >= 0; index--)
        {
            var active = outgoingKeyVoices[index];
            if (playableOnly && active.Replacement.IsAutomatic)
                continue;
            if (!all && replacement != null && replacement != active.Replacement)
                continue;
            if (!all && replacement == null && Stopwatch.GetElapsedTime(active.Deadline).TotalMilliseconds < 250)
                continue;
            active.Channel?.Stop();
            active.Track?.Dispose();
            outgoingKeyVoices.RemoveAt(index);
            // A replacement occurrence releases one reserved voice, not every simultaneous duplicate.
            if (replacement != null && !all)
                break;
        }
    }
}
