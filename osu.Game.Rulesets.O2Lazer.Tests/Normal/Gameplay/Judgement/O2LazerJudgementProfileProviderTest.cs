using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.O2Jam;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class O2LazerJudgementProfileProviderTest
{
    [Test]
    public void TestWindowsScaleInverselyWithBpm()
    {
        var slow130 = table(130).SlowWindowFor(HitResult.Ok);
        var slow260 = table(260).SlowWindowFor(HitResult.Ok);

        Assert.That(slow260, Is.EqualTo(slow130 / 2).Within(0.001));
    }

    [Test]
    public void TestWindowsMatchOriginalBeatArea()
    {
        var windows = table(130);

        Assert.Multiple(() =>
        {
            Assert.That(windows.FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(O2JamScoring.BeatWindowForBpm(130, O2JamScoring.CoolBeatThreshold)).Within(0.001));
            Assert.That(windows.FrameworkWindowFor(HitResult.Good), Is.EqualTo(O2JamScoring.BeatWindowForBpm(130, O2JamScoring.GoodBeatThreshold)).Within(0.001));
            Assert.That(windows.SlowWindowFor(HitResult.Ok), Is.EqualTo(O2JamScoring.BeatWindowForBpm(130, O2JamScoring.BadBeatThreshold)).Within(0.001));
        });
    }

    [Test]
    public void TestBpmAtTimeFollowsChanges()
    {
        var timingMap = new O2LazerTimingMap(
            192,
            [new O2LazerMeasureInfo(0, 0, 192, 1)],
            [
                new O2LazerBpmEvent(0, 120, 0),
                new O2LazerBpmEvent(192, 180, 2000),
            ],
            [],
            [],
            [],
            120);

        Assert.Multiple(() =>
        {
            Assert.That(timingMap.GetBpmAtTime(1000), Is.EqualTo(120));
            Assert.That(timingMap.GetBpmAtTime(2001), Is.EqualTo(180));
        });
    }

    private static O2LazerJudgementWindowTable table(double bpm)
        => O2LazerJudgementProfileProvider.GetTable(O2LazerLayoutVariant.O2Jam7K, 1, 2, tail: false, bpm);
}
