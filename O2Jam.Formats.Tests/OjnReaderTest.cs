using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using O2Jam.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class OjnReaderTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void SelectedChartReadMatchesFullDecode(bool encrypted)
    {
        var bytes = OjnTestData.CreateChart();
        if (encrypted)
            bytes = encrypt(bytes);

        var full = new OjnReader().Read(new MemoryStream(bytes));
        var selected = new OjnReader().ReadChart(new MemoryStream(bytes), OjnDifficulty.EX);

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
        var bytes = OjnTestData.CreateChart();
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
            Assert.That(chart.BpmEvents.Single(), Is.EqualTo(new OjnBpmEvent(0.5, 240)));
            Assert.That(hold.Type, Is.EqualTo(OjnNoteType.Hold));
            Assert.That(hold.Position, Is.Zero);
            Assert.That(hold.EndPosition, Is.EqualTo(0.5));
            Assert.That(hold.SampleId, Is.Zero);
            Assert.That(hold.TailSampleId, Is.EqualTo(1));
        });
    }

    [Test]
    public void MeasureFractionsApplyRegardlessOfBlockOrder()
    {
        var ordered = OjnTestData.CreateChart();
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
        var bytes = OjnTestData.CreateChart();
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8, 4), encodingVersion);
        replaceTitle(bytes, Convert.FromHexString(titleHex));

        var document = new OjnReader().Read(new MemoryStream(bytes));

        Assert.That(document.Metadata.Title, Is.EqualTo(expected));
    }

    [Test]
    public void DecodesOriginalCp949KoreanMetadata()
    {
        var bytes = OjnTestData.CreateChart();
        byte[] title = [0xbf, 0xc0, 0xc5, 0xf5, 0xc0, 0xeb]; // 오투잼
        replaceTitle(bytes, title);

        var document = new OjnReader(OjnMetadataEncoding.Cp949).Read(new MemoryStream(bytes));

        Assert.That(document.Metadata.Title, Is.EqualTo("오투잼"));
    }

    [Test]
    public void AutomaticUsesHangulSignalForOriginalKoreanClient()
    {
        var bytes = OjnTestData.CreateChart();
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8, 4), 2.8f);
        // CP949 "징" is also syntactically-valid UTF-8 and decodes there as "¡".
        replaceTitle(bytes, [0xc2, 0xa1]);

        var document = new OjnReader().Read(new MemoryStream(bytes));

        Assert.That(document.Metadata.Title, Is.EqualTo("징"));
    }

    [Test]
    public void DecodesExplicitUtf8EastAsianMetadata()
    {
        var bytes = OjnTestData.CreateChart();
        replaceTitle(bytes, Encoding.UTF8.GetBytes("中文 테스트"));

        var document = new OjnReader(OjnMetadataEncoding.Utf8).Read(new MemoryStream(bytes));

        Assert.That(document.Metadata.Title, Is.EqualTo("中文 테스트"));
    }

    [Test]
    public void AutomaticDoesNotMistakeValidGbkForUtf8()
    {
        var bytes = OjnTestData.CreateChart();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        replaceTitle(bytes, Encoding.GetEncoding(936).GetBytes("猫猫Dance"));

        var document = new OjnReader().Read(new MemoryStream(bytes));

        Assert.That(document.Metadata.Title, Is.EqualTo("猫猫Dance"));
    }

    [Test]
    public void TrimsOnlyIncompleteCharacterAtFixedFieldBoundary()
    {
        var bytes = OjnTestData.CreateChart();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var title = Encoding.GetEncoding(936).GetBytes("[B16][152/190/171]活泼纯情小姑娘");
        replaceTitle(bytes, [.. title, 0xef]);

        var document = new OjnReader().Read(new MemoryStream(bytes));

        Assert.That(document.Metadata.Title, Is.EqualTo("[B16][152/190/171]活泼纯情小姑娘"));
    }

    [Test]
    public void Utf8BomIsRecognisedAutomatically()
    {
        var bytes = OjnTestData.CreateChart();
        replaceTitle(bytes, [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("中文 테스트")]);

        var document = new OjnReader().Read(new MemoryStream(bytes));

        Assert.That(document.Metadata.Title, Is.EqualTo("中文 테스트"));
    }

    [Test]
    public void InvalidOptionalImageOffsetsDoNotRejectPlayableCharts()
    {
        var bytes = OjnTestData.CreateChart();
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
        var bytes = OjnTestData.CreateChart();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(64, 4), 2_000_001);

        Assert.Throws<InvalidDataException>(() => new OjnReader().Read(new MemoryStream(bytes)));
    }

    [Test]
    public void TapAfterOpenHoldIsNotConsumedAsTail()
    {
        var bytes = OjnTestData.CreateChart();
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
        var bytes = OjnTestData.CreateChart();
        bytes[323] = 3;

        var notes = new OjnReader().Read(new MemoryStream(bytes)).Charts[0].Notes;

        Assert.That(notes, Is.Empty);
    }

    [Test]
    public void NonPlayableReleaseFlagStillProducesAutomaticAudioEvent()
    {
        var bytes = OjnTestData.CreateChart();
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(316, 2), 9);
        bytes[323] = 7;

        var document = new OjnReader().Read(new MemoryStream(bytes));
        var note = document.Charts[0].Notes.Single();

        Assert.Multiple(() =>
        {
            Assert.That(note.IsPlayable, Is.False);
            Assert.That(note.Type, Is.EqualTo(OjnNoteType.Tap));
            Assert.That(note.SampleKind, Is.EqualTo(OjnSampleKind.Background));
            Assert.That(note.SampleId, Is.EqualTo(1000));
        });
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

}
