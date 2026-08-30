using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Objects;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class OjnReaderTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void SelectedChartReadMatchesFullDecode(bool encrypted)
    {
        var bytes = CreateChart();
        if (encrypted)
            bytes = encrypt(bytes);

        var full = new OjnReader().Read(new MemoryStream(bytes));
        var selected = new OjnReader().ReadChart(new MemoryStream(bytes), O2JamDifficulty.EX);

        Assert.Multiple(() =>
        {
            Assert.That(selected.Metadata.Title, Is.EqualTo(full.Metadata.Title));
            Assert.That(selected.Charts, Has.Count.EqualTo(1));
            Assert.That(selected.Charts[0].Difficulty, Is.EqualTo(full.Charts[0].Difficulty));
            Assert.That(selected.Charts[0].BpmEvents, Is.EqualTo(full.Charts[0].BpmEvents));
            Assert.That(selected.Charts[0].Notes, Is.EqualTo(full.Charts[0].Notes));
            Assert.That(selected.Charts[0].MeasureFractions, Is.EqualTo(full.Charts[0].MeasureFractions));
            Assert.That(selected.Charts[0].MeasureCount, Is.EqualTo(full.Charts[0].MeasureCount));
            Assert.That(selected.Metadata.Cover, Is.Empty);
            Assert.That(selected.Metadata.Thumbnail, Is.Empty);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReadsTimingFractionsAndLongNotePair(bool encrypted)
    {
        var bytes = CreateChart();
        if (encrypted)
            bytes = encrypt(bytes);

        var document = new OjnReader().Read(new MemoryStream(bytes));
        var chart = document.Charts[0];
        var hold = chart.Notes.Single();

        Assert.Multiple(() =>
        {
            Assert.That(document.Metadata.Title, Is.EqualTo("Clean O2Jam"));
            Assert.That(chart.MeasureFractions.Single(), Is.EqualTo(new OjnMeasureFraction(1, 0.5)));
            Assert.That(chart.MeasureCount, Is.EqualTo(2));
            Assert.That(chart.BpmEvents.Single(), Is.EqualTo(new O2JamBpmEvent(0.5, 240)));
            Assert.That(hold.Type, Is.EqualTo(OjnNoteType.Hold));
            Assert.That(hold.Position, Is.Zero);
            Assert.That(hold.EndPosition, Is.EqualTo(0.5));
            Assert.That(hold.SampleId, Is.Zero);
            Assert.That(hold.TailSampleId, Is.EqualTo(1));
        });
    }

    [Test]
    public void FactoryProducesNativeManiaCompatibleHold()
    {
        var document = new OjnReader().Read(new MemoryStream(CreateChart()));
        var beatmap = new OjnBeatmapFactory().Create(document, O2JamDifficulty.EX);
        var hold = beatmap.HitObjects.Single() as O2JamHoldNote;
        var schedule = O2JamPreviewSchedule.Create(beatmap, true);
        Assert.That(hold!.GetNodeSamples(1), Is.Empty);
        var converted = (O2JamBeatmap)new O2JamBeatmapConverter(beatmap, new O2LazerRuleset()).Convert();
        var convertedHold = (O2JamHoldNote)converted.HitObjects.Single();
        convertedHold.ApplyDefaults(converted.ControlPointInfo, converted.Difficulty);

        Assert.Multiple(() =>
        {
            Assert.That(hold, Is.Not.Null);
            Assert.That(hold!.StartTime, Is.Zero);
            Assert.That(hold.EndTime, Is.EqualTo(1000).Within(0.001));
            Assert.That(hold.GetNodeSamples(0).OfType<O2JamHitSampleInfo>().Select(sample => sample.SampleId), Is.EqualTo(new[] { 0 }));
            Assert.That(hold.GetNodeSamples(1), Is.Empty);
            Assert.That(schedule.PreviewEvents.Select(evt => (evt.Time, evt.SampleId)), Is.EqualTo(new[] { (0d, 0) }));
            Assert.That(beatmap.TimingMap.EffectiveBpmAtPosition(0.5), Is.EqualTo(240));
            Assert.That(beatmap.Stages.Single().Columns, Is.EqualTo(7));
            Assert.That(beatmap.MeasureLineTimes, Is.EqualTo(new[] { 0d, 1000d, 2000d }).Within(0.001));
            Assert.That(converted.MeasureLineTimes, Is.EqualTo(beatmap.MeasureLineTimes));
            Assert.That(convertedHold.NestedHitObjects.Select(nested => nested.GetType()), Is.EquivalentTo(new[]
            {
                typeof(O2JamHoldHead),
                typeof(O2JamHoldBody),
                typeof(O2JamHoldTail),
            }));
            Assert.That(convertedHold.Head.Samples.OfType<O2JamHitSampleInfo>().Select(sample => sample.SampleId), Is.EqualTo(new[] { 0 }));
            Assert.That(convertedHold.Tail.Samples, Is.Empty);
            Assert.That(convertedHold.Body.Samples, Is.Empty);
        });
    }

    [Test]
    public void MeasureFractionsApplyRegardlessOfBlockOrder()
    {
        var ordered = CreateChart();
        var reordered = new byte[ordered.Length];
        ordered.AsSpan(0, 300).CopyTo(reordered);

        // Move the channel-0 fraction block after the notes it affects. Some OJN writers do not
        // preserve channel order, so positions must be normalised only after all blocks are known.
        ordered.AsSpan(312, 60).CopyTo(reordered.AsSpan(300));
        ordered.AsSpan(300, 12).CopyTo(reordered.AsSpan(360));

        var chart = new OjnReader().Read(new MemoryStream(reordered)).Charts[0];
        var hold = chart.Notes.Single();

        Assert.Multiple(() =>
        {
            Assert.That(chart.BpmEvents.Single().Position, Is.EqualTo(0.5));
            Assert.That(hold.EndPosition, Is.EqualTo(0.5));
        });
    }

    [TestCase("5BB9FAB7FE5DC9AFC4C8CBFE", "[国服]莎娜塔", 2f)]
    [TestCase("5BB9FAB7FE5DC9AFC4C8CBFE", "[国服]莎娜塔", 2.9f)]
    [TestCase("5BB9FAB7FE5DBAECC9ABBCA4C7E9", "[国服]红色激情", 2f)]
    [TestCase("5BB9FAB7FE5DCEC2DCB0D2BBBFCC", "[国服]温馨一刻", 2.1f)]
    [TestCase("5BB9FAB7FE5DB6FEB6C8B3E5BBF7", "[国服]二度冲击", 2f)]
    public void DecodesLegacyGbkMetadataWithoutProducingCp949Mojibake(string titleHex, string expected, float encodingVersion)
    {
        var bytes = CreateChart();
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8, 4), encodingVersion);
        replaceTitle(bytes, Convert.FromHexString(titleHex));

        var document = new OjnReader().Read(new MemoryStream(bytes));

        Assert.That(document.Metadata.Title, Is.EqualTo(expected));
    }

    [Test]
    public void DecodesOriginalCp949KoreanMetadata()
    {
        var bytes = CreateChart();
        byte[] title = [0xbf, 0xc0, 0xc5, 0xf5, 0xc0, 0xeb]; // 오투잼
        replaceTitle(bytes, title);

        var document = new OjnReader(OjnMetadataEncoding.Cp949).Read(new MemoryStream(bytes));

        Assert.That(document.Metadata.Title, Is.EqualTo("오투잼"));
    }

    [Test]
    public void AutomaticUsesHangulSignalForOriginalKoreanClient()
    {
        var bytes = CreateChart();
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8, 4), 2.8f);
        // CP949 "징" is also syntactically-valid UTF-8 and decodes there as "¡".
        replaceTitle(bytes, [0xc2, 0xa1]);

        var document = new OjnReader().Read(new MemoryStream(bytes));

        Assert.That(document.Metadata.Title, Is.EqualTo("징"));
    }

    [Test]
    public void DecodesExplicitUtf8EastAsianMetadata()
    {
        var bytes = CreateChart();
        replaceTitle(bytes, Encoding.UTF8.GetBytes("中文 테스트"));

        var document = new OjnReader(OjnMetadataEncoding.Utf8).Read(new MemoryStream(bytes));

        Assert.That(document.Metadata.Title, Is.EqualTo("中文 테스트"));
    }

    [Test]
    public void AutomaticDoesNotMistakeValidGbkForUtf8()
    {
        var bytes = CreateChart();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        replaceTitle(bytes, Encoding.GetEncoding(936).GetBytes("猫猫Dance"));

        var document = new OjnReader().Read(new MemoryStream(bytes));

        Assert.That(document.Metadata.Title, Is.EqualTo("猫猫Dance"));
    }

    [Test]
    public void TrimsOnlyIncompleteCharacterAtFixedFieldBoundary()
    {
        var bytes = CreateChart();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var title = Encoding.GetEncoding(936).GetBytes("[B16][152/190/171]活泼纯情小姑娘");
        replaceTitle(bytes, [.. title, 0xef]);

        var document = new OjnReader().Read(new MemoryStream(bytes));

        Assert.That(document.Metadata.Title, Is.EqualTo("[B16][152/190/171]活泼纯情小姑娘"));
    }

    [Test]
    public void Utf8BomIsRecognisedAutomatically()
    {
        var bytes = CreateChart();
        replaceTitle(bytes, [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("中文 테스트")]);

        var document = new OjnReader().Read(new MemoryStream(bytes));

        Assert.That(document.Metadata.Title, Is.EqualTo("中文 테스트"));
    }

    [Test]
    public void InvalidOptionalImageOffsetsDoNotRejectPlayableCharts()
    {
        var bytes = CreateChart();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(100, 4), 12_856);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(296, 4), 130_000);

        var document = new OjnReader().Read(new MemoryStream(bytes));

        Assert.Multiple(() =>
        {
            Assert.That(document.Charts[0].Notes, Has.Length.EqualTo(1));
            Assert.That(document.Metadata.Cover, Is.Empty);
            Assert.That(document.Metadata.Thumbnail, Is.Empty);
        });
    }

    [Test]
    public void RejectsUnboundedBlockCountsBeforeParsing()
    {
        var bytes = CreateChart();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(64, 4), 2_000_001);

        Assert.Throws<InvalidDataException>(() => new OjnReader().Read(new MemoryStream(bytes)));
    }

    [Test]
    public void TapAfterOpenHoldIsNotConsumedAsTail()
    {
        var bytes = CreateChart();
        bytes[359] = 0;

        var notes = new OjnReader().Read(new MemoryStream(bytes)).Charts[0].Notes;

        Assert.Multiple(() =>
        {
            Assert.That(notes, Has.Length.EqualTo(2));
            Assert.That(notes, Has.All.Matches<OjnNoteEvent>(note => note.Type == OjnNoteType.Tap));
        });
    }

    [Test]
    public void OrphanPlayableReleaseIsNotConvertedToTap()
    {
        var bytes = CreateChart();
        bytes[323] = 3;

        var notes = new OjnReader().Read(new MemoryStream(bytes)).Charts[0].Notes;

        Assert.That(notes, Is.Empty);
    }

    [Test]
    public void NonPlayableReleaseFlagStillProducesAutomaticAudioEvent()
    {
        var bytes = CreateChart();
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(316, 2), 9);
        bytes[323] = 7;

        var document = new OjnReader().Read(new MemoryStream(bytes));
        var note = document.Charts[0].Notes.Single();
        var automaticAudio = new OjnBeatmapFactory().Create(document, O2JamDifficulty.EX).AutomaticAudioEvents.Single();

        Assert.Multiple(() =>
        {
            Assert.That(note.IsPlayable, Is.False);
            Assert.That(note.Type, Is.EqualTo(OjnNoteType.Tap));
            Assert.That(note.SampleKind, Is.EqualTo(OjnSampleKind.Background));
            Assert.That(note.SampleId, Is.EqualTo(1000));
            Assert.That(automaticAudio.Kind, Is.EqualTo(O2JamAudioEventKind.Background));
        });
    }

    internal static byte[] CreateChart()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write((uint)100);
        writer.Write(new byte[] { (byte)'o', (byte)'j', (byte)'n', 0 });
        writer.Write(3f);
        writer.Write(0);
        writer.Write(120f);
        writer.Write((ushort)5);
        writer.Write((ushort)10);
        writer.Write((ushort)15);
        writer.Write((short)0);
        writeThree(writer, 0);
        writeThree(writer, 0);
        writeThree(writer, 0);
        writer.Write((uint)4);
        writer.Write((uint)0);
        writer.Write((uint)0);
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write(new byte[20]);
        writer.Write((uint)0);
        writer.Write((uint)1);
        writeFixed(writer, "Clean O2Jam", 64);
        writeFixed(writer, "Open", 32);
        writeFixed(writer, "Tester", 32);
        writeFixed(writer, "o2ma100.ojm", 32);
        writer.Write((uint)0);
        writeThree(writer, 0);
        writer.Write((uint)300);
        writer.Write((uint)372);
        writer.Write((uint)372);
        writer.Write((uint)372);

        Assert.That(stream.Position, Is.EqualTo(300));

        writeFloatBlock(writer, 0, 0, 0.5f);
        writeNoteBlock(writer, 0, 2, 4, 0, 1, 0x88, 2);
        writeFloatBlock(writer, 1, 1, 240);
        writeNoteBlock(writer, 1, 2, 4, 0, 2, 0x88, 3);

        Assert.That(stream.Position, Is.EqualTo(372));
        return stream.ToArray();
    }

    private static byte[] encrypt(byte[] plain)
    {
        const byte blockSize = 7;
        const byte main = 0x33;
        const byte middle = 0x55;
        const byte initial = 0x77;
        byte[] key = Enumerable.Repeat(main, blockSize).ToArray();
        key[0] = initial;
        key[blockSize / 2] = middle;

        var encryptedPayload = new byte[plain.Length];
        for (var index = 0; index < plain.Length; index++)
            encryptedPayload[plain.Length - 1 - index] = (byte)(plain[index] ^ key[index % blockSize]);

        return [(byte)'n', (byte)'e', (byte)'w', blockSize, main, middle, initial, 0, .. encryptedPayload];
    }

    private static void replaceTitle(byte[] chart, byte[] title)
    {
        if (title.Length > 64)
            throw new ArgumentOutOfRangeException(nameof(title));

        Array.Clear(chart, 108, 64);
        title.CopyTo(chart, 108);
    }

    private static void writeFloatBlock(BinaryWriter writer, uint measure, ushort channel, float value)
    {
        writer.Write(measure);
        writer.Write(channel);
        writer.Write((ushort)1);
        writer.Write(value);
    }

    private static void writeNoteBlock(BinaryWriter writer, uint measure, ushort channel, ushort count, int populatedIndex, ushort id, byte audio, byte type)
    {
        writer.Write(measure);
        writer.Write(channel);
        writer.Write(count);

        for (var index = 0; index < count; index++)
        {
            writer.Write(index == populatedIndex ? id : (ushort)0);
            writer.Write(index == populatedIndex ? audio : (byte)0);
            writer.Write(index == populatedIndex ? type : (byte)0);
        }
    }

    private static void writeFixed(BinaryWriter writer, string value, int length)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes);
        writer.Write(new byte[length - bytes.Length]);
    }

    private static void writeThree(BinaryWriter writer, uint value)
    {
        writer.Write(value);
        writer.Write(value);
        writer.Write(value);
    }
}
