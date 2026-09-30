using NUnit.Framework;
using O2Jam.Formats.Ojn;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamImportMetadataTest
{
    private const string current = "o2lazer-projection:1:20261001 o2lazer-identity:1 o2lazer-chart:1:2 o2lazer-level:1:119 o2lazer-encoding-context:1:none";

    [TestCase("")]
    [TestCase("o2jam o2lazer-clean:2 o2lazer-encoding:2")]
    public void LegacyMarkersDoNotProveProjectionCompletion(string tags) =>
        Assert.That(O2JamImportMetadata.Read(tags, out _), Is.EqualTo(O2JamImportMetadataStatus.Legacy));

    [TestCase("o2lazer-level:1:119")]
    [TestCase("o2lazer-level:1:65536")]
    [TestCase("o2lazer-chart:1:3")]
    [TestCase("o2lazer-encoding-context:1:99")]
    public void RejectsDuplicateTokensEvenWhenTheyAgree(string extra) =>
        Assert.That(O2JamImportMetadata.Read(current + " " + extra, out _), Is.EqualTo(O2JamImportMetadataStatus.Invalid));

    [TestCase("o2lazer-level:1:119", "o2lazer-level:1:65536")]
    [TestCase("o2lazer-chart:1:2", "o2lazer-chart:1:3")]
    [TestCase("o2lazer-level:1:119", "o2lazer-level:1:-1")]
    [TestCase("o2lazer-encoding-context:1:none", "o2lazer-encoding-context:1:99")]
    [TestCase("o2lazer-projection:1:20261001", "o2lazer-projection:1:0")]
    [TestCase("o2lazer-identity:1", "o2lazer-identity:0")]
    [TestCase("o2lazer-chart:1:2", "")]
    public void RejectsIncompleteOrOutOfRangeMetadata(string original, string replacement) =>
        Assert.That(O2JamImportMetadata.Read(current.Replace(original, replacement), out _), Is.EqualTo(O2JamImportMetadataStatus.Invalid));

    [TestCase("o2lazer-projection:1:20261001", "o2lazer-projection:2:20261001")]
    [TestCase("o2lazer-projection:1:20261001", "o2lazer-projection:1:20261002")]
    [TestCase("o2lazer-identity:1", "o2lazer-identity:2")]
    [TestCase("o2lazer-chart:1:2", "o2lazer-chart:2:2")]
    public void RecognisesNewerDataWithoutDowngradingIt(string original, string replacement) =>
        Assert.That(O2JamImportMetadata.Read(current.Replace(original, replacement), out _), Is.EqualTo(O2JamImportMetadataStatus.Unsupported));

    [Test]
    [SetCulture("fr-FR")]
    public void RawLevelAndChartOrderSurviveEditedDisplayNames()
    {
        var beatmap = new BeatmapInfo
        {
            DifficultyName = "Edited name",
            StarRating = 3.5,
            Metadata = new BeatmapMetadata { Tags = current + " o2lazer-o2jam-stars:1:0.5" },
        };
        Assert.Multiple(() =>
        {
            Assert.That(O2JamStarRatingMetadata.ResolveLevel(beatmap), Is.EqualTo(119));
            Assert.That(O2JamStarRatingMetadata.GetO2JamStars(beatmap), Is.EqualTo(11.9).Within(0.00001));
            Assert.That(O2JamStarRatingMetadata.ResolveChartOrder(beatmap), Is.EqualTo(2));
        });
    }

    [TestCase("none", null)]
    [TestCase("0", OjnMetadataEncoding.Automatic)]
    [TestCase("1", OjnMetadataEncoding.Gbk)]
    [TestCase("2", OjnMetadataEncoding.Cp949)]
    public void DistinguishesUnneededContextFromAnAutomaticDirectoryDecision(string value, OjnMetadataEncoding? expected)
    {
        Assert.That(O2JamImportMetadata.Read(current.Replace("context:1:none", "context:1:" + value), out var metadata),
            Is.EqualTo(O2JamImportMetadataStatus.Valid));
        Assert.That(metadata!.EncodingFallback, Is.EqualTo(expected));
    }

    [Test]
    public void ConflictingManiaTokensCannotBecomeAReusableCache()
    {
        var version = O2JamStarRatingMetadata.ManiaVersionTag;
        var combo = O2JamStarRatingMetadata.CreateManiaMaxComboTag(100);
        Assert.Multiple(() =>
        {
            Assert.That(O2JamStarRatingMetadata.HasCurrentManiaVersion(version + " " + version), Is.False);
            Assert.That(O2JamStarRatingMetadata.ReadManiaMaxCombo(combo + " " + combo), Is.Null);
        });
    }
}
