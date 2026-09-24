using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ManagedBass;
using ManagedBass.Fx;
using NUnit.Framework;
using Realms;
using osu.Framework.Threading;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Formats.Ojm;
using osu.Game.Rulesets.O2Lazer.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Objects;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
[Category("LocalDiagnostics")]
public class O2JamSyncDiagnosticTest
{
    [Test]
    [NUnit.Framework.Explicit("Compares the reported Ogg's raw and native tempo/reverse decode paths in memory, without a sound device.")]
    public void InspectReportedOggDecoderAlignment()
    {
        var root = Environment.GetEnvironmentVariable("O2JAM_CORPUS_PATH");
        if (string.IsNullOrWhiteSpace(root))
            Assert.Ignore("Set O2JAM_CORPUS_PATH to inspect the reported chart.");

        using var audio = File.OpenRead(Path.Combine(root!, "SongC", "o2ma3033.ojm"));
        var archive = new OjmReader().Read(audio);
        var data = archive.Samples[0].Data;
        typeof(AudioThread).GetMethod("PreloadBass", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
        Assert.That(Bass.Init(0), Is.True);
        try
        {
            double[] seeks = [0, 1, 5, 20, 2000];
            foreach (var seek in seeks)
            {
                var raw = Bass.CreateStream(data, 0, data.Length, BassFlags.Decode | BassFlags.Prescan);
                var source = Bass.CreateStream(data, 0, data.Length, BassFlags.Decode | BassFlags.Prescan);
                Assert.That(raw, Is.Not.Zero);
                Assert.That(source, Is.Not.Zero);
                var tempo = BassFx.TempoCreate(source, BassFlags.Decode | BassFlags.FxFreeSource);
                var native = BassFx.ReverseCreate(tempo, 5f, BassFlags.Decode | BassFlags.FxFreeSource);
                try
                {
                    Assert.That(native, Is.Not.Zero);
                    Bass.ChannelSetAttribute(tempo, ChannelAttribute.TempoUseQuickAlgorithm, 1);
                    Bass.ChannelSetAttribute(tempo, ChannelAttribute.TempoOverlapMilliseconds, 4);
                    Bass.ChannelSetAttribute(tempo, ChannelAttribute.TempoSequenceMilliseconds, 30);
                    Bass.ChannelSetAttribute(tempo, (ChannelAttribute)0x10017, 1);
                    Bass.ChannelSetAttribute(native, ChannelAttribute.ReverseDirection, 1);
                    Bass.ChannelGetInfo(raw, out var info);
                    var position = Bass.ChannelSeconds2Bytes(raw, seek / 1000);
                    Assert.That(Bass.ChannelSetPosition(raw, position), Is.True);
                    Assert.That(Bass.ChannelSetPosition(native, position), Is.True);
                    var reference = new float[info.Frequency * info.Channels / 2];
                    var actual = new float[reference.Length];
                    var referenceBytes = Bass.ChannelGetData(raw, reference, reference.Length * sizeof(float) | (int)DataFlags.Float);
                    var actualBytes = Bass.ChannelGetData(native, actual, actual.Length * sizeof(float) | (int)DataFlags.Float);
                    Assert.That(actualBytes, Is.EqualTo(referenceBytes).And.GreaterThan(0));
                    var maxDifference = reference.Zip(actual).Max(pair => Math.Abs(pair.First - pair.Second));
                    var (lead, correlation) = findDecodedLead(reference, actual, info.Channels, info.Frequency);
                    TestContext.Progress.WriteLine($"Ogg seek={seek:F3} ms: frequency={info.Frequency}, channels={info.Channels}, max PCM difference={maxDifference:F9}, raw position={Bass.ChannelBytes2Seconds(raw, Bass.ChannelGetPosition(raw)) * 1000:F6} ms, native position={Bass.ChannelBytes2Seconds(native, Bass.ChannelGetPosition(native)) * 1000:F6} ms");
                    TestContext.Progress.WriteLine($"Native PCM lead relative to raw Ogg: {lead:F6} ms (positive = ahead), correlation={correlation:F9}");
                }
                finally
                {
                    Bass.StreamFree(raw);
                    Bass.StreamFree(native);
                }
            }
        }
        finally
        {
            Bass.Free();
        }
    }

    private static (double Lead, double Correlation) findDecodedLead(float[] reference, float[] actual, int channels, int frequency)
    {
        var limit = frequency / 10;
        var end = Math.Min(reference.Length, actual.Length) / channels - limit;
        var bestOffset = 0;
        var bestCorrelation = double.NegativeInfinity;

        // Waveform correlation identifies a constant decode delay without inferring musical onsets
        // or relying on a sound device's reporting position. Only a half-second diagnostic is decoded.
        double correlate(int offset)
        {
            var sum = 0d;
            var leftEnergy = 0d;
            var rightEnergy = 0d;
            for (var frame = limit; frame < end; frame += 8)
            {
                var left = reference[(frame + offset) * channels];
                var right = actual[frame * channels];
                sum += left * right;
                leftEnergy += left * left;
                rightEnergy += right * right;
            }
            return sum / Math.Sqrt(leftEnergy * rightEnergy);
        }

        for (var offset = -limit; offset <= limit; offset++)
        {
            var correlation = correlate(offset);
            if (correlation > bestCorrelation)
                (bestOffset, bestCorrelation) = (offset, correlation);
        }
        return (bestOffset * 1000d / frequency, bestCorrelation);
    }

    [Test]
    [NUnit.Framework.Explicit("Reads the reported external chart and its single audio archive without playing audio or benchmarking.")]
    public void InspectReportedArtOfWarChart()
    {
        var root = Environment.GetEnvironmentVariable("O2JAM_CORPUS_PATH");
        if (string.IsNullOrWhiteSpace(root))
            Assert.Ignore("Set O2JAM_CORPUS_PATH to inspect the reported chart.");

        var path = Path.Combine(root!, "SongC", "o2ma3033.ojn");
        using var source = File.OpenRead(path);
        var document = new OjnReader().Read(source);
        var archivePath = Path.Combine(Path.GetDirectoryName(path)!, document.Metadata.OjmFileName);
        using var audio = File.OpenRead(archivePath);
        var archive = new OjmReader().Read(audio);
        TestContext.Progress.WriteLine($"{path}: {document.Metadata.Title} / {document.Metadata.Artist}; initial BPM={document.Metadata.InitialBpm}");
        foreach (var sample in archive.Samples.Values)
            TestContext.Progress.WriteLine($"Audio: id={sample.Id}, name={sample.Name}, bytes={sample.ByteLength}, signature={Encoding.ASCII.GetString(sample.Data, 0, Math.Min(4, sample.Data.Length))}");

        foreach (var chart in document.Charts)
        {
            var beatmap = new OjnBeatmapFactory().Create(document, chart.Difficulty);
            var schedule = O2JamPreviewSchedule.Create(beatmap, true);
            TestContext.Progress.WriteLine($"{chart.Difficulty}: level={chart.Level}; notes={beatmap.HitObjects.Count}; first note={beatmap.HitObjects.First().StartTime:F6} ms; last note={beatmap.HitObjects.Last().StartTime:F6} ms");
            TestContext.Progress.WriteLine($"BPM events: {string.Join(", ", chart.BpmEvents.Select(evt => $"{evt.Position:F9}: {evt.Bpm:F9}"))}");
            TestContext.Progress.WriteLine($"Measure fractions: {string.Join(", ", chart.MeasureFractions.Select(evt => $"{evt.Measure}: {evt.Fraction:F9}"))}");
            TestContext.Progress.WriteLine($"First note groups: {string.Join(", ", beatmap.HitObjects.GroupBy(note => note.StartTime).Take(12).Select(group => $"{group.Key:F6} ms ({group.Count()} notes)"))}");
            foreach (var evt in schedule.PreviewEvents.Where(evt => archive.Samples.ContainsKey(evt.SampleId)))
                TestContext.Progress.WriteLine($"Audible event: time={evt.Time:F6} ms, sample={evt.SampleId}, volume={evt.Volume}, automatic={evt.IsAutomatic}, keysound={evt.IsKeySound}");

            // Any conversion discrepancy would affect this comparison before an audio device or
            // platform offset is involved. Header declarations alone cannot validate runtime sync.
            foreach (var note in beatmap.HitObjects)
            {
                var position = note is O2JamHoldNote hold ? hold.HeadChartPosition : ((O2JamNote)note).ChartPosition;
                Assert.That(beatmap.TimingMap.TimeAt(position), Is.EqualTo(note.StartTime).Within(0.000001));
                Assert.That(beatmap.TimingMap.PositionAt(note.StartTime), Is.EqualTo(position).Within(0.000001));
            }
        }

        Assert.That(document.Metadata.Title, Is.EqualTo("[荣誉]战争的艺术"));
        Assert.That(archive.Samples, Has.Count.EqualTo(1));
    }

    [Test]
    [NUnit.Framework.Explicit("Reads only the reported beatmap's stored offsets from a read-only dynamic Realm. No schema migration or writes.")]
    public void InspectReportedBeatmapOffsetsReadOnly()
    {
        var path = Environment.GetEnvironmentVariable("O2JAM_DIAGNOSTIC_REALM");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            Assert.Ignore("Set O2JAM_DIAGNOSTIC_REALM to read the installed beatmap offsets.");

        using var realm = Realm.GetInstance(new RealmConfiguration(path!)
        {
            IsReadOnly = true,
            IsDynamic = true,
        });
        var matches = realm.DynamicApi.All("Beatmap")
                           .Filter("Metadata.Title == $0 AND Ruleset.ShortName == $1", "[荣誉]战争的艺术", "o2lazer");
        var count = 0;
        foreach (dynamic beatmap in matches)
        {
            count++;
            TestContext.Progress.WriteLine($"{beatmap.DifficultyName}: offset={beatmap.UserSettings.Offset}, source={beatmap.Metadata.Source}, deleted={beatmap.BeatmapSet.DeletePending}");
        }
        Assert.That(count, Is.GreaterThan(0));

        var scores = realm.DynamicApi.All("Score")
                          .Filter("BeatmapInfo.Metadata.Title == $0 AND Ruleset.ShortName == $1 AND DeletePending == false",
                              "[荣誉]战争的艺术", "o2lazer");
        TestContext.Progress.WriteLine($"Local scores for reported chart: {scores.Count()}");
        foreach (dynamic score in scores)
            TestContext.Progress.WriteLine($"Score diagnostic: difficulty={score.BeatmapInfo.DifficultyName}, date={score.Date}, replay files={score.Files.Count}, mods={score.Mods}");
    }
}
