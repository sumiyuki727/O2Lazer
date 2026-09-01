using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Difficulty;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamStarRatingMetadataTest
{
    [TestCase(0)]
    [TestCase(119)]
    [TestCase(65535)]
    [SetCulture("fr-FR")]
    public void RoundTripsO2JamStarsIndependentlyOfDisplayCulture(int level)
    {
        var tag = O2JamStarRatingMetadata.CreateO2JamTag((ushort)level);
        Assert.Multiple(() =>
        {
            Assert.That(tag, Does.Not.Contain(","));
            Assert.That(O2JamStarRatingMetadata.ReadO2Jam($"o2ma100\t{tag}\nother"), Is.EqualTo(O2JamDifficultyRating.FromLevel(level)));
        });
    }

    [TestCase("")]
    [TestCase("o2ma100")]
    [TestCase("o2lazer-mania-version:0:20241007")]
    [TestCase("o2lazer-mania-version:1:0")]
    public void MissingOrOutdatedVersionsRequireRefresh(string tags)
    {
        var beatmap = new BeatmapInfo { StarRating = 3.25, Metadata = new BeatmapMetadata { Tags = tags } };
        Assert.That(O2JamStarRatingMetadata.ReadMania(beatmap), Is.Null);
    }

    [TestCase("NaN")]
    [TestCase("Infinity")]
    [TestCase("-1")]
    [TestCase("3,25")]
    [TestCase("1e1000")]
    [TestCase("garbage")]
    public void InvalidValuesRequireRefresh(string value)
    {
        var tag = O2JamStarRatingMetadata.CreateO2JamTag(0);
        var prefix = tag[..(tag.LastIndexOf(':') + 1)];
        Assert.That(O2JamStarRatingMetadata.ReadO2Jam(prefix + value), Is.Null);
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(-1)]
    public void InvalidNativeStarsRequireRecalculation(double stars)
    {
        var beatmap = new BeatmapInfo { StarRating = stars, Metadata = new BeatmapMetadata { Tags = O2JamStarRatingMetadata.ManiaVersionTag } };
        Assert.That(O2JamStarRatingMetadata.ReadMania(beatmap), Is.Null);
    }

    [Test]
    public void NativeReprocessingIsRecognisedBeforeImportMetadataIsRefreshed()
    {
        var beatmap = new BeatmapInfo { StarRating = 3.123456789012345, DifficultyName = "HX Lv.119" };
        beatmap.Ruleset.LastAppliedDifficultyVersion = O2JamManiaStarRating.CacheVersion;
        Assert.Multiple(() =>
        {
            Assert.That(O2JamStarRatingMetadata.ReadMania(beatmap), Is.EqualTo(beatmap.StarRating));
            Assert.That(O2JamStarRatingMetadata.GetO2JamStars(beatmap), Is.EqualTo(11.9).Within(0.000001));
        });
        beatmap.DifficultyName = "Edited name";
        Assert.That(O2JamStarRatingMetadata.ResolveLevel(beatmap), Is.Zero);
    }
}
