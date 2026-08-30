using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Core;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Core;

[TestFixture]
public class O2JamTimingMapTest
{
    [Test]
    public void ConstantBpmRoundTripsPositionAndTime()
    {
        var map = new O2JamTimingMap(120);

        Assert.That(map.TimeAt(1), Is.EqualTo(2000).Within(1e-7));
        Assert.That(map.PositionAt(2000), Is.EqualTo(1).Within(1e-7));
        Assert.That(map.TimeAt(0.5), Is.EqualTo(1000).Within(1e-7));
        Assert.That(map.PositionAt(1000), Is.EqualTo(0.5).Within(1e-7));
    }

    [Test]
    public void BpmChangeIsIntegratedWithinOnePlay()
    {
        var map = new O2JamTimingMap(120, [new O2JamBpmEvent(1, 240)]);

        Assert.That(map.TimeAt(1), Is.EqualTo(2000).Within(1e-7));
        Assert.That(map.TimeAt(2), Is.EqualTo(3000).Within(1e-7));
        Assert.That(map.PositionAt(2500), Is.EqualTo(1.5).Within(1e-7));
        Assert.That(map.EffectiveBpmAtTime(1999), Is.EqualTo(120));
        Assert.That(map.EffectiveBpmAtTime(2000), Is.EqualTo(240));
    }

    [Test]
    public void PlaybackRateChangesEffectiveBpmRatherThanWindowMultiplier()
    {
        var map = new O2JamTimingMap(120, [new O2JamBpmEvent(1, 240)]);

        Assert.That(map.TimeAt(1, 2), Is.EqualTo(1000).Within(1e-7));
        Assert.That(map.TimeAt(2, 2), Is.EqualTo(1500).Within(1e-7));
        Assert.That(map.PositionAt(1250, 2), Is.EqualTo(1.5).Within(1e-7));
        Assert.That(map.EffectiveBpmAtTime(999, 2), Is.EqualTo(240));
        Assert.That(map.EffectiveBpmAtTime(1000, 2), Is.EqualTo(480));
    }

    [Test]
    public void SamePositionBpmEventUsesLastAuthoredValue()
    {
        var map = new O2JamTimingMap(120,
        [
            new O2JamBpmEvent(1, 180),
            new O2JamBpmEvent(1, 240),
        ]);

        Assert.That(map.EffectiveBpmAtPosition(1), Is.EqualTo(240));
    }
}
