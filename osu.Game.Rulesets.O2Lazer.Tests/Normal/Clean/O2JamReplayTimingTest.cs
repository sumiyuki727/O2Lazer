using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Input.StateChanges;
using osu.Game.Replays;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.O2Lazer.Replays;
using static osu.Game.Input.Handlers.ReplayInputHandler;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamReplayTimingTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void LargeJumpVisitsEveryPressAndRelease(bool frameAccurate)
    {
        using var handler = createHandler(frameAccurate,
            new O2JamReplayFrame(100, ManiaAction.Key1),
            new O2JamReplayFrame(110),
            new O2JamReplayFrame(120, ManiaAction.Key7),
            new O2JamReplayFrame(300));

        assertStep(handler, 1000, 100, ManiaAction.Key1);
        assertStep(handler, 1000, 110);
        assertStep(handler, 1000, 120, ManiaAction.Key7);
        assertStep(handler, 1000, 300);
        assertStep(handler, 1000, 1000);
    }

    [Test]
    public void EqualTimestampFramesKeepInputOrder()
    {
        using var handler = createHandler(false,
            new O2JamReplayFrame(100, ManiaAction.Key1),
            new O2JamReplayFrame(100),
            new O2JamReplayFrame(100, ManiaAction.Key7));

        assertStep(handler, 100, 100, ManiaAction.Key1);
        assertStep(handler, 100, 100);
        assertStep(handler, 100, 100, ManiaAction.Key7);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RewindRestoresHeldStateAndThenClearsBeforeFirstFrame(bool frameAccurate)
    {
        using var handler = createHandler(frameAccurate,
            new O2JamReplayFrame(100, ManiaAction.Key2),
            new O2JamReplayFrame(200));

        assertStep(handler, 300, 100, ManiaAction.Key2);
        assertStep(handler, 300, 200);
        assertStep(handler, 300, 300);
        assertStep(handler, 0, 200);
        assertStep(handler, 0, 100, ManiaAction.Key2);
        assertStep(handler, 0, 0);
        assertStep(handler, 300, 100, ManiaAction.Key2);
        assertStep(handler, 300, 200);
    }

    [Test]
    public void RepeatedTimeKeepsHeldStateUntilRelease()
    {
        using var handler = createHandler(false,
            new O2JamReplayFrame(100, ManiaAction.Key3),
            new O2JamReplayFrame(500));

        assertStep(handler, 100, 100, ManiaAction.Key3);
        for (var i = 0; i < 3; i++)
            assertStep(handler, 250, 250, ManiaAction.Key3);
        assertStep(handler, 500, 500);
    }

    [Test]
    public void FrameAccuratePlaybackWaitsForNearbyRelease()
    {
        using var handler = createHandler(true,
            new O2JamReplayFrame(100, ManiaAction.Key4),
            new O2JamReplayFrame(110));

        assertStep(handler, 100, 100, ManiaAction.Key4);
        assertStep(handler, 105, null, ManiaAction.Key4);
        assertStep(handler, 110, 110);
    }

    [Test]
    public void IncompleteReplayWaitsAndResumesWhenReleaseArrives()
    {
        var replay = new Replay
        {
            HasReceivedAllFrames = false,
            Frames = [new O2JamReplayFrame(100, ManiaAction.Key5)],
        };
        using var handler = new O2JamFramedReplayInputHandler(replay);
        assertStep(handler, 200, 100, ManiaAction.Key5);
        assertStep(handler, 200, null, ManiaAction.Key5);
        replay.Frames.Add(new O2JamReplayFrame(200));
        replay.HasReceivedAllFrames = true;
        assertStep(handler, 200, 200);
        assertStep(handler, 300, 300);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EmptyReplayOnlyAdvancesWhenComplete(bool complete)
    {
        using var handler = new O2JamFramedReplayInputHandler(new Replay { HasReceivedAllFrames = complete });
        assertStep(handler, 100, complete ? 100 : null);
    }

    private static O2JamFramedReplayInputHandler createHandler(bool frameAccurate, params O2JamReplayFrame[] frames) =>
        new(new Replay { Frames = frames.Cast<osu.Game.Rulesets.Replays.ReplayFrame>().ToList() })
        {
            FrameAccuratePlayback = frameAccurate,
        };

    private static void assertStep(O2JamFramedReplayInputHandler handler, double proposed, double? expected, params ManiaAction[] actions)
    {
        Assert.That(handler.SetFrameFromTime(proposed), Is.EqualTo(expected));
        var inputs = new List<IInput>();
        handler.CollectPendingInputs(inputs);
        Assert.That(inputs.OfType<ReplayState<ManiaAction>>().Single().PressedActions, Is.EqualTo(actions));
    }
}
