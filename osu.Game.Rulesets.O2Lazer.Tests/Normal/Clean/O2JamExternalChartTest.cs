using System;
using System.IO;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets;
using osu.Game.Rulesets.O2Lazer.Beatmaps;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamExternalChartTest
{
    private string directory = null!;
    private string outsideFile = null!;

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), $"o2lazer-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "chart.ojn"), [1]);
        outsideFile = Path.Combine(Path.GetDirectoryName(directory)!, $"outside-{Guid.NewGuid():N}.ojm");
        File.WriteAllBytes(outsideFile, [2]);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(directory, true);
        File.Delete(outsideFile);
    }

    [Test]
    public void ResolvesOnlyFilesInsideExternalChartDirectory()
    {
        var beatmap = createBeatmapInfo(directory, "chart.ojn");

        Assert.Multiple(() =>
        {
            Assert.That(O2JamExternalChart.IsO2JamEntry(beatmap), Is.True);
            Assert.That(O2JamExternalChart.TryResolve(beatmap, out var resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(Path.Combine(directory, "chart.ojn")));
            Assert.That(O2JamExternalChart.TryResolveResource(resolved, Path.GetFileName(outsideFile), out _), Is.False);
            Assert.That(O2JamExternalChart.TryResolveResource(resolved, outsideFile, out _), Is.False);
        });
    }

    [Test]
    public void RejectsDifferentRulesetIdentity()
    {
        var beatmap = createBeatmapInfo(directory, "chart.ojn", "mania");
        Assert.That(O2JamExternalChart.TryResolve(beatmap, out _), Is.False);
    }

    [TestCase("bms")]
    [TestCase("o2jam")]
    public void DoesNotClaimOtherKeySoundRulesetEntry(string shortName)
    {
        var beatmap = createBeatmapInfo(directory, "chart.ojn", shortName);

        Assert.Multiple(() =>
        {
            Assert.That(O2JamExternalChart.IsO2JamEntry(beatmap), Is.False);
            Assert.That(O2JamExternalChart.TryResolve(beatmap, out _), Is.False);
        });
    }

    [Test]
    public void ResolvesSharedSourceAfterDifficultyHashIsSeparated()
    {
        var beatmap = createBeatmapInfo(directory, "chart.ojn");
        beatmap.Hash = "difficulty-specific-hash";
        beatmap.Metadata.AudioFile = "chart.ojn";

        Assert.Multiple(() =>
        {
            Assert.That(beatmap.Path, Is.Null);
            Assert.That(O2JamExternalChart.TryResolve(beatmap, out var resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(Path.Combine(directory, "chart.ojn")));
        });
    }

    private static BeatmapInfo createBeatmapInfo(string source, string fileName, string shortName = O2LazerIdentity.ShortName)
    {
        const string hash = "chart-hash";
        var beatmapSet = new BeatmapSetInfo();
        beatmapSet.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = hash }, fileName));

        var beatmap = new BeatmapInfo(new RulesetInfo { ShortName = shortName }, metadata: new BeatmapMetadata { Source = source })
        {
            Hash = hash,
            BeatmapSet = beatmapSet,
        };

        beatmapSet.Beatmaps.Add(beatmap);
        return beatmap;
    }
}
