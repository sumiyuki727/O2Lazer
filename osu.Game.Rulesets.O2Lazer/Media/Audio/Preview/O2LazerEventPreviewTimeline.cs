using System;
using System.Collections.Generic;
using System.Threading;

namespace osu.Game.Rulesets.O2Lazer.Media.Audio.Preview;

internal readonly record struct O2LazerPreviewTimelineEntry(double Time, ushort SampleKey, string SamplePath, int Volume, bool ResumeAfterSeek);

internal sealed record O2LazerEventPreviewTimeline(
    IReadOnlyList<O2LazerPreviewTimelineEntry> Entries,
    double Length,
    bool DeriveLengthFromSamples = false,
    bool ExtendLengthFromSamples = false)
{
    internal const double DEFAULT_LENGTH = 30000;

    internal static O2LazerEventPreviewTimeline CreateSingleFile(string samplePath) => new(
        [new O2LazerPreviewTimelineEntry(0, 0, samplePath, 100, true)],
        DEFAULT_LENGTH,
        DeriveLengthFromSamples: true);

    internal static O2LazerEventPreviewTimeline Create(
        Func<CancellationToken, IReadOnlyList<O2LazerPreviewSampleEvent>> sampleEventFactory,
        IReadOnlyDictionary<ushort, string> sampleDefinitions,
        CancellationToken cancellationToken) =>
        Create(() => sampleEventFactory(cancellationToken), sampleDefinitions, cancellationToken);

    internal static O2LazerEventPreviewTimeline Create(
        Func<IReadOnlyList<O2LazerPreviewSampleEvent>> sampleEventFactory,
        IReadOnlyDictionary<ushort, string> sampleDefinitions,
        CancellationToken cancellationToken = default)
    {
        var entries = new List<O2LazerPreviewTimelineEntry>();

        foreach (var (evt, resumeAfterSeek) in sampleEventFactory())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (sampleDefinitions.TryGetValue(evt.SampleKey, out var samplePath))
                entries.Add(new O2LazerPreviewTimelineEntry(evt.Time, evt.SampleKey, samplePath, evt.Volume, resumeAfterSeek));
        }

        cancellationToken.ThrowIfCancellationRequested();
        entries.Sort((a, b) => a.Time.CompareTo(b.Time));

        if (entries.Count > 0 && entries[0].Time > 0)
        {
            var leadIn = entries[0].Time;

            for (var i = 0; i < entries.Count; i++)
                entries[i] = entries[i] with { Time = entries[i].Time - leadIn };
        }

        var length = entries.Count > 0 ? entries[^1].Time + 5000 : DEFAULT_LENGTH;
        return new O2LazerEventPreviewTimeline(entries, length, ExtendLengthFromSamples: true);
    }
}
