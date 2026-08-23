using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.O2Jam;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.O2Jam;

[TestFixture]
public class O2JamDecoderTest
{
    private string directory = null!;

    [SetUp]
    public void SetUp()
    {
        OjmDecoder.ClearCache();
        directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"o2jam-decoder-{Guid.NewGuid()}");
        Directory.CreateDirectory(directory);
    }

    [TearDown]
    public void TearDown()
    {
        OjmDecoder.ClearCache();
        Directory.Delete(directory, true);
    }

    [Test]
    public void TestZeroBasedM30MapsReferencesDown()
    {
        File.WriteAllBytes(Path.Combine(directory, "test.ojm"), createM30([(0, "zero"u8.ToArray()), (1, "one!"u8.ToArray())]));
        var charts = OjnDecoder.DecodeAll(createOjn([[1, 2], [1, 2], [1, 2]]), Path.Combine(directory, "test.ojn"));

        Assert.Multiple(() =>
        {
            Assert.That(charts.Single(chart => chart.Difficulty == OjnDifficulty.EX).ParseResult.SampleDefinitions.Keys,
                Is.EquivalentTo(new ushort[] { 0, 1 }));
            Assert.That(charts.Single(chart => chart.Difficulty == OjnDifficulty.NX).ParseResult.SampleDefinitions.Keys,
                Is.EquivalentTo(new ushort[] { 0, 1 }));
            Assert.That(charts.Single(chart => chart.Difficulty == OjnDifficulty.HX).ParseResult.SampleDefinitions.Keys,
                Is.EquivalentTo(new ushort[] { 0, 1 }));
        });
    }

    [Test]
    public void TestOneBasedM30MapsDownInFixedMapping()
    {
        File.WriteAllBytes(Path.Combine(directory, "test.ojm"), createM30([(1, "one!"u8.ToArray()), (2, "two!"u8.ToArray())]));
        var charts = OjnDecoder.DecodeAll(createOjn([[1, 2], [1, 2], [1, 2]]), Path.Combine(directory, "test.ojn"));

        Assert.Multiple(() =>
        {
            Assert.That(charts.Single(chart => chart.Difficulty == OjnDifficulty.EX).ParseResult.SampleDefinitions.Keys,
                Is.EquivalentTo(new ushort[] { 0, 1 }));
            Assert.That(charts.Single(chart => chart.Difficulty == OjnDifficulty.NX).ParseResult.SampleDefinitions.Keys,
                Is.EquivalentTo(new ushort[] { 0, 1 }));
            Assert.That(charts.Single(chart => chart.Difficulty == OjnDifficulty.HX).ParseResult.SampleDefinitions.Keys,
                Is.EquivalentTo(new ushort[] { 0, 1 }));
        });
    }

    [Test]
    public void TestMissingCompanionOjmFallsBackToZeroBased()
    {
        var charts = OjnDecoder.DecodeAll(createOjn([[1, 2], [1, 2], [1, 2]]), Path.Combine(directory, "test.ojn"));

        Assert.That(charts.Single(chart => chart.Difficulty == OjnDifficulty.EX).ParseResult.SampleDefinitions.Keys,
            Is.EquivalentTo(new ushort[] { 0, 1 }));
    }

    [Test]
    public void TestZeroBasedArchiveAppliesSameRuleAcrossDifficulties()
    {
        File.WriteAllBytes(Path.Combine(directory, "test.ojm"),
            createM30([(0, "zero"u8.ToArray()), (1, "one!"u8.ToArray()), (2, "two!"u8.ToArray()),
                       (6, "six!"u8.ToArray()), (7, "seven!"u8.ToArray()), (8, "eight!"u8.ToArray()),
                       (10, "ten!"u8.ToArray()), (11, "eleven!"u8.ToArray()), (12, "twelve!"u8.ToArray())]));
        var charts = OjnDecoder.DecodeAll(createOjn([[1, 2], [1, 6], [10, 11]]), Path.Combine(directory, "test.ojn"));

        Assert.Multiple(() =>
        {
            Assert.That(charts.Single(chart => chart.Difficulty == OjnDifficulty.EX).ParseResult.SampleDefinitions.Keys,
                Is.EquivalentTo(new ushort[] { 0, 1 }));
            Assert.That(charts.Single(chart => chart.Difficulty == OjnDifficulty.NX).ParseResult.SampleDefinitions.Keys,
                Is.EquivalentTo(new ushort[] { 0, 5 }));
            Assert.That(charts.Single(chart => chart.Difficulty == OjnDifficulty.HX).ParseResult.SampleDefinitions.Keys,
                Is.EquivalentTo(new ushort[] { 9, 10 }));
        });
    }

    [Test]
    public void TestGetSampleReadsSingleM30PayloadLazily()
    {
        var ojmPath = Path.Combine(directory, "test.ojm");
        File.WriteAllBytes(ojmPath, createM30([(1, "lazy-payload"u8.ToArray())]));

        Assert.That(OjmDecoder.GetSample(ojmPath, 1), Is.EqualTo("lazy-payload"u8.ToArray()));
    }

    [Test]
    public void TestOmcBgmReferencesRemainCanonical()
    {
        File.WriteAllBytes(Path.Combine(directory, "test.ojm"), createOmc(2));
        var charts = OjnDecoder.DecodeAll(
            createOjn([[1, 2], [1, 2], [1, 2]], [[4, 4], [4, 4], [4, 4]]),
            Path.Combine(directory, "test.ojn"));

        Assert.Multiple(() =>
        {
            Assert.That(charts.Single(chart => chart.Difficulty == OjnDifficulty.EX).ParseResult.SampleDefinitions.Keys,
                Is.EquivalentTo(new ushort[] { 1000, 1001 }));
            Assert.That(charts.Single(chart => chart.Difficulty == OjnDifficulty.NX).ParseResult.SampleDefinitions.Keys,
                Is.EquivalentTo(new ushort[] { 1000, 1001 }));
            Assert.That(charts.Single(chart => chart.Difficulty == OjnDifficulty.HX).ParseResult.SampleDefinitions.Keys,
                Is.EquivalentTo(new ushort[] { 1000, 1001 }));
        });
    }

    [Test]
    public void TestM30BgmZeroMapsDownInFixedMapping()
    {
        File.WriteAllBytes(Path.Combine(directory, "test.ojm"), createM30WithBgmZeroAndOneBasedKeys());
        var charts = OjnDecoder.DecodeAll(createOjn([[1, 2], [1, 2], [1, 2]]), Path.Combine(directory, "test.ojn"));

        Assert.Multiple(() =>
        {
            Assert.That(charts.Single(chart => chart.Difficulty == OjnDifficulty.EX).ParseResult.SampleDefinitions.Keys,
                Is.EquivalentTo(new ushort[] { 0, 1 }));
            Assert.That(charts.Single(chart => chart.Difficulty == OjnDifficulty.NX).ParseResult.SampleDefinitions.Keys,
                Is.EquivalentTo(new ushort[] { 0, 1 }));
            Assert.That(charts.Single(chart => chart.Difficulty == OjnDifficulty.HX).ParseResult.SampleDefinitions.Keys,
                Is.EquivalentTo(new ushort[] { 0, 1 }));
        });
    }

    [Test]
    public void TestM30WithoutKeyZeroCanStillUseCanonicalReferences()
    {
        File.WriteAllBytes(Path.Combine(directory, "test.ojm"),
            createM30([(1, "one!"u8.ToArray()), (2, "two!"u8.ToArray()), (3, "three!"u8.ToArray())]));
        var charts = OjnDecoder.DecodeAll(createOjn([[2, 4], [2, 4], [2, 4]]), Path.Combine(directory, "test.ojn"));

        Assert.Multiple(() =>
        {
            Assert.That(charts.Single(chart => chart.Difficulty == OjnDifficulty.EX).ParseResult.SampleDefinitions.Keys,
                Is.EquivalentTo(new ushort[] { 1, 3 }));
            Assert.That(charts.Single(chart => chart.Difficulty == OjnDifficulty.NX).ParseResult.SampleDefinitions.Keys,
                Is.EquivalentTo(new ushort[] { 1, 3 }));
            Assert.That(charts.Single(chart => chart.Difficulty == OjnDifficulty.HX).ParseResult.SampleDefinitions.Keys,
                Is.EquivalentTo(new ushort[] { 1, 3 }));
        });
    }

    private static byte[] createOjn(ushort[][] difficultyReferences, byte[][]? difficultyFlags = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(1234);
        writer.Write("ojn\0"u8.ToArray());
        writer.Write(2.5f);
        writer.Write(2);
        writer.Write(120f);
        write(writer, new short[] { 4, 7, 12, 0 });
        write(writer, difficultyReferences.Select(references => references.Length).ToArray());
        write(writer, difficultyReferences.Select(references => references.Length).ToArray());
        write(writer, new[] { 1, 1, 1 });
        write(writer, new[] { 2, 2, 2 });
        writer.Write((short)0);
        writer.Write((short)1234);
        writer.Write(new byte[20]);
        writer.Write(0);
        writer.Write(1);
        writeFixedString(writer, "Synthetic Song", 64);
        writeFixedString(writer, "Synthetic Artist", 32);
        writeFixedString(writer, "Synthetic Noter", 32);
        writeFixedString(writer, "test.ojm", 32);
        writer.Write(0);
        write(writer, new[] { 5, 5, 5 });

        var offsets = new int[3];
        var position = 300;
        for (var difficulty = 0; difficulty < 3; difficulty++)
        {
            offsets[difficulty] = position;
            position += 20 + difficultyReferences[difficulty].Length * 4;
        }

        write(writer, offsets);
        writer.Write(0);

        Assert.That(stream.Position, Is.EqualTo(300));

        for (var difficulty = 0; difficulty < 3; difficulty++)
        {
            writeEventHeader(writer, 0, 1, 1);
            writer.Write(180f);

            var references = difficultyReferences[difficulty];
            writeEventHeader(writer, 0, 2, (short)references.Length);
            for (var index = 0; index < references.Length; index++)
                writeSound(writer, references[index], difficultyFlags?[difficulty][index] ?? 0);
        }

        return stream.ToArray();
    }

    private static byte[] createM30(params (ushort SampleId, byte[] Payload)[] samples)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("M30\0"u8.ToArray());
        writer.Write(1);
        writer.Write(0);
        writer.Write(samples.Length);
        writer.Write(28);
        writer.Write(samples.Sum(sample => 52 + sample.Payload.Length));
        writer.Write(0);

        foreach (var (sampleId, payload) in samples)
        {
            writeFixedString(writer, "sample", 32);
            writer.Write(payload.Length);
            writer.Write((short)5);
            writer.Write((short)2);
            writer.Write(1);
            writer.Write(sampleId);
            writer.Write((short)0);
            writer.Write(0);
            writer.Write(payload);
        }

        return stream.ToArray();
    }

    private static byte[] createM30WithBgmZeroAndOneBasedKeys()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("M30\0"u8.ToArray());
        writer.Write(1);
        writer.Write(0);
        writer.Write(2);
        writer.Write(28);
        writer.Write(2 * (52 + 4));
        writer.Write(0);
        writeM30Sample(writer, 0, 0, "bgm!"u8.ToArray());
        writeM30Sample(writer, 1, 5, "key!"u8.ToArray());
        return stream.ToArray();
    }

    private static void writeM30Sample(BinaryWriter writer, ushort sampleId, short sampleType, byte[] payload)
    {
        writeFixedString(writer, "sample", 32);
        writer.Write(payload.Length);
        writer.Write(sampleType);
        writer.Write((short)2);
        writer.Write(1);
        writer.Write(sampleId);
        writer.Write((short)0);
        writer.Write(0);
        writer.Write(payload);
    }

    private static byte[] createOmc(int oggCount)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("OMC\0"u8.ToArray());
        writer.Write((short)0);
        writer.Write((short)oggCount);
        writer.Write(20);
        writer.Write(20);
        writer.Write(20 + oggCount * 40);

        for (var index = 0; index < oggCount; index++)
        {
            writeFixedString(writer, "sample", 32);
            writer.Write(4);
            writer.Write(new byte[] { (byte)index, 0, 0, 0 });
        }

        return stream.ToArray();
    }

    private static void writeEventHeader(BinaryWriter writer, int measure, short channel, short divisions)
    {
        writer.Write(measure);
        writer.Write(channel);
        writer.Write(divisions);
    }

    private static void writeSound(BinaryWriter writer, ushort reference, byte flag)
    {
        writer.Write(reference);
        writer.Write((byte)0);
        writer.Write(flag);
    }

    private static void write(BinaryWriter writer, short[] values)
    {
        foreach (var value in values)
            writer.Write(value);
    }

    private static void write(BinaryWriter writer, int[] values)
    {
        foreach (var value in values)
            writer.Write(value);
    }

    private static void writeFixedString(BinaryWriter writer, string value, int size)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > size)
            throw new ArgumentException("Fixed string exceeds its field size.");

        writer.Write(bytes);
        writer.Write(new byte[size - bytes.Length]);
    }
}
