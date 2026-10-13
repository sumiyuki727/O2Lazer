using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using O2Jam.Core;

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

    [Test]
    public void PublicEventsRejectMutationThroughCollectionInterfaces()
    {
        var map = new O2JamTimingMap(120, [new O2JamBpmEvent(1, 180)]);
        var replacement = new O2JamBpmEvent(1, 240);

        Assert.Throws<NotSupportedException>(() => ((IList<O2JamBpmEvent>)map.Events)[0] = replacement);
        Assert.Throws<NotSupportedException>(() => ((IList)map.Events)[0] = replacement);
        Assert.That(map.Events[0], Is.EqualTo(new O2JamBpmEvent(1, 180)));
        Assert.That(map.EffectiveBpmAtPosition(1), Is.EqualTo(180));
    }

    [Test]
    public void CallerOwnedArrayCannotChangeEventsOrClonedTiming()
    {
        O2JamBpmEvent[] events = [new(1, 180), new(0, 240)];
        var map = new O2JamTimingMap(120, events);
        events[0] = new O2JamBpmEvent(1, 60);
        events[1] = new O2JamBpmEvent(0, 480);
        var copy = new O2JamTimingMap(map.InitialBpm, map.Events);
        O2JamBpmEvent[] expectedEvents = [new(0, 240), new(1, 180)];
        double[] positions = [-0.5, 0, 0.5, 1, 2];

        Assert.Multiple(() =>
        {
            Assert.That(map.Events, Is.EqualTo(expectedEvents));
            Assert.That(map.TimeAt(1), Is.EqualTo(1000).Within(1e-7));
            Assert.That(map.TimeAt(2), Is.EqualTo(1000 + 4000d / 3).Within(1e-7));
            foreach (var position in positions)
                Assert.That(copy.TimeAt(position), Is.EqualTo(map.TimeAt(position)).Within(1e-7));
        });
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    [TestCase(-1)]
    public void InvalidEventPositionIsRejectedBeforeReadingRemainingInput(double position)
    {
        IEnumerable<O2JamBpmEvent> events()
        {
            yield return new O2JamBpmEvent(position, 180);
            throw new InvalidOperationException("Input was read beyond an invalid event position.");
        }

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new O2JamTimingMap(120, events()));
        Assert.That(exception!.ParamName, Is.EqualTo("events"));
    }

    [TestCase(0.75)]
    [TestCase(1)]
    [TestCase(1.5)]
    public void ZeroPositionAndLastAuthoredChangesPreserveNegativeAndMultiSegmentQueries(double rate)
    {
        var map = new O2JamTimingMap(120,
        [
            new O2JamBpmEvent(1, 180),
            new O2JamBpmEvent(0, 240),
            new O2JamBpmEvent(1, 60),
            new O2JamBpmEvent(0.5, 120),
            new O2JamBpmEvent(0, 180),
        ]);
        O2JamBpmEvent[] expectedEvents = [new(0, 180), new(0.5, 120), new(1, 60)];
        double[] positions = [-0.5, 0, 0.25, 0.5, 0.75, 1, 1.5, 2];

        Assert.Multiple(() =>
        {
            Assert.That(map.Events, Is.EqualTo(expectedEvents));
            Assert.That(map.InitialBpm, Is.EqualTo(120));
            Assert.That(map.TimeAt(-0.5, rate), Is.EqualTo(-2000d / 3 / rate).Within(1e-7));
            Assert.That(map.TimeAt(0, rate), Is.Zero);
            Assert.That(map.TimeAt(0.5, rate), Is.EqualTo(2000d / 3 / rate).Within(1e-7));
            Assert.That(map.TimeAt(1, rate), Is.EqualTo((2000d / 3 + 1000) / rate).Within(1e-7));
            Assert.That(map.TimeAt(2, rate), Is.EqualTo((2000d / 3 + 5000) / rate).Within(1e-7));
            Assert.That(map.EffectiveBpmAtTime(-1000, rate), Is.EqualTo(180 * rate));
            foreach (var position in positions)
                Assert.That(map.PositionAt(map.TimeAt(position, rate), rate), Is.EqualTo(position).Within(1e-7));
        });
    }

    [Test]
    public void EventSourceIsEnumeratedOnce()
    {
        var enumerations = 0;
        IEnumerable<O2JamBpmEvent> events()
        {
            if (++enumerations > 1)
                throw new InvalidOperationException("Event source was enumerated more than once.");
            yield return new O2JamBpmEvent(1, 240);
            yield return new O2JamBpmEvent(0, 180);
        }

        var map = new O2JamTimingMap(120, events());
        Assert.That(enumerations, Is.EqualTo(1));
        Assert.That(map.TimeAt(2), Is.EqualTo(4000d / 3 + 1000).Within(1e-7));
    }
}
