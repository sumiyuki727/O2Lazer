using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Formats.Ojm;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
public class O2JamWorkingBeatmapRecoveryTest
{
    [Test]
    public void FailedArchiveLoadMakesWrapperIneligibleForReuse()
    {
        var info = new BeatmapInfo(new O2LazerRuleset().RulesetInfo) { DifficultyName = "EX" };
        var inner = new FlatWorkingBeatmap(new Beatmap { BeatmapInfo = info });
        var missingPath = Path.Combine(Path.GetTempPath(), $"o2lazer-missing-{Guid.NewGuid():N}.ojn");
        var wrapper = new O2JamWorkingBeatmap(inner, null!, missingPath);

        Assert.That(wrapper.CanReuse, Is.True);
        _ = wrapper.Beatmap;
        Assert.That(() => wrapper.CanReuse, Is.False.After(2000, 10),
            "A later selection needs a fresh wrapper after transient archive I/O failure.");
    }

    [Test]
    public async Task ReplacingOnlyOjmRequiresFreshResourceSnapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"o2lazer-ojm-replace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var chartPath = Path.Combine(directory, "chart.ojn");
            var archivePath = Path.Combine(directory, "o2ma100.ojm");
            File.WriteAllBytes(chartPath, OjnTestData.CreateChart());
            var withoutArchive = new O2JamExternalChartResources(chartPath, O2JamDifficulty.EX);
            await withoutArchive.Archive.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(withoutArchive.CanReuse, Is.True);

            File.WriteAllBytes(archivePath,
                [(byte)'O', (byte)'J', (byte)'M', 0, 0, 0, 0, 0,
                    20, 0, 0, 0, 20, 0, 0, 0, 20, 0, 0, 0]);
            Assert.That(withoutArchive.CanReuse, Is.False,
                "An OJM appearing after a missing archive must create a fresh wrapper.");

            var withArchive = new O2JamExternalChartResources(chartPath, O2JamDifficulty.EX);
            await withArchive.Archive.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(withArchive.CanReuse, Is.True);

            OjmArchiveCache.Shared.InvalidateSource(chartPath);
            Assert.That(withArchive.CanReuse, Is.False,
                "Explicit invalidation must replace a wrapper even when OJM length and timestamp match.");

            var afterRefresh = new O2JamExternalChartResources(chartPath, O2JamDifficulty.EX);
            await afterRefresh.Archive.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(afterRefresh.CanReuse, Is.True);

            using (var stream = File.Open(archivePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                stream.WriteByte(0);
            Assert.That(afterRefresh.CanReuse, Is.False,
                "A changed OJM cannot keep the old sample skin or transfer its old music track.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
