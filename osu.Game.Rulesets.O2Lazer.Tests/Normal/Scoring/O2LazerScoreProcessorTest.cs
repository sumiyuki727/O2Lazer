using NUnit.Framework;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Scoring;

[TestFixture]
public class O2LazerScoreProcessorTest
{
    [Test]
    public void TestFirstPerfectCountsCombo()
    {
        var beatmap = createBeatmap();
        var processor = new O2LazerScoreProcessor();
        processor.ApplyBeatmap(beatmap);

        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
        {
            Type = HitResult.Perfect,
        });

        Assert.Multiple(() =>
        {
            Assert.That(processor.Combo.Value, Is.EqualTo(1));
            Assert.That(processor.HighestCombo.Value, Is.EqualTo(1));
        });
    }

    [Test]
    public void TestBadBreaksCombo()
    {
        var beatmap = createBeatmap(count: 2);
        var processor = new O2LazerScoreProcessor();
        processor.ApplyBeatmap(beatmap);

        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
        {
            Type = HitResult.Perfect,
        });
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[1], beatmap.HitObjects[1].CreateJudgement())
        {
            Type = HitResult.Ok,
        });

        Assert.Multiple(() =>
        {
            Assert.That(processor.Combo.Value, Is.Zero);
            Assert.That(processor.HighestCombo.Value, Is.EqualTo(1));
        });
    }

    private static O2LazerBeatmap createBeatmap(int count = 1)
    {
        var beatmap = new O2LazerBeatmap
        {
            LayoutVariant = O2LazerLayoutVariant.O2Jam7K,
            TotalColumns = 7,
            Rank = 2,
        };

        for (var i = 0; i < count; i++)
        {
            beatmap.HitObjects.Add(new O2LazerNote
            {
                StartTime = 1000 + i * 100,
                Column = i % 7,
                Beatmap = beatmap,
            });
        }

        return beatmap;
    }
}
