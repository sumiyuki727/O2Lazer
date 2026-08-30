using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Formats.Ojm;
using osu.Game.Rulesets.O2Lazer.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Objects;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[Category("LocalDiagnostics")]
public class OjnCorpusCompatibilityTest
{
    [Test]
    [Explicit("Inspects preview lead-in in three locally available charts; does not benchmark playback.")]
    public void InspectConfiguredPreviewLeadIn()
    {
        var corpusPath = Environment.GetEnvironmentVariable("O2JAM_CORPUS_PATH");
        if (string.IsNullOrWhiteSpace(corpusPath) || !Directory.Exists(corpusPath))
            Assert.Ignore("Set O2JAM_CORPUS_PATH to inspect preview lead-in.");

        foreach (var file in new[] { "o2ma3267.ojn", "o2ma3001.ojn", "o2ma3060.ojn" })
        {
            var path = Path.Combine(corpusPath!, "SongC", file);
            using var stream = File.OpenRead(path);
            var document = new OjnReader().Read(stream);
            var archivePath = Path.Combine(Path.GetDirectoryName(path)!, document.Metadata.OjmFileName);
            var archive = OjmArchiveCache.Shared.GetAll(path, archivePath);
            foreach (var chart in document.Charts)
            {
                var beatmap = new OjnBeatmapFactory().Create(document, chart.Difficulty);
                var schedule = O2JamPreviewSchedule.Create(beatmap, true);
                var first = schedule.PreviewEvents.FirstOrDefault(evt => evt.Volume > 0 && archive.Samples.ContainsKey(evt.SampleId));
                Assert.That(first.Volume, Is.GreaterThan(0));
                TestContext.Progress.WriteLine($"{file} {chart.Difficulty}: first existing audible event at {first.Time:N1} ms, sample {first.SampleId}.");
            }
        }
    }
    [Test]
    [Explicit("Requires an external O2Jam catalogue and builds every playable OJN difficulty.")]
    public void ReadsConfiguredCorpus()
    {
        var corpusPath = Environment.GetEnvironmentVariable("O2JAM_CORPUS_PATH");
        if (string.IsNullOrWhiteSpace(corpusPath) || !Directory.Exists(corpusPath))
            Assert.Ignore("Set O2JAM_CORPUS_PATH to run the external catalogue compatibility test.");

        var files = Directory.EnumerateFiles(corpusPath!, "*", SearchOption.AllDirectories)
                             .Where(path => string.Equals(Path.GetExtension(path), ".ojn", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                             .ToArray();
        var failures = new List<string>();
        var chartCount = 0;
        var playableChartCount = 0;
        long hitObjectCount = 0;
        long automaticAudioEventCount = 0;

        foreach (var path in files)
        {
            try
            {
                using var stream = File.OpenRead(path);
                var document = new OjnReader().Read(stream);
                chartCount += document.Charts.Count;

                foreach (var chart in document.Charts.Where(chart => chart.Notes.Any(note => note.IsPlayable)))
                {
                    playableChartCount++;
                    var beatmap = new OjnBeatmapFactory().Create(document, chart.Difficulty);
                    var preview = O2JamPreviewSchedule.Create(beatmap, true);
                    hitObjectCount += beatmap.HitObjects.Count;
                    automaticAudioEventCount += beatmap.AutomaticAudioEvents.Count;

                    if (beatmap.HitObjects.Any(hitObject => !double.IsFinite(hitObject.StartTime)
                                                           || hitObject.Column is < 0 or >= O2JamBeatmap.ColumnCount
                                                           || hitObject is O2JamHoldNote { Duration: < 0 })
                        || preview.PreviewEvents.Any(evt => !double.IsFinite(evt.Time))
                        || beatmap.MeasureLineTimes.Any(time => !double.IsFinite(time))
                        || !beatmap.MeasureLineTimes.SequenceEqual(beatmap.MeasureLineTimes.Order())
                        || !preview.PreviewEvents.Select(evt => evt.Time).SequenceEqual(
                            preview.PreviewEvents.Select(evt => evt.Time).Order()))
                    {
                        throw new InvalidDataException("The converted beatmap contains an invalid or unordered gameplay event.");
                    }
                }
            }
            catch (Exception exception)
            {
                failures.Add($"{path}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        TestContext.Progress.WriteLine(
            $"Read {files.Length:N0} OJN files and built {playableChartCount:N0}/{chartCount:N0} playable charts "
            + $"containing {hitObjectCount:N0} hit objects and {automaticAudioEventCount:N0} automatic audio events.");
        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures.Take(50)));
    }

    [Test]
    [Explicit("Requires an external O2Jam catalogue and fully decodes representative OJM archives.")]
    public void ReadsRepresentativeSampleArchives()
    {
        var corpusPath = Environment.GetEnvironmentVariable("O2JAM_CORPUS_PATH");
        if (string.IsNullOrWhiteSpace(corpusPath) || !Directory.Exists(corpusPath))
            Assert.Ignore("Set O2JAM_CORPUS_PATH to run the external catalogue compatibility test.");

        var groups = Directory.EnumerateFiles(corpusPath!, "*", SearchOption.AllDirectories)
                              .Where(path => string.Equals(Path.GetExtension(path), ".ojm", StringComparison.OrdinalIgnoreCase))
                              .Select(path => new FileInfo(path))
                              .GroupBy(readArchiveKind)
                              .ToArray();
        var failures = new List<string>();
        var decoded = 0;

        foreach (var group in groups)
        {
            var ordered = group.OrderBy(file => file.Length).ToArray();
            FileInfo[] representatives = [ordered[0], ordered[ordered.Length / 2], ordered[^1]];

            foreach (var file in representatives.DistinctBy(file => file.FullName))
            {
                try
                {
                    using var stream = file.OpenRead();
                    var archive = new OjmReader().Read(stream);
                    decoded += archive.Samples.Count;
                }
                catch (Exception exception)
                {
                    failures.Add($"{file.FullName}: {exception.GetType().Name}: {exception.Message}");
                }
            }
        }

        TestContext.Progress.WriteLine($"Decoded {groups.Length * 3:N0} representative OJM archives containing {decoded:N0} supported samples.");
        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
    }

    [Test]
    [Explicit("Requires an external O2Jam catalogue and fully decodes every OJM archive.")]
    public void ReadsAllSampleArchives()
    {
        var corpusPath = Environment.GetEnvironmentVariable("O2JAM_CORPUS_PATH");
        if (string.IsNullOrWhiteSpace(corpusPath) || !Directory.Exists(corpusPath))
            Assert.Ignore("Set O2JAM_CORPUS_PATH to run the external catalogue compatibility test.");

        var files = Directory.EnumerateFiles(corpusPath!, "*", SearchOption.AllDirectories)
                             .Where(path => string.Equals(Path.GetExtension(path), ".ojm", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                             .ToArray();
        var failures = new List<string>();
        long sampleCount = 0;
        long decodedBytes = 0;

        foreach (var path in files)
        {
            try
            {
                using var stream = File.OpenRead(path);
                var archive = new OjmReader().Read(stream);
                sampleCount += archive.Samples.Count;
                decodedBytes += archive.Samples.Values.Sum(sample => (long)sample.Data.Length);

                if (archive.Samples.Any(pair => pair.Key != pair.Value.Id || pair.Value.Data.Length == 0))
                    throw new InvalidDataException("The decoded archive contains an invalid sample entry.");
            }
            catch (Exception exception)
            {
                failures.Add($"{path}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        TestContext.Progress.WriteLine(
            $"Fully decoded {files.Length:N0} OJM archives containing {sampleCount:N0} samples and {decodedBytes:N0} decoded bytes.");
        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures.Take(50)));
    }

    [Test]
    [Explicit("Requires an external O2Jam catalogue and scans all OJN/OJM sample references.")]
    public void AuditsReferencedSamples()
    {
        var corpusPath = Environment.GetEnvironmentVariable("O2JAM_CORPUS_PATH");
        if (string.IsNullOrWhiteSpace(corpusPath) || !Directory.Exists(corpusPath))
            Assert.Ignore("Set O2JAM_CORPUS_PATH to run the external catalogue compatibility test.");

        var archiveIndexes = new Dictionary<string, (OjmArchiveIndex Index, string Kind)>(StringComparer.OrdinalIgnoreCase);
        var audits = new Dictionary<string, SampleReferenceAudit>(StringComparer.Ordinal);
        var missingArchives = new List<string>();
        var missingSamples = new List<string>();

        foreach (var ojnPath in Directory.EnumerateFiles(corpusPath!, "*", SearchOption.AllDirectories)
                                         .Where(path => string.Equals(Path.GetExtension(path), ".ojn", StringComparison.OrdinalIgnoreCase)))
        {
            OjnDocument document;
            using (var stream = File.OpenRead(ojnPath))
                document = new OjnReader().Read(stream);

            var resourceName = string.IsNullOrWhiteSpace(document.Metadata.OjmFileName)
                ? Path.ChangeExtension(Path.GetFileName(ojnPath), ".ojm")
                : document.Metadata.OjmFileName;
            if (!O2JamExternalChart.TryResolveResource(ojnPath, resourceName, out var ojmPath))
            {
                var fallback = Path.ChangeExtension(Path.GetFileName(ojnPath), ".ojm");
                if (!O2JamExternalChart.TryResolveResource(ojnPath, fallback, out ojmPath))
                {
                    missingArchives.Add($"{ojnPath}: {resourceName}");
                    continue;
                }
            }

            if (!archiveIndexes.TryGetValue(ojmPath, out var archive))
            {
                using var archiveStream = File.OpenRead(ojmPath);
                var kind = readArchiveKind(new FileInfo(ojmPath));
                archiveIndexes[ojmPath] = archive = (new OjmReader().ReadIndex(archiveStream), kind);
            }

            var references = document.Charts.SelectMany(chart => chart.Notes)
                                     .SelectMany(note => note.TailSampleId is { } tail
                                         ? new[] { note.SampleId, tail }
                                         : [note.SampleId])
                                     .ToArray();
            if (!audits.TryGetValue(archive.Kind, out var audit))
                audits[archive.Kind] = audit = new SampleReferenceAudit();
            audit.ObserveFile(references, archive.Index.SampleIds);

            foreach (var sampleId in references.Distinct())
            {
                if (!archive.Index.SampleIds.Contains(sampleId)
                    && !archive.Index.SampleIds.Contains(shiftWithinBank(sampleId, 1))
                    && !archive.Index.SampleIds.Contains(shiftWithinBank(sampleId, -1))
                    && missingSamples.Count < 100)
                    missingSamples.Add($"{ojnPath}: sample {sampleId} missing from {ojmPath}");
            }
        }

        var total = new SampleReferenceAudit();
        foreach (var audit in audits.Values)
            total.Add(audit);

        TestContext.Progress.WriteLine(
            $"Audited {archiveIndexes.Count:N0} OJM archives; {missingArchives.Count:N0} archives were missing.\n"
            + string.Join(Environment.NewLine, audits.OrderBy(pair => pair.Key).Select(pair => pair.Value.Describe(pair.Key)))
            + $"\n{total.Describe("TOTAL")}\nUnresolved under all three mappings:\n{string.Join(Environment.NewLine, missingSamples.Take(20))}");

        Assert.Multiple(() =>
        {
            Assert.That(missingArchives, Is.Empty, string.Join(Environment.NewLine, missingArchives.Take(50)));
            Assert.That(total.ExactEvents, Is.GreaterThan(0));
            // Occurrence weighting is dominated by deliberately silent normalised id 0 events.
            // Unique authored references are the useful corpus-wide mapping sanity check.
            Assert.That(total.ExactUnique, Is.GreaterThan(total.PlusOneUnique));
            Assert.That(total.ExactUnique, Is.GreaterThan(total.MinusOneUnique));
        });
    }

    private static int shiftWithinBank(int sampleId, int delta)
    {
        var bank = sampleId >= 1000 ? 1000 : 0;
        var shifted = sampleId - bank + delta;
        return shifted < 0 ? -1 : bank + shifted;
    }

    private sealed class SampleReferenceAudit
    {
        public long EventCount { get; private set; }
        public long ExactEvents { get; private set; }
        public long PlusOneEvents { get; private set; }
        public long MinusOneEvents { get; private set; }
        public long UniqueCount { get; private set; }
        public long ExactUnique { get; private set; }
        public long PlusOneUnique { get; private set; }
        public long MinusOneUnique { get; private set; }
        public long ZeroEvents { get; private set; }
        public int FileCount { get; private set; }
        public int ExactBestFiles { get; private set; }
        public int PlusOneBestFiles { get; private set; }
        public int MinusOneBestFiles { get; private set; }

        public void ObserveFile(IReadOnlyCollection<int> references, IReadOnlySet<int> available)
        {
            FileCount++;
            EventCount += references.Count;
            ZeroEvents += references.Count(reference => reference == 0);
            ExactEvents += references.Count(available.Contains);
            PlusOneEvents += references.Count(reference => available.Contains(shiftWithinBank(reference, 1)));
            MinusOneEvents += references.Count(reference => available.Contains(shiftWithinBank(reference, -1)));

            var unique = references.Distinct().ToArray();
            UniqueCount += unique.Length;
            var exact = unique.Count(available.Contains);
            var plusOne = unique.Count(reference => available.Contains(shiftWithinBank(reference, 1)));
            var minusOne = unique.Count(reference => available.Contains(shiftWithinBank(reference, -1)));
            ExactUnique += exact;
            PlusOneUnique += plusOne;
            MinusOneUnique += minusOne;

            var best = Math.Max(exact, Math.Max(plusOne, minusOne));
            if (exact == best)
                ExactBestFiles++;
            if (plusOne == best)
                PlusOneBestFiles++;
            if (minusOne == best)
                MinusOneBestFiles++;
        }

        public void Add(SampleReferenceAudit other)
        {
            EventCount += other.EventCount;
            ExactEvents += other.ExactEvents;
            PlusOneEvents += other.PlusOneEvents;
            MinusOneEvents += other.MinusOneEvents;
            UniqueCount += other.UniqueCount;
            ExactUnique += other.ExactUnique;
            PlusOneUnique += other.PlusOneUnique;
            MinusOneUnique += other.MinusOneUnique;
            ZeroEvents += other.ZeroEvents;
            FileCount += other.FileCount;
            ExactBestFiles += other.ExactBestFiles;
            PlusOneBestFiles += other.PlusOneBestFiles;
            MinusOneBestFiles += other.MinusOneBestFiles;
        }

        public string Describe(string kind) =>
            $"{kind}: files={FileCount:N0}; events exact/+1/-1={rate(ExactEvents, EventCount):P3}/{rate(PlusOneEvents, EventCount):P3}/{rate(MinusOneEvents, EventCount):P3} "
            + $"({EventCount:N0}, zero={ZeroEvents:N0}); unique exact/+1/-1={rate(ExactUnique, UniqueCount):P3}/{rate(PlusOneUnique, UniqueCount):P3}/{rate(MinusOneUnique, UniqueCount):P3} "
            + $"({UniqueCount:N0}); best-file ties exact/+1/-1={ExactBestFiles:N0}/{PlusOneBestFiles:N0}/{MinusOneBestFiles:N0}";

        private static double rate(long resolved, long total) => total == 0 ? 0 : resolved / (double)total;
    }

    private static string readArchiveKind(FileInfo file)
    {
        using var stream = file.OpenRead();
        using var reader = new BinaryReader(stream);
        var signature = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(3));
        _ = reader.ReadByte();

        if (signature != "M30")
            return signature;

        _ = reader.ReadUInt32();
        return $"{signature}:{reader.ReadUInt32()}";
    }
}
