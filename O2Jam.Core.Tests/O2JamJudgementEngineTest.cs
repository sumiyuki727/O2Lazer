using NUnit.Framework;
using O2Jam.Core;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Core;

[TestFixture]
public class O2JamJudgementEngineTest
{
    [TestCase(O2JamEndpointKind.Tap, 6, O2JamAccuracy.Cool)]
    [TestCase(O2JamEndpointKind.Tap, -6, O2JamAccuracy.Cool)]
    [TestCase(O2JamEndpointKind.Tap, 18, O2JamAccuracy.Good)]
    [TestCase(O2JamEndpointKind.Tap, -18, O2JamAccuracy.Good)]
    [TestCase(O2JamEndpointKind.Tap, 25, O2JamAccuracy.Bad)]
    [TestCase(O2JamEndpointKind.HoldHead, -25, O2JamAccuracy.Bad)]
    [TestCase(O2JamEndpointKind.HoldRelease, 25, O2JamAccuracy.Bad)]
    [TestCase(O2JamEndpointKind.HoldRelease, -25, O2JamAccuracy.Bad)]
    public void OriginalIntegerBoundsRemainInsideContinuousWindows(O2JamEndpointKind endpoint, double ticks, O2JamAccuracy expected)
    {
        var map = new O2JamTimingMap(120);
        var engine = new O2JamJudgementEngine(new O2JamPositionClock(map));
        const double target = 1;
        var inputPosition = target + O2JamTimingMap.TicksToPosition(ticks);

        var result = engine.Judge(target, map.TimeAt(inputPosition), endpoint);

        Assert.That(result.Accuracy, Is.EqualTo(expected));
        Assert.That(result.OffsetTicks, Is.EqualTo(ticks).Within(1e-6));
    }

    [TestCase(O2JamEndpointKind.Tap)]
    [TestCase(O2JamEndpointKind.HoldHead)]
    [TestCase(O2JamEndpointKind.HoldRelease)]
    public void FractionalPositionsRemainContinuous(O2JamEndpointKind endpoint)
    {
        var map = new O2JamTimingMap(150);
        var engine = new O2JamJudgementEngine(new O2JamPositionClock(map));
        var inputTime = map.TimeAt(1 + O2JamTimingMap.TicksToPosition(6.5));
        var result = engine.Judge(1, inputTime, endpoint);
        Assert.That(result.Accuracy, Is.EqualTo(O2JamAccuracy.Cool));
        Assert.That(result.OffsetTicks, Is.EqualTo(6.5).Within(1e-10));
    }

    [TestCase(-25.00001, O2JamAccuracy.None)]
    [TestCase(-25, O2JamAccuracy.Bad)]
    [TestCase(-18.00001, O2JamAccuracy.Bad)]
    [TestCase(-18, O2JamAccuracy.Good)]
    [TestCase(-6.00001, O2JamAccuracy.Good)]
    [TestCase(-6, O2JamAccuracy.Cool)]
    [TestCase(6.99999, O2JamAccuracy.Cool)]
    [TestCase(7, O2JamAccuracy.Good)]
    [TestCase(18.99999, O2JamAccuracy.Good)]
    [TestCase(19, O2JamAccuracy.Bad)]
    [TestCase(25.99999, O2JamAccuracy.Bad)]
    [TestCase(26, O2JamAccuracy.Miss)]
    public void ContinuousIntervalsKeepInclusiveEarlyAndExclusiveLateBounds(double ticks, O2JamAccuracy expected)
    {
        var map = new O2JamTimingMap(160);
        var engine = new O2JamJudgementEngine(new O2JamPositionClock(map));
        O2JamEndpointKind[] endpoints = [O2JamEndpointKind.Tap, O2JamEndpointKind.HoldHead, O2JamEndpointKind.HoldRelease];
        foreach (var endpoint in endpoints)
        {
            var result = engine.Judge(1, map.TimeAt(1 + O2JamTimingMap.TicksToPosition(ticks)), endpoint);
            Assert.That(result.Accuracy, Is.EqualTo(expected == O2JamAccuracy.None && endpoint == O2JamEndpointKind.HoldRelease
                ? O2JamAccuracy.Miss : expected), endpoint.ToString());
            Assert.That(result.OffsetTicks, Is.EqualTo(ticks).Within(1e-10));
        }
    }

    [TestCase(-0.05)]
    [TestCase(0)]
    [TestCase(1.003)]
    public void IntervalPolicyAlsoAppliesToNegativeAndFractionalTargets(double target)
    {
        var map = new O2JamTimingMap(160);
        var engine = new O2JamJudgementEngine(new O2JamPositionClock(map));
        Assert.That(engine.Judge(target, map.TimeAt(target + O2JamTimingMap.TicksToPosition(6.5)), O2JamEndpointKind.Tap).Accuracy,
            Is.EqualTo(O2JamAccuracy.Cool));
        Assert.That(engine.Judge(target, map.TimeAt(target - O2JamTimingMap.TicksToPosition(6.5)), O2JamEndpointKind.Tap).Accuracy,
            Is.EqualTo(O2JamAccuracy.Good));
    }

