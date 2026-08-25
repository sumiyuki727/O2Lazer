using System.Reflection;
using NUnit.Framework;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.O2Lazer.UI.Gameplay;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play.HUD;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Scoring;

[TestFixture]
public class O2LazerScoreProcessorTest
{
    [Test]
    public void TestFirstPerfectStartsAtZero()
    {
        var beatmap = createBeatmap(count: 2);
        var processor = new O2LazerScoreProcessor();
        processor.ApplyBeatmap(beatmap);

        applyResult(processor, beatmap, 0, HitResult.Perfect);

        Assert.Multiple(() =>
        {
            Assert.That(processor.Combo.Value, Is.Zero);
            Assert.That(processor.HighestCombo.Value, Is.Zero);
        });
    }

    [Test]
    public void TestSecondPerfectCountsOne()
    {
        var beatmap = createBeatmap(count: 2);
        var processor = new O2LazerScoreProcessor();
        processor.ApplyBeatmap(beatmap);

        applyResult(processor, beatmap, 0, HitResult.Perfect);
        applyResult(processor, beatmap, 1, HitResult.Perfect);

        Assert.Multiple(() =>
        {
            Assert.That(processor.Combo.Value, Is.EqualTo(1));
            Assert.That(processor.HighestCombo.Value, Is.EqualTo(1));
        });
    }

    [Test]
    public void TestBadBreaksComboToMinusOne()
    {
        var beatmap = createBeatmap(count: 2);
        var processor = new O2LazerScoreProcessor();
        processor.ApplyBeatmap(beatmap);

        applyResult(processor, beatmap, 0, HitResult.Perfect);
        applyResult(processor, beatmap, 1, HitResult.Ok);

        Assert.Multiple(() =>
        {
            Assert.That(processor.Combo.Value, Is.EqualTo(-1));
            Assert.That(processor.HighestCombo.Value, Is.Zero);
        });
    }

    [Test]
    public void TestPerfectAfterBreakReturnsToZero()
    {
        var beatmap = createBeatmap(count: 3);
        var processor = new O2LazerScoreProcessor();
        processor.ApplyBeatmap(beatmap);

        applyResult(processor, beatmap, 0, HitResult.Perfect);
        applyResult(processor, beatmap, 1, HitResult.Ok);
        applyResult(processor, beatmap, 2, HitResult.Perfect);

        Assert.Multiple(() =>
        {
            Assert.That(processor.Combo.Value, Is.Zero);
            Assert.That(processor.HighestCombo.Value, Is.Zero);
        });
    }

    [Test]
    public void TestMaximumAchievableComboHasFirstNoteOffset()
    {
        var beatmap = createBeatmap(count: 2);
        var processor = new O2LazerScoreProcessor();
        processor.ApplyBeatmap(beatmap);

        var scoreInfo = new ScoreInfo();
        processor.PopulateScore(scoreInfo);

        Assert.That(scoreInfo.GetMaximumAchievableCombo(), Is.EqualTo(1));
    }

    [Test]
    public void TestFrameworkComboCountersClampNegativeDisplay()
    {
        Assert.That(O2LazerComboCounterDisplayPatcher.IsInstalled, Is.True);

        var defaultCounter = new DefaultComboCounter();
        defaultCounter.DisplayedCount = -1;
        Assert.That(defaultCounter.DisplayedCount, Is.Zero);

        var legacyCounter = new LegacyDefaultComboCounter();
        typeof(LegacyDefaultComboCounter).GetProperty(nameof(LegacyDefaultComboCounter.DisplayedCount))!.SetValue(legacyCounter, -1);
        Assert.That(legacyCounter.DisplayedCount, Is.Zero);
    }

    private static void applyResult(O2LazerScoreProcessor processor, O2LazerBeatmap beatmap, int index, HitResult result)
    {
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[index], beatmap.HitObjects[index].CreateJudgement())
        {
            Type = result,
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
