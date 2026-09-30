using System;
using System.Collections.Generic;
using NUnit.Framework;
using O2Jam.Formats.Ojm;
using O2Jam.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class FormatOwnershipTest
{
    [Test]
    public void OjnCollectionsAndArtworkCannotPolluteAnotherConsumer()
    {
        ushort[] levels = [3];
        uint[] durations = [10];
        byte[] cover = [1, 2];
        byte[] thumbnail = [3, 4];
        OjnNoteEvent[] notes = [new(1, 2, 7, 100, 0, OjnNoteType.Tap, OjnSampleKind.KeySound)];
        var metadata = new OjnMetadata(1, 2.9f, 120, "title", "artist", "arranger", "song.ojm",
            levels, durations, cover, thumbnail);
        var chart = new OjnChart(OjnDifficulty.EX, 3, [], notes, [], 2);
        var charts = new List<OjnChart> { chart };
        var document = new OjnDocument(metadata, charts);

        levels[0] = 9;
        durations[0] = 99;
        cover[0] = 9;
        thumbnail[0] = 9;
        notes[0] = notes[0] with { SampleId = 99 };
        charts.Clear();
        metadata.Cover[1] = 9;
        metadata.Thumbnail[1] = 9;

        Assert.Multiple(() =>
        {
            Assert.That(document.Metadata.Levels[0], Is.EqualTo(3));
            Assert.That(document.Metadata.Durations[0], Is.EqualTo(10));
            Assert.That(document.Metadata.Cover, Is.EqualTo(new byte[] { 1, 2 }));
            Assert.That(document.Metadata.Thumbnail, Is.EqualTo(new byte[] { 3, 4 }));
            Assert.That(document.Charts, Has.Count.EqualTo(1));
            Assert.That(document.Charts[0].Notes[0].SampleId, Is.EqualTo(7));
        });
        Assert.Throws<NotSupportedException>(() => ((IList<OjnChart>)document.Charts).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<OjnNoteEvent>)chart.Notes).Clear());
    }

    [Test]
    public void OjmIndexAndPayloadCannotPolluteAnotherConsumer()
    {
        byte[] bytes = [1, 2, 3];
        var sample = new OjmSample(7, "sample", ".ogg", bytes);
        var samples = new Dictionary<int, OjmSample> { [7] = sample };
        var archive = new OjmArchive(samples);
        var ids = new HashSet<int> { 7 };
        var index = new OjmArchiveIndex(ids);

        bytes[0] = 9;
        samples.Clear();
        ids.Clear();
        sample.Data[1] = 9;
        using var stream = sample.OpenRead();

        Assert.Multiple(() =>
        {
            Assert.That(archive.Samples.Keys, Is.EquivalentTo(new[] { 7 }));
            Assert.That(archive.Samples[7].Data, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(index.SampleIds, Is.EquivalentTo(new[] { 7 }));
            Assert.That(stream.CanWrite, Is.False);
            Assert.That(stream.ReadByte(), Is.EqualTo(1));
        });
        Assert.Throws<NotSupportedException>(() => ((IDictionary<int, OjmSample>)archive.Samples).Clear());
    }
}