    [Test]
    public void PassiveMissStartsAtTheExcludedLateBadBoundary()
    {
        var map = new O2JamTimingMap(120);
        var engine = new O2JamHoldJudgementEngine(new O2JamPositionClock(map));
        var holding = new O2JamHoldState().ResolveHead(O2JamAccuracy.Cool).BeginHold();
        var justInside = map.TimeAt(1 + O2JamTimingMap.TicksToPosition(25.99999));
        var boundary = map.TimeAt(1 + O2JamTimingMap.TicksToPosition(26));
        Assert.That(engine.InspectHead(default, 1, justInside, false), Is.EqualTo(O2JamAccuracy.None));
        Assert.That(engine.InspectHead(default, 1, boundary, false), Is.EqualTo(O2JamAccuracy.Miss));
        Assert.That(engine.InspectTail(holding, 1, justInside, false, false), Is.EqualTo(O2JamAccuracy.None));
        Assert.That(engine.InspectTail(holding, 1, boundary, false, false), Is.EqualTo(O2JamAccuracy.Miss));
    }

    [Test]
    public void EarlyTapOutsideBadWindowIsIgnoredAndLateTapMisses()
    {
        var map = new O2JamTimingMap(120);
        var engine = new O2JamJudgementEngine(new O2JamPositionClock(map));
        const double target = 1;
        var outside = O2JamTimingMap.TicksToPosition(O2JamJudgementEngine.TapAndHeadBadTicks + 0.01);

        Assert.That(engine.Judge(target, map.TimeAt(target - outside), O2JamEndpointKind.Tap).Accuracy, Is.EqualTo(O2JamAccuracy.None));
        Assert.That(engine.Judge(target, map.TimeAt(target + O2JamTimingMap.TicksToPosition(26)), O2JamEndpointKind.Tap).Accuracy, Is.EqualTo(O2JamAccuracy.Miss));
    }

    [Test]
    public void ExplicitEarlyReleaseOutsideWindowMisses()
    {
        var map = new O2JamTimingMap(120);
        var engine = new O2JamJudgementEngine(new O2JamPositionClock(map));
        const double target = 1;
        var outside = O2JamTimingMap.TicksToPosition(O2JamJudgementEngine.ReleaseBadTicks + 0.01);

        Assert.That(engine.Judge(target, map.TimeAt(target - outside), O2JamEndpointKind.HoldRelease).Accuracy, Is.EqualTo(O2JamAccuracy.Miss));
    }

    [TestCase(92880.05432143154, O2JamAccuracy.Bad)]
    [TestCase(92867.87592267562, O2JamAccuracy.Bad)]
    [TestCase(92859.41780821918, O2JamAccuracy.Miss)]
    public void OutroTempoChangeWidensReleaseWindow(double releaseTime, O2JamAccuracy expected)
    {
        // SAY THAT YOU changes to half BPM at the last centre LN's head. Testing only its
        // initial BPM incorrectly classifies the reported ~408 ms early release as a miss.
        var map = new O2JamTimingMap(146, [new O2JamBpmEvent(56.25, 73)]);
        var engine = new O2JamJudgementEngine(new O2JamPositionClock(map));
        var result = engine.Judge(56.5, releaseTime, O2JamEndpointKind.HoldRelease);

        Assert.Multiple(() =>
        {
            Assert.That(map.TimeAt(56.25), Is.EqualTo(92465.75342465754).Within(1e-6));
            Assert.That(map.TimeAt(56.5), Is.EqualTo(93287.67123287672).Within(1e-6));
            Assert.That(result.EffectiveBpm, Is.EqualTo(73));
            Assert.That(result.Accuracy, Is.EqualTo(expected));
        });
    }

    [Test]
    public void CoolWindowCrossingBpmChangeIsAsymmetricInMilliseconds()
    {
        var map = new O2JamTimingMap(120, [new O2JamBpmEvent(1, 240)]);
        var engine = new O2JamJudgementEngine(new O2JamPositionClock(map));
        const double target = 1.01;
        var positionWindow = O2JamTimingMap.TicksToPosition(O2JamJudgementEngine.CoolTicks);
        var targetTime = map.TimeAt(target);
        var earlyTime = map.TimeAt(target - positionWindow);
        var lateTime = map.TimeAt(target + O2JamTimingMap.TicksToPosition(7));

        Assert.That(engine.Judge(target, earlyTime, O2JamEndpointKind.Tap).Accuracy, Is.EqualTo(O2JamAccuracy.Cool));
        Assert.That(engine.Judge(target, lateTime, O2JamEndpointKind.Tap).Accuracy, Is.EqualTo(O2JamAccuracy.Good));
        Assert.That(targetTime - earlyTime, Is.GreaterThan(lateTime - targetTime));
        Assert.That(targetTime - earlyTime, Is.EqualTo(52.5).Within(1e-6));
        Assert.That(lateTime - targetTime, Is.EqualTo(36.4583333333).Within(1e-6));
    }

    [Test]
    public void RateChangeShrinksRealTimeWindowThroughPositionClock()
    {
        var map = new O2JamTimingMap(120);
        var engine = new O2JamJudgementEngine(new O2JamPositionClock(map, 2));
        const double target = 0.5;
        var boundary = target + O2JamTimingMap.TicksToPosition(O2JamJudgementEngine.CoolTicks);
        var targetTime = map.TimeAt(target, 2);
        var boundaryTime = map.TimeAt(boundary, 2);

        Assert.That(engine.Judge(target, boundaryTime, O2JamEndpointKind.Tap).Accuracy, Is.EqualTo(O2JamAccuracy.Cool));
        Assert.That(boundaryTime - targetTime, Is.EqualTo(31.25).Within(1e-6));
        Assert.That(engine.Judge(target, targetTime, O2JamEndpointKind.Tap).EffectiveBpm, Is.EqualTo(240));
    }
}
