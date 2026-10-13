using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using O2Jam.Formats.Ojm;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class OjmReaderTest
{
    [Test, Combinatorial]
    public void NonSeekBudgetCoversReadAndIndex([Values(false, true)] bool indexOnly,
                                              [Values(-1, 0, 1)] int allowance,
                                              [Values(1, 2, int.MaxValue)] int chunkSize)
    {
        var bytes = createM30(0, [(0, (ushort)5, "zero"u8.ToArray())]);
        using var stream = new OjmCountingStream(bytes, false, chunkSize);
        var reader = new OjmReader(new OjmReadLimits(bytes.Length + allowance, 4, 4));
        TestDelegate read = () =>
        {
            if (indexOnly)
                Assert.That(reader.ReadIndex(stream).SampleIds, Is.EquivalentTo(new[] { 0 }));
            else
                Assert.That(reader.Read(stream).Samples[0].Data, Is.EqualTo("zero"u8.ToArray()));
        };
        if (allowance < 0)
            Assert.That(Assert.Throws<InvalidDataException>(read)!.Message, Does.Contain("non-seek source byte budget"));
        else
            read();
        Assert.That(stream.BytesRead, Is.EqualTo(bytes.Length));
        Assert.That(stream.CanRead, Is.True, "The caller owns the input stream.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnknownLengthStopsAfterOneExcessProbe(bool indexOnly)
    {
        using var stream = new OjmCountingStream(new byte[1024], false, 2);
        var reader = new OjmReader(new OjmReadLimits(17, 0, 0));
        Assert.Throws<InvalidDataException>(() =>
        {
            if (indexOnly) reader.ReadIndex(stream);
            else reader.Read(stream);
        });
        Assert.That(stream.BytesRead, Is.EqualTo(18));
        Assert.That(stream.CanRead, Is.True);
    }

    [TestCase("OJM", 47)]
    [TestCase("OMC", 47)]
    [TestCase("M30", 3)]
    public void SingleBudgetRejectsBeforeReadingPayload(string format, int singleLimit)
    {
        using var stream = new OjmCountingStream(budgetArchive(format), true);
        var reader = new OjmReader(new OjmReadLimits(0, singleLimit, 1024));
        Assert.That(Assert.Throws<InvalidDataException>(() => reader.Read(stream))!.Message,
            Does.Contain("sample exceeds the supported output byte budget"));
        Assert.That(stream.BytesRead, Is.EqualTo(format == "M30" ? 80 : 76));
        Assert.That(stream.CanRead, Is.True);
    }

    [TestCase("OJM", 48, 96)]
    [TestCase("OMC", 48, 96)]
    [TestCase("M30", 4, 8)]
    public void ExactSingleAndCumulativeBudgetsKeepCompleteOutput(string format, int singleLimit, long eagerLimit)
    {
        var bytes = budgetArchive(format);
        var expected = new OjmReader().Read(new MemoryStream(bytes));
        var reader = new OjmReader(new OjmReadLimits(0, singleLimit, eagerLimit));
        // A budget belongs to a read, even when the same reader is reused.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var stream = new OjmCountingStream(bytes, true);
            var archive = reader.Read(stream);
            Assert.That(archive.DecodedByteLength, Is.EqualTo(eagerLimit));
            foreach (var (id, sample) in expected.Samples)
                Assert.That(archive.Samples[id].Data, Is.EqualTo(sample.Data));
        }
    }

    [TestCase("OJM", 48, 95)]
    [TestCase("OMC", 48, 95)]
    [TestCase("M30", 4, 7)]
    public void CumulativeBudgetRejectsBeforeSecondPayload(string format, int singleLimit, long eagerLimit)
    {
        using var stream = new OjmCountingStream(budgetArchive(format), true);
        var reader = new OjmReader(new OjmReadLimits(0, singleLimit, eagerLimit));
        Assert.That(Assert.Throws<InvalidDataException>(() => reader.Read(stream))!.Message,
            Does.Contain("eager output byte budget"));
        Assert.That(stream.BytesRead, Is.EqualTo(136));
    }

    [TestCase("OJM")]
    [TestCase("OMC")]
    public void SkippedWaveDoesNotConsumeOutputBudgetAndRetainsCipherState(string format)
    {
        var bytes = createOmcEffects(Enumerable.Range(0, 37).Select(x => (byte)x).ToArray(), "one!"u8.ToArray());
        if (format == "OJM") { bytes[1] = (byte)'J'; bytes[2] = (byte)'M'; }
        var expected = new OjmReader().Read(new MemoryStream(bytes)).Samples[1].Data;
        var archive = new OjmReader(new OjmReadLimits(0, 48, 48)).Read(new MemoryStream(bytes), new HashSet<int> { 1 });
        Assert.That(archive.Samples.Keys, Is.EquivalentTo(new[] { 1 }));
        Assert.That(archive.Samples[1].Data, Is.EqualTo(expected));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnsupportedUnselectedEmptyAndDuplicateM30PayloadsAreNotMaterialised(bool selected)
    {
        var bytes = createM30(0, [(0, (ushort)5, "zero"u8.ToArray()), (2, (ushort)17, new byte[60]),
            (0, (ushort)5, new byte[80]), (1, (ushort)5, "one!"u8.ToArray()), (3, (ushort)5, [])]);
        var archive = new OjmReader(new OjmReadLimits(0, 4, 8)).Read(new MemoryStream(bytes), selected ? new HashSet<int> { 0, 1 } : null);
        Assert.That(archive.Samples.Keys, Is.EquivalentTo(new[] { 0, 1 }));
        Assert.That(archive.Samples[0].Data, Is.EqualTo("zero"u8.ToArray()), "The first reference still wins.");
        Assert.That(archive.Samples[1].Data, Is.EqualTo("one!"u8.ToArray()));
    }

    [Test]
    public void M30TruncatedFinalPayloadDoesNotConsumeBudgetOrHideEarlierOutput()
    {
        var bytes = budgetArchive("M30")[..^2];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 4);
        var archive = new OjmReader(new OjmReadLimits(0, 4, 4)).Read(new MemoryStream(bytes));
        Assert.That(archive.Samples.Keys, Is.EquivalentTo(new[] { 0 }));
    }

    [Test, Combinatorial]
    public void HugeVirtualPayloadRejectsBeforeAllocation([Values("OJM", "OMC", "M30")] string format,
                                                        [Values(134217728u, 2147483648u, uint.MaxValue)] uint size)
    {
        var prefixLength = format == "M30" ? 80 : 76;
        var prefix = budgetArchive(format)[..prefixLength];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(format == "M30" ? 60 : 72), size);
        using var stream = new OjmVirtualArchive(prefix, prefixLength + (long)size);
        var reader = new OjmReader(new OjmReadLimits(0, 128 * 1024 * 1024 - 1, long.MaxValue));
        Assert.That(Assert.Throws<InvalidDataException>(() => reader.Read(stream))!.Message, Does.Contain("output byte budget"));
        Assert.That(stream.Position, Is.EqualTo(prefixLength));
    }

    [Test]
    public void SeekIndexMaySkipHugePayloadOutsideBufferAndOutputBudgets()
    {
        var prefix = createM30(0, [(0, (ushort)5, [])]);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(60), uint.MaxValue);
        using var stream = new OjmVirtualArchive(prefix, prefix.Length + (long)uint.MaxValue);
        Assert.That(new OjmReader(new OjmReadLimits(0, 0, 0)).ReadIndex(stream).SampleIds, Is.EquivalentTo(new[] { 0 }));
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
    }

    [TestCase("OJM", 47, 48)]
    [TestCase("OMC", 47, 48)]
    [TestCase("M30", 3, 4)]
    public void LazyBudgetChecksMaterialisationAndIncludesWaveWrapper(string format, int shortLimit, int exactLimit)
    {
        withArchive(format, (path, bytes) =>
        {
            var expected = new OjmReader().Read(new MemoryStream(bytes));
            var limited = new OjmReader(new OjmReadLimits(0, shortLimit, 0)).ReadLazy(path);
            Assert.That(limited.Samples.Values, Has.All.Matches<OjmSample>(sample => !sample.IsLoaded));
            Assert.Throws<InvalidDataException>(() => _ = limited.Samples[0].Data);
            Assert.That(limited.IsSourceInvalidated, Is.False);
            Assert.That(limited.Samples[0].IsLoaded, Is.False);
            var exact = new OjmReader(new OjmReadLimits(0, exactLimit, 0)).ReadLazy(path);
            foreach (var (id, sample) in expected.Samples)
                Assert.That(exact.Samples[id].Data, Is.EqualTo(sample.Data));
        });
    }

    [Test]
    public void LazySizeFailureDoesNotOpenSourceOrRetireOtherSamples()
    {
        withArchive("M30", (path, _) =>
        {
            var bytes = createM30(0, [(0, (ushort)5, new byte[5]), (1, (ushort)5, "one!"u8.ToArray())]);
            File.WriteAllBytes(path, bytes);
            var stamp = File.GetLastWriteTimeUtc(path);
            var archive = new OjmReader(new OjmReadLimits(0, 4, 0)).ReadLazy(path);
            File.Delete(path);
            Assert.That(Assert.Throws<InvalidDataException>(() => _ = archive.Samples[0].Data)!.Message, Does.Contain("output byte budget"));
            Assert.That(archive.IsSourceInvalidated, Is.False);
            File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, stamp);
            Assert.That(archive.Samples[1].Data, Is.EqualTo("one!"u8.ToArray()));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EmptyNonSeekInputKeepsHeaderFailureAndCallerOwnership(bool indexOnly)
    {
        using var stream = new OjmCountingStream([], false);
        var reader = new OjmReader(new OjmReadLimits(0, 0, 0));
        Assert.That(Assert.Throws<InvalidDataException>(() =>
        {
            if (indexOnly) reader.ReadIndex(stream);
            else reader.Read(stream);
        })!.Message, Does.Contain("header is truncated"));
        Assert.That(stream.CanRead, Is.True);
    }

    private static byte[] budgetArchive(string format)
    {
        if (format == "M30")
            return createM30(0, [(0, (ushort)5, "zero"u8.ToArray()), (1, (ushort)5, "one!"u8.ToArray())]);
        var bytes = createOmcEffects("zero"u8.ToArray(), "one!"u8.ToArray());
        if (format == "OJM") { bytes[1] = (byte)'J'; bytes[2] = (byte)'M'; }
        return bytes;
    }

    [TestCase("OJM", 51)]
    [TestCase("OMC", 51)]
    [TestCase("OJM", 52)]
    [TestCase("OMC", 52)]
    public void WaveAndBackgroundShareEagerBudget(string format, long limit)
    {
        var bytes = createOmcEffects("zero"u8.ToArray());
        if (format == "OJM") { bytes[1] = (byte)'J'; bytes[2] = (byte)'M'; }
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), 1);
        using var memory = new MemoryStream();
        memory.Write(bytes);
        using (var writer = new BinaryWriter(memory, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writeFixed(writer, "background", 32);
            writer.Write(4u);
            writer.Write("ogg!"u8);
        }
        using var stream = new OjmCountingStream(memory.ToArray(), true);
        var reader = new OjmReader(new OjmReadLimits(0, 48, limit));
        if (limit < 52)
        {
            Assert.That(Assert.Throws<InvalidDataException>(() => reader.Read(stream))!.Message, Does.Contain("eager output byte budget"));
            Assert.That(stream.BytesRead, Is.EqualTo(116));
        }
        else
        {
            var archive = reader.Read(stream);
            Assert.That(archive.Samples[1000].Data, Is.EqualTo("ogg!"u8.ToArray()));
            Assert.That(archive.DecodedByteLength, Is.EqualTo(52));
        }
    }

    [TestCase("OJM")]
    [TestCase("OMC")]
    [TestCase("M30")]
    public void EmptySlotsDoNotConsumeZeroOutputBudget(string format)
    {
        var bytes = format == "M30" ? createM30(0, [(0, (ushort)5, [])]) : createOmcEffects(Array.Empty<byte>());
        if (format == "OJM") { bytes[1] = (byte)'J'; bytes[2] = (byte)'M'; }
        Assert.That(new OjmReader(new OjmReadLimits(0, 0, 0)).Read(new MemoryStream(bytes)).Samples, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HugeM30PayloadCanBeSkippedWithoutOutputAllocation(bool unsupported)
    {
        var prefix = createM30(0, [(0, (ushort)5, [])]);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(60), uint.MaxValue);
        if (unsupported)
            BinaryPrimitives.WriteUInt16LittleEndian(prefix.AsSpan(64), 17);
        using var stream = new OjmVirtualArchive(prefix, prefix.Length + (long)uint.MaxValue);
        var reader = new OjmReader(new OjmReadLimits(0, 0, 0));
        Assert.That(reader.Read(stream, unsupported ? null : new HashSet<int>()).Samples, Is.Empty);
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
    }

    private sealed class OjmCountingStream(byte[] data, bool canSeek, int chunkSize = int.MaxValue) : Stream
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
        protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
    }

    private sealed class OjmVirtualArchive(byte[] prefix, long length) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get; set; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (Position >= prefix.Length)
                throw new InvalidOperationException("Payload access must be rejected before this point.");
            var read = Math.Min(buffer.Length, prefix.Length - (int)Position);
            prefix.AsSpan((int)Position, read).CopyTo(buffer);
            Position += read;
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            _ => Length + offset,
        };
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
