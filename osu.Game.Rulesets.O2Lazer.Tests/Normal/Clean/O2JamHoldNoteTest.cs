using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Objects;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamHoldNoteTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void ApplyingDefaultsKeepsOnlyHeadSamples(bool hasTailSamples)
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        var headSample = new O2JamHitSampleInfo(7, 70, 0.5f);
        var hold = new O2JamHoldNote
        {
            StartTime = 100,
            Duration = 1000,
            Samples = [new O2JamHitSampleInfo(9, 100, 0)],
            NodeSamples = hasTailSamples
                ? [[headSample], [new O2JamHitSampleInfo(8, 90, -0.5f)]]
                : [[headSample]],
        };

        hold.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);

        Assert.Multiple(() =>
        {
            Assert.That(hold.NodeSamples, Has.Count.EqualTo(2));
            Assert.That(hold.GetNodeSamples(0), Is.EqualTo(new[] { headSample }));
            Assert.That(hold.GetNodeSamples(1), Is.Empty);
            Assert.That(hold.Head.Samples, Is.EqualTo(new[] { headSample }));
            Assert.That(hold.Tail.Samples, Is.Empty);
            Assert.That(hold.Body.Samples, Is.Empty);
            Assert.That(hold.Tail.StartTime, Is.EqualTo(1100));
            Assert.That(hold.Tail, Is.TypeOf<O2JamHoldTail>());
        });
    }

    [Test]
    public void MissingNodeSamplesUseMainSamplesOnlyAtHead()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        var headSample = new O2JamHitSampleInfo(7, 70, 0.5f);
        var hold = new O2JamHoldNote
        {
            StartTime = 100,
            Duration = 1000,
            Samples = [headSample],
        };

        hold.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);

        Assert.Multiple(() =>
        {
            Assert.That(hold.Head.Samples, Is.EqualTo(new[] { headSample }));
            Assert.That(hold.Tail.Samples, Is.Empty);
            Assert.That(hold.Body.Samples, Is.Empty);
        });
    }
}
