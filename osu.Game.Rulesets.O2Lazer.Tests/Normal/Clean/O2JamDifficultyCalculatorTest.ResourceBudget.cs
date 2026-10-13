using System;
using System.IO;
using NUnit.Framework;
using O2Jam.Core;
using O2Jam.Formats.Ojn;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamDifficultyCalculatorTest
{
    [Test]
    public void CommittedDifficultyRejectsOversizedStreamBeforeReadingOrGameplayDecoding()
    {
        var ruleset = new O2LazerRuleset();
        var plan = new O2JamImportPlan("", "", "source.ojn", [], "", "", 100, "", "", "", 120, [], []);
        var chart = new O2JamImportChart(O2JamDifficulty.EX, 5, "", 1000, 1, 1, -1, -1);
        var info = new BeatmapInfo
        {
            Ruleset = ruleset.RulesetInfo,
            BeatmapSet = new BeatmapSetInfo(),
            Metadata = new BeatmapMetadata { AudioFile = plan.FileName, Tags = O2JamImportMetadata.Create(plan, chart) },
            StarRating = -1,
        };
        info.BeatmapSet.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = new string('a', 64) }, plan.FileName));
        using var input = new OversizedOjnStream();
        var calculator = ruleset.CreateDifficultyCalculator(new BudgetWorkingBeatmap(info, input));
        Assert.That(Assert.Throws<InvalidDataException>(() => calculator.Calculate())!.Message, Does.Contain("source byte budget"));
        Assert.That(input.Reads, Is.Zero);
        Assert.That(input.CanRead, Is.False, "The opened committed source must be disposed after failure.");
    }

    private sealed class OversizedOjnStream : MemoryStream
    {
        public int Reads { get; private set; }
        public override long Length => (long)OjnReadLimits.Default.SourceBytes + 1;
        public override int Read(byte[] buffer, int offset, int count)
        {
            Reads++;
            return base.Read(buffer, offset, count);
        }
        public override int Read(Span<byte> buffer)
        {
            Reads++;
            return base.Read(buffer);
        }
    }

    private sealed class BudgetWorkingBeatmap(BeatmapInfo info, Stream input) : WorkingBeatmap(info, null!)
    {
        protected override IBeatmap GetBeatmap() => throw new AssertionException("Committed difficulty must not decode gameplay audio.");
        public override Texture GetBackground() => null!;
        protected override Track GetBeatmapTrack() => null!;
        protected override ISkin GetSkin() => null!;
        public override Stream GetStream(string storagePath) => input;
    }
}
