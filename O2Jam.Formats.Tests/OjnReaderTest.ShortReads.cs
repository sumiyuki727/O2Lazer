using System;
using System.IO;
using NUnit.Framework;
using O2Jam.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class OjnReaderTest
{
    [Test, Combinatorial]
    public void SelectedChartShortReadsMatchFullDecode([Values(false, true)] bool encrypted,
                                                      [Values(false, true)] bool canSeek,
                                                      [Values(1, 2)] int chunkSize,
                                                      [Values(OjnDifficulty.EX, OjnDifficulty.NX, OjnDifficulty.HX)] OjnDifficulty difficulty)
    {
        var bytes = createBudgetCharts();
        if (encrypted)
            bytes = encrypt(bytes);
        var expected = new OjnReader().Read(bytes);
        var expectedChart = expected.Charts[(int)difficulty];
        using var stream = new BudgetCountingStream(bytes, canSeek, chunkSize);
        if (canSeek)
            stream.Position = 17;
        var reader = new OjnReader(OjnMetadataEncoding.Automatic, null, new OjnReadLimits(bytes.Length, 4));
        var actual = reader.ReadChart(stream, difficulty);
        var actualChart = actual.Charts[0];
        var expectedBytesRead = canSeek ? (encrypted ? bytes.Length + 3 : 375) : bytes.Length;

        Assert.Multiple(() =>
        {
            Assert.That(actual.Metadata.SongId, Is.EqualTo(expected.Metadata.SongId));
            Assert.That(actual.Metadata.EncodingVersion, Is.EqualTo(expected.Metadata.EncodingVersion));
            Assert.That(actual.Metadata.InitialBpm, Is.EqualTo(expected.Metadata.InitialBpm));
            Assert.That(actual.Metadata.Title, Is.EqualTo(expected.Metadata.Title));
            Assert.That(actual.Metadata.Artist, Is.EqualTo(expected.Metadata.Artist));
            Assert.That(actual.Metadata.NoteArranger, Is.EqualTo(expected.Metadata.NoteArranger));
            Assert.That(actual.Metadata.OjmFileName, Is.EqualTo(expected.Metadata.OjmFileName));
            Assert.That(actual.Metadata.Levels, Is.EqualTo(expected.Metadata.Levels));
            Assert.That(actual.Metadata.Durations, Is.EqualTo(expected.Metadata.Durations));
            Assert.That(actual.Metadata.Cover, Is.Empty);
            Assert.That(actual.Metadata.Thumbnail, Is.Empty);
            Assert.That(actual.Charts, Has.Count.EqualTo(1));
            Assert.That(actualChart.Difficulty, Is.EqualTo(expectedChart.Difficulty));
            Assert.That(actualChart.Level, Is.EqualTo(expectedChart.Level));
            Assert.That(actualChart.BpmEvents, Is.EqualTo(expectedChart.BpmEvents));
            Assert.That(actualChart.Notes, Is.EqualTo(expectedChart.Notes));
            Assert.That(actualChart.MeasureFractions, Is.EqualTo(expectedChart.MeasureFractions));
            Assert.That(actualChart.MeasureCount, Is.EqualTo(expectedChart.MeasureCount));
            Assert.That(stream.BytesRead, Is.EqualTo(expectedBytesRead));
            Assert.That(stream.CanRead, Is.True, "The caller owns the input stream.");
        });
    }

    [Test, Combinatorial]
    public void SelectedChartRejectsIncompleteEncryptedInput([Values(false, true)] bool canSeek,
                                                            [Values(0, 1, 2, 3, 4, 7, 8, 307)] int length)
    {
        // Even a complete encryption header cannot make a truncated OJN header playable.
        var bytes = encrypt(new byte[299])[..length];
        using var stream = new BudgetCountingStream(bytes, canSeek, 1);
        var reader = new OjnReader();
        TestDelegate read = () => reader.ReadChart(stream, OjnDifficulty.EX);

        if (length < 3)
            Assert.Throws<EndOfStreamException>(read);
        else
            Assert.Throws<InvalidDataException>(read);
        Assert.That(stream.CanRead, Is.True, "Decode failure must not dispose the caller's stream.");
    }
}
