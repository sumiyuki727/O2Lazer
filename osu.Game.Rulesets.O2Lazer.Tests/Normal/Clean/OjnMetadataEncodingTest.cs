using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class OjnMetadataEncodingTest
{
    [TestCase("F2D82DF4B6D2B4D2B3E8DD", "眞-千年女王")]
    [TestCase("BFE4B1ABC0C720BBEA204D7973746572696F7573204D6F756E7461696E", "요괴의 산 Mysterious Mountain")]
    [TestCase("414E54492054484520A1C420484F4C494320284578207665722E29", "ANTI THE ∞ HOLIC (Ex ver.)")]
    [TestCase("DEEFE3EA", "蛇神")]
    [TestCase("566F6C6361EBBF", "Volca肉")]
    [TestCase("4841454C45A5B2207E416E67656C20576F726C647E", "HAELEⅢ ~Angel World~")]
    [TestCase("556E69742EA5E1", "Unit.α")]
    [TestCase("2DA1E02D", "-□-")]
    [TestCase("41A5D8", "AΩ")]
    [TestCase("F1D3F6A2DEABDFD6", "竹取飛翔")]
    [TestCase("EAC5AADEAAC73FAAB1A1A2DCF4DEDDAACEE6D5", "月まで?け、不死の煙")]
    [TestCase("43656C65737469616C205374696E676572207E20ACE1ACD6ACE2ACD6ACD5ACD6ACDDACDCACD1", "Celestial Stinger ~ переделка")]
    public void KoreanPackContextResolvesAmbiguousVersion29Titles(string hex, string expected)
    {
        var document = contextualReader(OjnMetadataEncoding.Cp949).Read(chart(Convert.FromHexString(hex)));
        Assert.That(document.Metadata.Title, Is.EqualTo(expected));
    }

    [TestCase(OjnMetadataEncoding.Automatic)]
    [TestCase(OjnMetadataEncoding.Gbk)]
    public void JapaneseFieldEvidenceOutranksVersionAndDirectory(OjnMetadataEncoding context)
    {
        var document = contextualReader(context).Read(chart(Convert.FromHexString("AAA2AACAAABF")));
        Assert.That(document.Metadata.Title, Is.EqualTo("あなた"));
    }

    [Test]
    public void OtherHeaderFieldsResolveAmbiguousTitle()
    {
        var bytes = chart(Convert.FromHexString("DEEFE3EA"));
        setField(bytes, 172, 32, encoded("暴走", 949));
        Assert.That(new OjnReader().Read(bytes).Metadata.Title, Is.EqualTo("蛇神"));
    }

    [TestCase("[国服]莎娜塔")]
    [TestCase("[国服]红色激情")]
    [TestCase("[国服]温馨一刻")]
    [TestCase("[国服]二度冲击")]
    [TestCase("[台服]燕尾蝶")]
    [TestCase("[荣誉]F -from beatmaniaIIDX-")]
    [TestCase("[韩]彩虹碎片")]
    [TestCase("[超]Sayonara Planet Wars")]
    public void ChineseCatalogueLabelsSurviveKoreanContext(string title)
    {
        var document = contextualReader(OjnMetadataEncoding.Cp949).Read(chart(encoded(title, 936)));
        Assert.That(document.Metadata.Title, Is.EqualTo(title));
    }

    [Test]
    public void MixedFileDecodesChineseCharterIndependently()
    {
        var bytes = chart(encoded("Aquaris", 949));
        setField(bytes, 204, 32, Convert.FromHexString("B7E2D3A1A5DFB9EDBEABEC60"));
        var document = contextualReader(OjnMetadataEncoding.Cp949).Read(bytes);
        Assert.That(document.Metadata.NoteArranger, Is.EqualTo("封印ミ鬼精靈"));
    }

    [Test]
    public void TranslatedTitleDoesNotInheritOriginalJapaneseArtistsEncoding()
    {
        var bytes = chart(encoded("[112/140/126]丛云", 936));
        setField(bytes, 172, 32, encoded("あなた", 949));
        var metadata = contextualReader(OjnMetadataEncoding.Gbk).Read(bytes).Metadata;
        Assert.Multiple(() =>
        {
            Assert.That(metadata.Title, Is.EqualTo("[112/140/126]丛云"));
            Assert.That(metadata.Artist, Is.EqualTo("あなた"));
        });
    }

    [Test]
    public void ConflictingHeaderFieldsDoNotForceAnotherFieldsCodePage()
    {
        var bytes = chart(encoded("[国服]莎娜塔", 936));
        setField(bytes, 172, 32, encoded("あなた", 949));
        var document = contextualReader(OjnMetadataEncoding.Cp949).Read(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(document.Metadata.Title, Is.EqualTo("[国服]莎娜塔"));
            Assert.That(document.Metadata.Artist, Is.EqualTo("あなた"));
            Assert.That(OjnReader.InspectHeaderEncoding(bytes), Is.EqualTo(OjnMetadataEncoding.Automatic));
        });
    }

    [Test]
    public void AsciiExplicitEncodingAndBomDoNotRequestDirectoryIo()
    {
        OjnMetadataEncoding unexpectedFallback() => throw new AssertionException("Unambiguous metadata must not scan a directory.");
        var ascii = new OjnReader(OjnMetadataEncoding.Automatic, unexpectedFallback).Read(chart("ASCII"u8.ToArray()));
        var explicitKorean = new OjnReader(OjnMetadataEncoding.Cp949, unexpectedFallback).Read(chart(encoded("蛇神", 949)));
        var utf8 = new OjnReader(OjnMetadataEncoding.Automatic, unexpectedFallback).Read(chart([0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("中文 테스트")]));
        Assert.Multiple(() =>
        {
            Assert.That(ascii.Metadata.Title, Is.EqualTo("ASCII"));
            Assert.That(explicitKorean.Metadata.Title, Is.EqualTo("蛇神"));
            Assert.That(utf8.Metadata.Title, Is.EqualTo("中文 테스트"));
        });
    }

    [TestCase(4, 0, OjnMetadataEncoding.Cp949)]
    [TestCase(0, 4, OjnMetadataEncoding.Gbk)]
    [TestCase(3, 0, OjnMetadataEncoding.Automatic)]
    [TestCase(4, 2, OjnMetadataEncoding.Automatic)]
    [TestCase(9, 1, OjnMetadataEncoding.Cp949)]
    public void DirectoryRequiresEnoughConsistentEvidence(int korean, int chinese, OjnMetadataEncoding expected)
    {
        using var directory = new TemporaryCatalogue();
        for (var index = 0; index < korean; index++)
            directory.Write($"k{index}.OJN", chart(encoded("あなた", 949)));
        for (var index = 0; index < chinese; index++)
            directory.Write($"c{index}.ojn", chart(encoded("[国服]莎娜塔", 936)));
        var path = directory.Write("ambiguous.ojn", chart(encoded("蛇神", 949)));
        directory.Write("truncated.ojn", [1, 2, 3]);
        directory.Write("ignored.ojm", chart(encoded("[国服]莎娜塔", 936)));
        Assert.That(new OjnDirectoryEncoding().GetForFile(path), Is.EqualTo(expected));
    }

    [Test]
    public void ImportAndGameplayUseSameContextWithoutDependingOnFolderName()
    {
        using var directory = new TemporaryCatalogue();
        for (var index = 0; index < 4; index++)
            directory.Write($"context{index}.ojn", chart(encoded("あなた", 949)));
        var path = directory.Write("ambiguous.ojn", chart(encoded("蛇神", 949)));
        var plan = new O2JamImportPlanner().Create(path);
        var document = new OjnDocumentCache().Get(path, O2JamDifficulty.EX);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Title, Is.EqualTo("蛇神"));
            Assert.That(document.Metadata.Title, Is.EqualTo(plan.Title));
        });
    }

    [Test]
    public void RefreshInvalidatesContextAfterInPlaceHeaderEdits()
    {
        using var directory = new TemporaryCatalogue();
        for (var index = 0; index < 4; index++)
            directory.Write($"context{index}.ojn", chart(encoded("あなた", 949)));
        var path = directory.Write("ambiguous.ojn", chart(encoded("蛇神", 949)));
        var resolver = new OjnDirectoryEncoding();
        Assert.That(resolver.GetForFile(path), Is.EqualTo(OjnMetadataEncoding.Cp949));

        for (var index = 0; index < 4; index++)
            directory.Write($"context{index}.ojn", chart(encoded("[国服]莎娜塔", 936)));
        resolver.Clear();
        Assert.That(resolver.GetForFile(path), Is.EqualTo(OjnMetadataEncoding.Gbk));
    }

    [Test]
    [Explicit("Reads only OJN headers in the configured catalogue; does not benchmark or decode notes/audio.")]
    [Category("LocalDiagnostics")]
    public void AuditConfiguredCatalogueHeaders()
    {
        var root = Environment.GetEnvironmentVariable("O2JAM_CORPUS_PATH");
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            Assert.Ignore("Set O2JAM_CORPUS_PATH to inspect metadata encoding.");

        string[] newPacks = ["DSong", "ESong", "HSong", "NSong"];
        var legacyTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine("SongA", "o2ma112.ojn")] = "[国服]莎娜塔",
            [Path.Combine("SongA", "o2ma121.ojn")] = "[国服]红色激情",
            [Path.Combine("SongA", "o2ma127.ojn")] = "[国服]温馨一刻",
            [Path.Combine("SongB", "o2ma270.ojn")] = "[国服]二度冲击",
            [Path.Combine("SongA", "o2ma2982.ojn")] = "[台服]燕尾蝶",
            [Path.Combine("SongD", "o2ma1552.ojn")] = "[112/140/126]丛云",
            [Path.Combine("SongD", "o2ma280.ojn")] = "[韩]彩虹碎片",
            [Path.Combine("SongC", "o2ma1195.ojn")] = "[110/137/123]游戏王5DS",
            [Path.Combine("SongG", "o2ma1566.ojn")] = "[SP][176/194/212]天文学",
            [Path.Combine("SongL", "o2ma1254.ojn")] = "[超]Sayonara Planet Wars",
            [Path.Combine("SongZ", "o2ma2498.ojn")] = "[B16][152/190/171]活泼纯情小姑娘",
            [Path.Combine("SongM", "o2ma2570.ojn")] = "JUPITΨR GЯAVITY",
            [Path.Combine("SongM", "o2ma3351.ojn")] = "SuddeИDeath",
        };
        var resolver = new OjnDirectoryEncoding();
        var count = 0;
        var nonAscii = 0;
        var failures = new List<string>();
        var audit = new List<string> { "Path,Context,Title,CP949,GBK,Artist,Charter" };
        foreach (var path in Directory.EnumerateFiles(root!, "*.ojn", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            using var stream = File.OpenRead(path);
            var header = new byte[300];
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length
                || !header.AsSpan(4, 3).SequenceEqual("ojn"u8))
                continue;

            // The audit exercises the real metadata reader without parsing any note/image payload.
            Array.Clear(header, 64, 12);
            var reader = new OjnReader(OjnMetadataEncoding.Automatic, () => resolver.GetForFile(path));
            var metadata = reader.Read(header).Metadata;
            var rawTitle = header.AsSpan(108, 64).ToArray().TakeWhile(value => value != 0).ToArray();
            var cp949 = decodeTitle(rawTitle, 949);
            var gbk = decodeTitle(rawTitle, 936);
            audit.Add(string.Join(',', new[] { path, resolver.GetForFile(path).ToString(), metadata.Title, cp949, gbk, metadata.Artist, metadata.NoteArranger }.Select(csv)));
            if (legacyTitles.TryGetValue(Path.GetRelativePath(root!, path), out var expected) && metadata.Title != expected)
                failures.Add($"{path}: {metadata.Title} != {expected}");
            if (!newPacks.Contains(Path.GetFileName(Path.GetDirectoryName(path)), StringComparer.OrdinalIgnoreCase))
                continue;

            count++;
            if (rawTitle.Any(value => value >= 0x80))
                nonAscii++;
            if (metadata.Title != cp949)
                failures.Add($"{path}: {metadata.Title} != CP949 {cp949}");
            var rawArtist = header.AsSpan(172, 32).ToArray().TakeWhile(value => value != 0).ToArray();
            var rawCharter = header.AsSpan(204, 32).ToArray().TakeWhile(value => value != 0).ToArray();
            var expectedCharter = Path.GetRelativePath(root!, path) == Path.Combine("ESong", "o2ma117.ojn")
                ? "封印ミ鬼精靈"
                : decodeTitle(rawCharter, 949);
            if (metadata.Artist != decodeTitle(rawArtist, 949) || metadata.NoteArranger != expectedCharter)
                failures.Add($"{path}: unexpected artist/charter encoding ({metadata.Artist} / {metadata.NoteArranger})");
        }

        var auditPath = Environment.GetEnvironmentVariable("O2JAM_ENCODING_AUDIT_PATH");
        if (!string.IsNullOrWhiteSpace(auditPath))
            File.WriteAllLines(auditPath, audit, new UTF8Encoding(true));
        TestContext.Progress.WriteLine($"Inspected {audit.Count - 1} OJN headers; new packs: {count} titles ({nonAscii} non-ASCII). Mismatches: {failures.Count}.");
        Assert.That(count, Is.GreaterThan(0));
        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures.Take(100)));
    }

    private static OjnReader contextualReader(OjnMetadataEncoding context) => new(OjnMetadataEncoding.Automatic, () => context);

    private static byte[] encoded(string value, int codePage)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(codePage).GetBytes(value);
    }

    private static string decodeTitle(byte[] bytes, int codePage) => Encoding.GetEncoding(codePage).GetString(bytes).Trim();

    private static byte[] chart(byte[] title)
    {
        var bytes = OjnReaderTest.CreateChart();
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8, 4), 2.9f);
        setField(bytes, 108, 64, title);
        return bytes;
    }

    private static void setField(byte[] bytes, int offset, int length, byte[] value)
    {
        Assert.That(value.Length, Is.LessThanOrEqualTo(length));
        Array.Clear(bytes, offset, length);
        value.CopyTo(bytes, offset);
    }

    private static string csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private sealed class TemporaryCatalogue : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), $"o2lazer-encoding-{Guid.NewGuid():N}");

        public TemporaryCatalogue() => Directory.CreateDirectory(directory);

        public string Write(string name, byte[] data)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllBytes(path, data);
            return path;
        }

        public void Dispose()
        {
            foreach (var path in Directory.EnumerateFiles(directory))
                File.Delete(path);
            Directory.Delete(directory);
        }
    }
}
