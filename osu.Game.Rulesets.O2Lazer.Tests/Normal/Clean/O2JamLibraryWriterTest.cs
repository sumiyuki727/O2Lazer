using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamLibraryWriterTest
{
    [Test]
    public void MatchesExistingSetByCanonicalExternalSourcePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"o2lazer-source-{Guid.NewGuid():N}");
        var set = createSet(directory, "chart.ojn");

        Assert.Multiple(() =>
        {
            Assert.That(O2JamLibraryWriter.containsSourceChart(set, Path.Combine(directory, ".", "chart.ojn")), Is.True);
            Assert.That(O2JamLibraryWriter.containsSourceChart(set, Path.Combine(directory, "other.ojn")), Is.False);
        });
    }

    [Test]
    public void DoesNotReplaceEntriesOwnedByAnotherRuleset()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"o2lazer-source-{Guid.NewGuid():N}");
        var set = createSet(directory, "chart.ojn", "mania");

        Assert.Multiple(() =>
        {
            Assert.That(O2JamLibraryWriter.containsSourceChart(set, Path.Combine(directory, "chart.ojn")), Is.False);
            Assert.That(O2JamLibraryWriter.isOwnedByO2Lazer(set), Is.False);
        });
    }

    [TestCase("bms")]
    [TestCase("o2jam")]
    public void DoesNotOwnSameHashSetFromAnotherKeySoundRuleset(string shortName)
    {
        var set = createSet("source", "chart.ojn", shortName);
        set.Hash = "same-set-hash";

        Assert.That(O2JamLibraryWriter.isOwnedByO2Lazer(set), Is.False);
    }

    [Test]
    public void RefreshesMetadataWithoutReplacingExistingBeatmaps()
    {
        var set = createSet("source", "chart.ojn");
        var beatmap = set.Beatmaps.Single();
        var originalId = Guid.NewGuid();
        beatmap.ID = originalId;
        beatmap.Metadata.Title = "误码";
        beatmap.Metadata.Artist = "Old artist";
        beatmap.Metadata.Author.Username = "Old charter";
        var plan = createPlan("正确标题", "Artist", "Charter");

        var changed = O2JamLibraryWriter.refreshMetadata(set, plan);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(set.Beatmaps.Single(), Is.SameAs(beatmap));
            Assert.That(beatmap.Metadata.Title, Is.EqualTo("正确标题"));
            Assert.That(beatmap.Metadata.Artist, Is.EqualTo("Artist"));
            Assert.That(beatmap.Metadata.Author.Username, Is.EqualTo("Charter"));
            Assert.That(beatmap.Metadata.AudioFile, Is.EqualTo(plan.FileName));
            Assert.That(beatmap.Metadata.Tags, Does.Contain(O2JamLibraryWriter.MetadataMarker));
            Assert.That(beatmap.StarRating, Is.EqualTo(3.25));
            Assert.That(O2JamStarRatingMetadata.ReadO2Jam(beatmap.Metadata.Tags), Is.EqualTo(4.1).Within(0.000001));
            Assert.That(O2JamStarRatingMetadata.HasCurrentManiaVersion(beatmap.Metadata.Tags), Is.True);
            Assert.That(beatmap.Hash, Is.EqualTo(O2JamBeatmapIdentity.FromSource(plan.SourceHash, O2JamDifficulty.EX)));
            Assert.That(set.Hash, Is.EqualTo(plan.SetHash));
            Assert.That(beatmap.ID, Is.EqualTo(originalId));
        });
    }

    [Test]
    public void UnchangedMetadataDoesNotReportUpdate()
    {
        var set = createSet("source", "chart.ojn");
        var plan = createPlan("Title", "Artist", "Charter");
        var beatmap = set.Beatmaps.Single();
        beatmap.Metadata.Title = plan.Title;
        beatmap.Metadata.Artist = plan.Artist;
        beatmap.Metadata.Author.Username = plan.Author;
        beatmap.Metadata.AudioFile = plan.FileName;
        beatmap.Metadata.Tags = $"{O2JamLibraryWriter.MetadataMarker} {O2JamLibraryWriter.EncodingMarker} o2lazer-source-size:{plan.SourceData.LongLength} {O2JamStarRatingMetadata.CreateO2JamTag(plan.Charts.Single().Level)} {O2JamStarRatingMetadata.ManiaVersionTag}";
        beatmap.LastLocalUpdate = O2JamLibraryWriter.getSourceTimestamp(plan.SourcePath);
        beatmap.StarRating = plan.Charts.Single().ManiaStarRating;
        beatmap.Hash = O2JamBeatmapIdentity.FromSource(plan.SourceHash, plan.Charts.Single().Difficulty);
        set.Hash = plan.SetHash;

        Assert.That(O2JamLibraryWriter.refreshMetadata(set, plan), Is.False);
    }

    [Test]
    public void DifficultiesStoreIndependentManiaAndO2JamRatings()
    {
        var set = createSet("source", "chart.ojn");
        var ex = set.Beatmaps.Single();
        var nx = new BeatmapInfo(ex.Ruleset) { DifficultyName = "NX Lv.119", BeatmapSet = set };
        set.Beatmaps.Add(nx);
        var plan = createPlan("Title", "Artist", "Charter");
        plan = plan with { Charts = [plan.Charts[0], new O2JamImportChart(O2JamDifficulty.NX, 119, "nx-md5", 1000, 20, 5, 6.123456789012345)] };

        O2JamLibraryWriter.refreshMetadata(set, plan);
        Assert.Multiple(() =>
        {
            Assert.That(ex.StarRating, Is.EqualTo(3.25));
            Assert.That(nx.StarRating, Is.EqualTo(6.123456789012345));
            Assert.That(O2JamStarRatingMetadata.ReadO2Jam(ex.Metadata.Tags), Is.EqualTo(4.1).Within(0.000001));
            Assert.That(O2JamStarRatingMetadata.ReadO2Jam(nx.Metadata.Tags), Is.EqualTo(11.9).Within(0.000001));
            Assert.That(ex.Metadata, Is.Not.SameAs(nx.Metadata));
        });
    }

    [Test]
    public void DifficultyHashesAreStableAndIndependent()
    {
        const string sourceHash = "abcdef";
        var ex = O2JamBeatmapIdentity.FromSource(sourceHash, O2JamDifficulty.EX);
        var nx = O2JamBeatmapIdentity.FromSource(sourceHash, O2JamDifficulty.NX);
        var hx = O2JamBeatmapIdentity.FromSource(sourceHash, O2JamDifficulty.HX);

        Assert.Multiple(() =>
        {
            Assert.That(ex, Has.Length.EqualTo(64));
            Assert.That(new[] { ex, nx, hx }.Distinct().ToArray(), Has.Length.EqualTo(3));
            Assert.That(O2JamBeatmapIdentity.FromSource(sourceHash.ToUpperInvariant(), O2JamDifficulty.EX), Is.EqualTo(ex));
        });
    }

    [Test]
    public void RefreshReplacesOldManiaCacheWithoutTouchingUserTags()
    {
        var set = createSet("source", "chart.ojn");
        var beatmap = set.Beatmaps.Single();
        beatmap.Metadata.Tags = "o2ma100 keep-this-tag o2lazer-mania-version:1:0 o2lazer-o2jam-stars:0:2";
        var plan = createPlan("Title", "Artist", "Charter");

        Assert.That(O2JamLibraryWriter.refreshMetadata(set, plan), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(O2JamStarRatingMetadata.ReadMania(beatmap), Is.EqualTo(3.25));
            Assert.That(beatmap.Metadata.Tags.Split(' ').Count(tag => tag.StartsWith(O2JamStarRatingMetadata.ManiaVersionPrefix)), Is.EqualTo(1));
            Assert.That(beatmap.Metadata.Tags.Split(' ').Count(tag => tag.StartsWith(O2JamStarRatingMetadata.O2JamTagPrefix)), Is.EqualTo(1));
            Assert.That(beatmap.Metadata.Tags, Does.Contain("keep-this-tag"));
            Assert.That(beatmap.Metadata.Tags, Does.Contain("o2ma100"));
            Assert.That(O2JamLibraryWriter.refreshMetadata(set, plan), Is.False);
        });
    }

    [Test]
    public void MatchesUnchangedLegacySourceWhenSetHashPolicyDiffers()
    {
        var plan = createPlan("Title", "Artist", "Charter");
        var set = createSet(plan.SourceDirectory, plan.FileName);
        set.Hash = "legacy-set-hash";
        set.Files.Single().File.Hash = plan.SourceHash;
        set.Beatmaps.Single().Hash = plan.SourceHash;

        Assert.That(O2JamLibraryWriter.containsSourceContent(set, plan), Is.True);
    }

    [Test]
    public void FastRefreshFingerprintUsesMarkerTimestampAndLength()
    {
        var path = Path.GetTempFileName();

        try
        {
            File.WriteAllBytes(path, [1, 2, 3]);
            var matching = new O2JamImportedSource(
                Guid.NewGuid(),
                O2JamLibraryWriter.getSourceTimestamp(path),
                3,
                true,
                true);

            Assert.Multiple(() =>
            {
                Assert.That(O2JamImportService.isUnchanged(path, matching), Is.True);
                Assert.That(O2JamImportService.isUnchanged(path, matching with { HasCurrentMetadata = false }), Is.False);
                Assert.That(O2JamImportService.isUnchanged(path, matching with { SourceLength = 2 }), Is.False);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestCase(2f)]
    [TestCase(2.8f)]
    [TestCase(2.9f)]
    [TestCase(3f)]
    public void LegacyNonAsciiEncodingIsMigratedOnlyOnce(float version)
    {
        var path = Path.GetTempFileName();

        try
        {
            var bytes = OjnReaderTest.CreateChart();
            BitConverter.GetBytes(version).CopyTo(bytes, 8);
            Convert.FromHexString("DEEFE3EA00").CopyTo(bytes, 108);
            File.WriteAllBytes(path, bytes);
            var source = new O2JamImportedSource(
                Guid.NewGuid(),
                O2JamLibraryWriter.getSourceTimestamp(path),
                bytes.LongLength,
                true,
                false);

            Assert.Multiple(() =>
            {
                Assert.That(O2JamImportService.isUnchanged(path, source), Is.False);
                Assert.That(O2JamImportService.isUnchanged(path, source with { HasCurrentEncoding = true }), Is.True);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void EncodingMigrationReplacesOldMarkerWithoutReplacingBeatmap()
    {
        var set = createSet("source", "chart.ojn");
        var original = set.Beatmaps.Single();
        var id = original.ID;
        original.Metadata.Tags = "o2jam keep-this-tag o2lazer-encoding:1";
        O2JamLibraryWriter.refreshMetadata(set, createPlan("蛇神", "Artist", "Charter"));
        Assert.Multiple(() =>
        {
            Assert.That(set.Beatmaps.Single(), Is.SameAs(original));
            Assert.That(original.ID, Is.EqualTo(id));
            Assert.That(original.Metadata.Title, Is.EqualTo("蛇神"));
            Assert.That(original.Metadata.Tags, Does.Contain(O2JamLibraryWriter.EncodingMarker));
            Assert.That(original.Metadata.Tags, Does.Not.Contain("o2lazer-encoding:1"));
            Assert.That(original.Metadata.Tags, Does.Contain("keep-this-tag"));
        });
    }

    [Test]
    public void EncodingMigrationDoesNotReparseUnchangedAsciiMetadata()
    {
        var path = Path.GetTempFileName();
        try
        {
            var bytes = OjnReaderTest.CreateChart();
            File.WriteAllBytes(path, bytes);
            var source = new O2JamImportedSource(Guid.NewGuid(), O2JamLibraryWriter.getSourceTimestamp(path), bytes.LongLength, true, false);
            Assert.That(O2JamImportService.isUnchanged(path, source), Is.True);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static BeatmapSetInfo createSet(string source, string fileName, string shortName = O2LazerIdentity.ShortName)
    {
        const string hash = "source-hash";
        var set = new BeatmapSetInfo();
        set.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = hash }, fileName));

        var beatmap = new BeatmapInfo(new RulesetInfo { ShortName = shortName }, metadata: new BeatmapMetadata { Source = source })
        {
            Hash = hash,
            DifficultyName = "EX Lv.41",
            BeatmapSet = set,
        };

        set.Beatmaps.Add(beatmap);
        return set;
    }

    private static O2JamImportPlan createPlan(string title, string artist, string author) => new(
        "source\\chart.ojn",
        "source",
        "chart.ojn",
        [],
        Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant(),
        "set-hash",
        1,
        title,
        artist,
        author,
        120,
        [],
        [new O2JamImportChart(O2JamDifficulty.EX, 41, "md5", 1000, 1, 0, 3.25)]);
}
