using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
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
    public async Task ReplacingOnlyOjmRequiresFreshWorkingBeatmap()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"o2lazer-ojm-replace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var chartPath = Path.Combine(directory, "chart.ojn");
            var archivePath = Path.Combine(directory, "o2ma100.ojm");
            File.WriteAllBytes(chartPath, OjnTestData.CreateChart());
            var info = new BeatmapInfo(new O2LazerRuleset().RulesetInfo) { DifficultyName = "EX" };
            var inner = new FlatWorkingBeatmap(new Beatmap { BeatmapInfo = info });
            var withoutArchive = new O2JamWorkingBeatmap(inner, null!, chartPath);
            _ = withoutArchive.Beatmap;
            await archiveLoad(withoutArchive).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(withoutArchive.CanReuse, Is.True);

            File.WriteAllBytes(archivePath,
                [(byte)'O', (byte)'J', (byte)'M', 0, 0, 0, 0, 0,
                    20, 0, 0, 0, 20, 0, 0, 0, 20, 0, 0, 0]);
            Assert.That(withoutArchive.CanReuse, Is.False,
                "An OJM appearing after a missing archive must create a fresh wrapper.");

            var withArchive = new O2JamWorkingBeatmap(inner, null!, chartPath);
            _ = withArchive.Beatmap;
            await archiveLoad(withArchive).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(withArchive.CanReuse, Is.True);

            using (var stream = File.Open(archivePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                stream.WriteByte(0);
            Assert.That(withArchive.CanReuse, Is.False,
                "A changed OJM cannot keep the old sample skin or transfer its old music track.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static Task<OjmArchive> archiveLoad(O2JamWorkingBeatmap wrapper)
    {
        var field = typeof(O2JamWorkingBeatmap).GetField("archive", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((Lazy<Task<OjmArchive>>)field.GetValue(wrapper)!).Value;
    }
}
