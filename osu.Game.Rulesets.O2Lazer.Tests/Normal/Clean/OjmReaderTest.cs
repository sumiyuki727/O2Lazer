using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Formats.Ojm;
using osu.Game.Rulesets.O2Lazer.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class OjmReaderTest
{
    [Test]
    public void ReadsUnencryptedWaveAndBackgroundOgg()
    {
        byte[] pcm = [1, 2, 3, 4];
        byte[] ogg = [(byte)'O', (byte)'g', (byte)'g', (byte)'S'];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(new byte[] { (byte)'O', (byte)'J', (byte)'M', 0 });
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write((uint)20);
        writer.Write((uint)80);
        writer.Write((uint)120);

        writeFixed(writer, "kick", 32);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write((uint)44100);
        writer.Write((uint)88200);
        writer.Write((ushort)2);
        writer.Write((ushort)16);
        writer.Write((uint)0);
        writer.Write((uint)pcm.Length);
        writer.Write(pcm);

        writeFixed(writer, "music", 32);
        writer.Write((uint)ogg.Length);
        writer.Write(ogg);

        var bytes = stream.ToArray();
        var archive = new OjmReader().Read(new MemoryStream(bytes));
        var index = new OjmReader().ReadIndex(new MemoryStream(bytes));

        Assert.Multiple(() =>
        {
            Assert.That(archive.Samples.Keys, Is.EquivalentTo(new[] { 0, 1000 }));
            Assert.That(index.SampleIds, Is.EquivalentTo(archive.Samples.Keys));
            Assert.That(Encoding.ASCII.GetString(archive.Samples[0].Data, 0, 4), Is.EqualTo("RIFF"));
            Assert.That(archive.Samples[0].Data.TakeLast(4), Is.EqualTo(pcm));
            Assert.That(archive.Samples[1000].Data, Is.EqualTo(ogg));
        });
    }

    [Test]
    public void EmptyBackgroundSlotsRemainIntentionalSilence()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(new byte[] { (byte)'O', (byte)'J', (byte)'M', 0 });
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((uint)20);
        writer.Write((uint)20);
        writer.Write((uint)56);
        writeFixed(writer, "empty", 32);
        writer.Write((uint)0);

        var bytes = stream.ToArray();
        var archive = new OjmReader().Read(new MemoryStream(bytes));
        var index = new OjmReader().ReadIndex(new MemoryStream(bytes));

        Assert.Multiple(() =>
        {
            Assert.That(archive.Samples, Is.Empty);
            Assert.That(index.SampleIds, Is.Empty);
        });
    }

    [Test]
    public void DecodesNamiM30Sample()
    {
        byte[] decoded = [(byte)'O', (byte)'g', (byte)'g', (byte)'S', 1, 2, 3, 4];
        byte[] encoded = decoded.ToArray();
        byte[] nami = [(byte)'n', (byte)'a', (byte)'m', (byte)'i'];
        for (var index = 0; index < encoded.Length; index++)
            encoded[index] ^= nami[index % 4];

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(new byte[] { (byte)'M', (byte)'3', (byte)'0', 0 });
        writer.Write((uint)1);
        writer.Write((uint)16);
        writer.Write((uint)1);
        writer.Write((uint)28);
        writer.Write((uint)(28 + 52 + encoded.Length));
        writer.Write((uint)0);
        writeFixed(writer, "sample", 32);
        writer.Write((uint)encoded.Length);
        writer.Write((ushort)5);
        writer.Write((ushort)0);
        writer.Write((uint)0);
        writer.Write((ushort)7);
        writer.Write((ushort)0);
        writer.Write((uint)0);
        writer.Write(encoded);

        var archive = new OjmReader().Read(new MemoryStream(stream.ToArray()));

        Assert.That(archive.Samples[7].Data, Is.EqualTo(decoded));
    }

    [Test]
    public void Decodes0412M30SampleAndLeavesTrailingBytesUntouched()
    {
        byte[] decoded = [(byte)'O', (byte)'g', (byte)'g', (byte)'S', 9];
        var encoded = decoded.ToArray();
        byte[] mask = [(byte)'0', (byte)'4', (byte)'1', (byte)'2'];
        for (var index = 0; index + mask.Length <= encoded.Length; index += mask.Length)
        {
            for (var keyIndex = 0; keyIndex < mask.Length; keyIndex++)
                encoded[index + keyIndex] ^= mask[keyIndex];
        }

        var archive = new OjmReader().Read(new MemoryStream(createM30(32, [(5, (ushort)5, encoded)])));

        Assert.That(archive.Samples[5].Data, Is.EqualTo(decoded));
    }

    [Test]
    public void UnderReportedPayloadSizeDoesNotHideLaterM30Samples()
    {
        var bytes = createM30(0,
            [(0, (ushort)5, "zero"u8.ToArray()), (1, (ushort)5, "one!"u8.ToArray())]);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20, 4), 56);

        var archive = new OjmReader().Read(new MemoryStream(bytes));

        Assert.That(archive.Samples.Keys, Is.EquivalentTo(new[] { 0, 1 }));
    }

    [Test]
    public void OverReportedM30CountKeepsCompleteEarlierSamples()
    {
        var bytes = createM30(0, [(1, (ushort)5, "one!"u8.ToArray())]);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12, 4), 2);
        bytes = [.. bytes, 0xff];

        var archive = new OjmReader().Read(new MemoryStream(bytes));

        Assert.That(archive.Samples[1].Data, Is.EqualTo("one!"u8.ToArray()));
    }

    [Test]
    public void M30ReferencesRemainCanonicalForOjnZeroBasedLookup()
    {
        var archive = new OjmReader().Read(new MemoryStream(createM30(0,
            [(0, (ushort)5, "zero"u8.ToArray()), (1, (ushort)5, "one!"u8.ToArray()), (0, (ushort)0, "bgm!"u8.ToArray())])));
        var chart = new OjnReader().Read(new MemoryStream(OjnReaderTest.CreateChart())).Charts[0];

        Assert.Multiple(() =>
        {
            Assert.That(chart.Notes.Select(note => note.SampleId), Is.EqualTo(new[] { 0 }));
            Assert.That(chart.Notes.Select(note => note.TailSampleId), Is.EqualTo(new int?[] { 1 }));
            Assert.That(archive.Samples.Keys, Is.EquivalentTo(new[] { 0, 1, 1000 }));
        });
    }

    [Test]
    public void MissingNormalisedM30ReferenceDoesNotBorrowNextSample()
    {
        var archive = new OjmReader().Read(new MemoryStream(createM30(0,
            [(1, (ushort)5, "wrong"u8.ToArray())])));
        var chart = new OjnReader().Read(new MemoryStream(OjnReaderTest.CreateChart())).Charts[0];

        Assert.Multiple(() =>
        {
            Assert.That(chart.Notes[0].SampleId, Is.Zero);
            Assert.That(archive.Samples.ContainsKey(chart.Notes[0].SampleId), Is.False);
            Assert.That(archive.Samples[1].Data, Is.EqualTo("wrong"u8.ToArray()));
        });
    }

    [Test]
    public void LightweightIndexUsesSameM30IdMapping()
    {
        var bytes = createM30(0,
            [(0, (ushort)5, "zero"u8.ToArray()), (1, (ushort)5, "one!"u8.ToArray()), (0, (ushort)0, "bgm!"u8.ToArray())]);

        var index = new OjmReader().ReadIndex(new MemoryStream(bytes));

        Assert.That(index.SampleIds, Is.EquivalentTo(new[] { 0, 1, 1000 }));
    }

    [Test]
    public void ReferencedSampleReadSkipsUnusedM30Payloads()
    {
        var bytes = createM30(0,
            [(0, (ushort)5, "zero"u8.ToArray()), (1, (ushort)5, "one!"u8.ToArray()), (0, (ushort)0, "bgm!"u8.ToArray())]);

        var archive = new OjmReader().Read(new MemoryStream(bytes), new HashSet<int> { 1, 1000 });

        Assert.Multiple(() =>
        {
            Assert.That(archive.Samples.Keys, Is.EquivalentTo(new[] { 1, 1000 }));
            Assert.That(archive.Samples[1].Data, Is.EqualTo("one!"u8.ToArray()));
            Assert.That(archive.Samples[1000].Data, Is.EqualTo("bgm!"u8.ToArray()));
        });
    }

    [Test]
    public void FilteredOmcReadPreservesEncryptionStateAcrossSkippedEffects()
    {
        var bytes = createOmcEffects(
            Enumerable.Range(0, 37).Select(value => (byte)value).ToArray(),
            Enumerable.Range(50, 41).Select(value => (byte)value).ToArray());

        var full = new OjmReader().Read(new MemoryStream(bytes));
        var filtered = new OjmReader().Read(new MemoryStream(bytes), new HashSet<int> { 1 });

        Assert.Multiple(() =>
        {
            Assert.That(filtered.Samples.Keys, Is.EquivalentTo(new[] { 1 }));
            Assert.That(filtered.Samples[1].Data, Is.EqualTo(full.Samples[1].Data));
        });
    }

    [Test]
    public void LazyOmcReadDefersPayloadAndPreservesEncryptionState()
    {
        var bytes = createOmcEffects(
            Enumerable.Range(0, 37).Select(value => (byte)value).ToArray(),
            Enumerable.Range(50, 41).Select(value => (byte)value).ToArray());
        var path = Path.Combine(Path.GetTempPath(), $"o2lazer-lazy-omc-{Guid.NewGuid():N}.ojm");

        try
        {
            File.WriteAllBytes(path, bytes);
            var eager = new OjmReader().Read(new MemoryStream(bytes), new HashSet<int> { 1 });
            var lazy = new OjmReader().ReadLazy(path, new HashSet<int> { 1 });

            Assert.Multiple(() =>
            {
                Assert.That(lazy.Samples[1].IsLoaded, Is.False);
                Assert.That(lazy.Samples[1].Data, Is.EqualTo(eager.Samples[1].Data));
                Assert.That(lazy.Samples[1].IsLoaded, Is.True);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void LazyM30ReadDefersSelectedPayload()
    {
        var bytes = createM30(32, [(5, (ushort)5, "payload!"u8.ToArray())]);
        var path = Path.Combine(Path.GetTempPath(), $"o2lazer-lazy-m30-{Guid.NewGuid():N}.ojm");

        try
        {
            File.WriteAllBytes(path, bytes);
            var eager = new OjmReader().Read(new MemoryStream(bytes), new HashSet<int> { 5 });
            var lazy = new OjmReader().ReadLazy(path, new HashSet<int> { 5 });

            Assert.Multiple(() =>
            {
                Assert.That(lazy.Samples[5].IsLoaded, Is.False);
                Assert.That(lazy.Samples[5].Data, Is.EqualTo(eager.Samples[5].Data));
                Assert.That(lazy.Samples[5].IsLoaded, Is.True);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void LazyFullArchiveIndexesAllPayloadsWithoutLoadingThem()
    {
        var bytes = createM30(0,
            [(5, (ushort)5, "effect"u8.ToArray()), (0, (ushort)0, "background"u8.ToArray())]);
        var path = Path.Combine(Path.GetTempPath(), $"o2lazer-lazy-full-{Guid.NewGuid():N}.ojm");

        try
        {
            File.WriteAllBytes(path, bytes);
            var archive = new OjmReader().ReadLazy(path, null);

            Assert.Multiple(() =>
            {
                Assert.That(archive.Samples.Keys, Is.EquivalentTo(new[] { 5, 1000 }));
                Assert.That(archive.Samples.Values, Has.All.Matches<OjmSample>(sample => !sample.IsLoaded));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] createM30(uint encodingCode, params (ushort Reference, ushort Codec, byte[] Payload)[] samples)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(new byte[] { (byte)'M', (byte)'3', (byte)'0', 0 });
        writer.Write((uint)1);
        writer.Write(encodingCode);
        writer.Write((uint)samples.Length);
        writer.Write((uint)28);
        writer.Write((uint)samples.Sum(sample => 52 + sample.Payload.Length));
        writer.Write((uint)0);

        foreach (var (reference, codec, payload) in samples)
        {
            writeFixed(writer, "sample", 32);
            writer.Write((uint)payload.Length);
            writer.Write(codec);
            writer.Write((ushort)0);
            writer.Write((uint)0);
            writer.Write(reference);
            writer.Write((ushort)0);
            writer.Write((uint)0);
            writer.Write(payload);
        }

        return stream.ToArray();
    }

    private static byte[] createOmcEffects(params byte[][] payloads)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        var backgroundOffset = 20 + payloads.Sum(payload => 56 + payload.Length);

        writer.Write(new byte[] { (byte)'O', (byte)'M', (byte)'C', 0 });
        writer.Write((ushort)payloads.Length);
        writer.Write((ushort)0);
        writer.Write((uint)20);
        writer.Write((uint)backgroundOffset);
        writer.Write((uint)backgroundOffset);

        for (var index = 0; index < payloads.Length; index++)
        {
            var payload = payloads[index];
            writeFixed(writer, $"effect{index}", 32);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            writer.Write((uint)44100);
            writer.Write((uint)88200);
            writer.Write((ushort)2);
            writer.Write((ushort)16);
            writer.Write((uint)0);
            writer.Write((uint)payload.Length);
            writer.Write(payload);
        }

        return stream.ToArray();
    }

    private static void writeFixed(BinaryWriter writer, string value, int length)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        writer.Write(bytes);
        writer.Write(new byte[length - bytes.Length]);
    }
}
