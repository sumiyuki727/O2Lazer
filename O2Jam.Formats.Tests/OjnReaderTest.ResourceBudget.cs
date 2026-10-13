using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using NUnit.Framework;
using O2Jam.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class OjnReaderTest
{
    [Test, Combinatorial]
    public void SourceBudgetCoversEveryReaderEntry([Values(false, true)] bool encrypted,
                                                  [Values(-1, 0, 1)] int allowance,
                                                  [Values("Array", "Stream", "SelectedSeek", "SelectedNonSeek")] string entry)
    {
        var bytes = OjnTestData.CreateChart();
        if (encrypted)
            bytes = encrypt(bytes);
        var limit = bytes.Length + allowance;
        var reader = new OjnReader(OjnMetadataEncoding.Automatic, null, new OjnReadLimits(limit, 4));
        using var stream = new BudgetCountingStream(bytes, entry != "SelectedNonSeek");
        OjnDocument? result = null;
        TestDelegate read = () => result = entry switch
        {
            "Array" => reader.Read(bytes),
            "Stream" => reader.Read(stream),
            _ => reader.ReadChart(stream, OjnDifficulty.EX),
        };

        if (allowance < 0)
        {
            Assert.That(Assert.Throws<InvalidDataException>(read)!.Message, Does.Contain("source byte budget"));
            Assert.That(stream.BytesRead, Is.EqualTo(entry == "SelectedNonSeek" ? limit + 1 : 0));
        }
        else
        {
            read();
            Assert.That(result!.Charts[0].Notes.Single().SampleId, Is.Zero);
            Assert.That(result.Charts[0].Notes.Single().TailSampleId, Is.EqualTo(1));
        }
        Assert.That(stream.CanRead, Is.True, "The caller owns the input stream.");
    }

    [Test]
    public void FullStreamDecodePreservesCurrentPositionSemantics()
    {
        var bytes = OjnTestData.CreateChart();
        using var stream = new BudgetCountingStream([0, 0, 0, 0, .. bytes], true);
        stream.Position = 4;
        var reader = new OjnReader(OjnMetadataEncoding.Automatic, null, new OjnReadLimits(bytes.Length + 4, 4));
        Assert.That(reader.Read(stream).Charts[0].Notes.Single().SampleId, Is.Zero);
        Assert.That(stream.BytesRead, Is.EqualTo(bytes.Length));
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnknownLengthShortReadsStopAtByteBudget(bool encrypted)
    {
        var bytes = OjnTestData.CreateChart();
        if (encrypted)
            bytes = encrypt(bytes);
        using var stream = new BudgetCountingStream(bytes, false, 2);
        var reader = new OjnReader(OjnMetadataEncoding.Automatic, null, new OjnReadLimits(bytes.Length - 2, 4));
        Assert.That(Assert.Throws<InvalidDataException>(() => reader.Read(stream))!.Message, Does.Contain("source byte budget"));
        Assert.That(stream.BytesRead, Is.EqualTo(bytes.Length - 1));
        Assert.That(stream.CanRead, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnknownLengthShortReadsAcceptExactByteBudget(bool encrypted)
    {
        var bytes = OjnTestData.CreateChart();
        if (encrypted)
            bytes = encrypt(bytes);
        using var stream = new BudgetCountingStream(bytes, false, 2);
        var reader = new OjnReader(OjnMetadataEncoding.Automatic, null, new OjnReadLimits(bytes.Length, 4));
        Assert.That(reader.ReadChart(stream, OjnDifficulty.EX).Charts[0].Notes.Single().Type, Is.EqualTo(OjnNoteType.Hold));
        Assert.That(stream.BytesRead, Is.EqualTo(bytes.Length));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void EventBudgetCountsLnTailBeforeNormalisation(bool encrypted, bool selected)
    {
        var bytes = OjnTestData.CreateChart();
        if (encrypted)
            bytes = encrypt(bytes);
        var reader = new OjnReader(OjnMetadataEncoding.Automatic, null, new OjnReadLimits(bytes.Length, 3));
        using var stream = new MemoryStream(bytes);
        Assert.That(Assert.Throws<InvalidDataException>(() =>
        {
            if (selected) reader.ReadChart(stream, OjnDifficulty.EX);
            else reader.Read(bytes);
        })!.Message, Does.Contain("materialised event budget"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FullDecodeSharesEventBudgetAcrossThreeCharts(bool encrypted)
    {
        var bytes = createBudgetCharts();
        if (encrypted)
            bytes = encrypt(bytes);
        var exact = new OjnReader(OjnMetadataEncoding.Automatic, null, new OjnReadLimits(bytes.Length, 12));
        Assert.That(exact.Read(bytes).Charts.Select(chart => chart.Notes.Count), Is.EqualTo(new[] { 1, 1, 1 }));
        var limited = new OjnReader(OjnMetadataEncoding.Automatic, null, new OjnReadLimits(bytes.Length, 11));
        Assert.That(Assert.Throws<InvalidDataException>(() => limited.Read(bytes))!.Message, Does.Contain("materialised event budget"));
    }

    [TestCase(int.MaxValue)]
    [TestCase(1)]
    [TestCase(2)]
    public void SelectedChartSkipsOtherChartsAndEmbeddedImages(int chunkSize)
    {
        var chartBytes = createBudgetCharts();
        var bytes = new byte[chartBytes.Length + 1024];
        chartBytes.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(268), 1024);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(68), uint.MaxValue);
        using var stream = new BudgetCountingStream(bytes, true, chunkSize);
        var reader = new OjnReader(OjnMetadataEncoding.Automatic, null, new OjnReadLimits(bytes.Length, 4));
        var result = reader.ReadChart(stream, OjnDifficulty.EX);
        Assert.Multiple(() =>
        {
            Assert.That(result.Charts, Has.Count.EqualTo(1));
            Assert.That(result.Charts[0].Notes, Has.Count.EqualTo(1));
            Assert.That(result.Metadata.Cover, Is.Empty);
            Assert.That(stream.BytesRead, Is.EqualTo(375), "Only the prefix, header and selected chart may be read.");
        });
    }

    [Test]
    public void EventBudgetStopsReadingBeforeRemainingBlocks()
    {
        var bytes = OjnTestData.CreateChart();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(300), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(304), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(306), 16);
        for (var offset = 308; offset < bytes.Length; offset += 4)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(64), 1);
        using var stream = new BudgetCountingStream(bytes, true);
        var reader = new OjnReader(OjnMetadataEncoding.Automatic, null, new OjnReadLimits(bytes.Length, 2));
        Assert.That(Assert.Throws<InvalidDataException>(() => reader.ReadChart(stream, OjnDifficulty.EX))!.Message,
            Does.Contain("materialised event budget"));
        Assert.That(stream.BytesRead, Is.EqualTo(323));
    }

    [Test]
    public void DuplicateFractionsStillConsumeEventBudget()
    {
        var bytes = OjnTestData.CreateChart();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(64), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(316), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(318), 1);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(320), 0.75f);
        var reader = new OjnReader(OjnMetadataEncoding.Automatic, null, new OjnReadLimits(bytes.Length, 1));
        Assert.That(Assert.Throws<InvalidDataException>(() => reader.Read(bytes))!.Message, Does.Contain("materialised event budget"));
        var exact = new OjnReader(OjnMetadataEncoding.Automatic, null, new OjnReadLimits(bytes.Length, 2));
        Assert.That(exact.Read(bytes).Charts[0].MeasureFractions.Single().Fraction, Is.EqualTo(0.75));
    }

    [Test]
    public void DiscardedOrphanReleaseStillConsumesBudget()
    {
        var bytes = OjnTestData.CreateChart();
        bytes[323] = 3;
        var reader = new OjnReader(OjnMetadataEncoding.Automatic, null, new OjnReadLimits(bytes.Length, 3));
        Assert.That(Assert.Throws<InvalidDataException>(() => reader.Read(bytes))!.Message, Does.Contain("materialised event budget"));
        var exact = new OjnReader(OjnMetadataEncoding.Automatic, null, new OjnReadLimits(bytes.Length, 4));
        Assert.That(exact.Read(bytes).Charts[0].Notes, Is.Empty);
    }

    [Test]
    public void EmptySlotsAndIgnoredTimingValuesDoNotConsumeBudget()
    {
        var bytes = OjnTestData.CreateChart();
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(308), float.NaN);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(344), -10);
        var reader = new OjnReader(OjnMetadataEncoding.Automatic, null, new OjnReadLimits(bytes.Length, 2));
        var chart = reader.Read(bytes).Charts[0];
        Assert.That(chart.Notes, Has.Count.EqualTo(1));
        Assert.That(chart.BpmEvents, Is.Empty);
        Assert.That(chart.MeasureFractions, Is.Empty);
    }

    private static byte[] createBudgetCharts()
    {
        var original = OjnTestData.CreateChart();
        var bytes = new byte[300 + 72 * 3];
        original.AsSpan(0, 300).CopyTo(bytes);
        for (var index = 0; index < 3; index++)
        {
            original.AsSpan(300).CopyTo(bytes.AsSpan(300 + 72 * index));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(64 + index * 4), 4);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(284 + index * 4), (uint)(300 + 72 * index));
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(296), (uint)bytes.Length);
        return bytes;
    }

    private sealed class BudgetCountingStream(byte[] data, bool canSeek, int chunkSize = int.MaxValue) : Stream
    {
        private readonly MemoryStream source = new(data, writable: false);
        internal int BytesRead { get; private set; }
        public override bool CanRead => source.CanRead;
        public override bool CanSeek => canSeek;
        public override bool CanWrite => false;
        public override long Length => canSeek ? source.Length : throw new NotSupportedException();
        public override long Position { get => canSeek ? source.Position : throw new NotSupportedException(); set => source.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var read = source.Read(buffer[..Math.Min(buffer.Length, chunkSize)]);
            BytesRead += read;
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => canSeek ? source.Seek(offset, origin) : throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) source.Dispose();
            base.Dispose(disposing);
        }
    }
}
