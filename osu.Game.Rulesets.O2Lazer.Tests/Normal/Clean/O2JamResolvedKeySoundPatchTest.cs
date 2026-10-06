using System.Reflection;
using NUnit.Framework;
using O2Jam.Core;
using osu.Framework.Graphics;
using osu.Framework.Timing;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
public class O2JamResolvedKeySoundPatchTest
{
    [Test]
    public void NativeSelectorSuppressesOnlyTheResolvedO2JamColumnFallback()
    {
        _ = new O2LazerRuleset();
        var hold = new O2JamHoldNote
        {
            StartTime = 1000,
            Duration = 1000,
            HeadChartPosition = 1,
            TailChartPosition = 2,
            TimingMap = new O2JamTimingMap(120),
        };
        hold.ApplyDefaults(new osu.Game.Beatmaps.ControlPoints.ControlPointInfo(), new osu.Game.Beatmaps.BeatmapDifficulty());
        var headEntry = new HitObjectLifetimeEntry(hold.Head);
        var holdEntry = new HitObjectLifetimeEntry(hold);
        holdEntry.NestedEntries.Add(headEntry);
        var resultField = typeof(HitObjectLifetimeEntry).GetField("Result", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var selectedField = typeof(GameplaySampleTriggerSource).GetField("mostValidObject", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var select = typeof(GameplaySampleTriggerSource).GetMethod("GetMostValidObject", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var parentProperty = typeof(Drawable).GetProperty(nameof(Drawable.Parent))!;
        var clock = new FramedClock(new ManualClock { CurrentTime = 2001 });
        clock.ProcessFrame();
        using var source = new GameplaySampleTriggerSource(new HitObjectContainer()) { Clock = clock };
        selectedField.SetValue(source, holdEntry);
        parentProperty.SetValue(source, new O2JamManiaColumn(0, false));

        Assert.That(select.Invoke(source, null), Is.SameAs(hold),
            "Before the LN head is judged, the native selector must remain available.");

        resultField.SetValue(headEntry, new JudgementResult(hold.Head, hold.Head.CreateJudgement()) { Type = HitResult.Perfect });
        Assert.That(select.Invoke(source, null), Is.Null,
            "An O2Jam column must not replay a resolved LN head on a later key press.");

        parentProperty.SetValue(source, null);
        parentProperty.SetValue(source, new Column(0, false));
        Assert.That(select.Invoke(source, null), Is.SameAs(hold),
            "The native Mania column must retain its original fallback.");
    }

    [Test]
    public void ResolvedHoldHeadCannotBecomeANewColumnKeysound()
    {
        _ = new O2LazerRuleset();
        Assert.That(O2JamResolvedKeySoundPatch.IsInstalled, Is.True);
        Assert.That(O2JamCompatibilityPatches.CanPlay, Is.True);

        var hold = new O2JamHoldNote
        {
            StartTime = 1000,
            Duration = 1000,
            HeadChartPosition = 1,
            TailChartPosition = 2,
            TimingMap = new O2JamTimingMap(120),
        };
        hold.ApplyDefaults(new osu.Game.Beatmaps.ControlPoints.ControlPointInfo(), new osu.Game.Beatmaps.BeatmapDifficulty());

        var headEntry = new HitObjectLifetimeEntry(hold.Head);
        var holdEntry = new HitObjectLifetimeEntry(hold);
        holdEntry.NestedEntries.Add(headEntry);
        Assert.That(O2JamResolvedKeySoundPatch.ShouldSuppress(hold, holdEntry), Is.False,
            "A first press must retain the native keysound path.");

        var result = new JudgementResult(hold.Head, hold.Head.CreateJudgement()) { Type = HitResult.Perfect };
        var resultField = typeof(HitObjectLifetimeEntry).GetField("Result", BindingFlags.Instance | BindingFlags.NonPublic)!;
        resultField.SetValue(headEntry, result);

        Assert.Multiple(() =>
        {
            Assert.That(O2JamResolvedKeySoundPatch.ShouldSuppress(hold, holdEntry), Is.True,
                "The parent keeps its head sample after release, but must not sound again.");
            Assert.That(O2JamResolvedKeySoundPatch.ShouldSuppress(hold.Head, holdEntry), Is.False,
                "Only Mania's fallback to the parent is suppressed.");
            Assert.That(O2JamResolvedKeySoundPatch.ShouldSuppress(hold, new HitObjectLifetimeEntry(hold)), Is.False,
                "Another lifetime entry must not borrow the finished head state.");
        });

        resultField.SetValue(headEntry, null);
        Assert.That(O2JamResolvedKeySoundPatch.ShouldSuppress(hold, holdEntry), Is.False,
            "Rewinding before the head must restore its original keysound eligibility.");
    }

    [TestCase(false, HitResult.Perfect)]
    [TestCase(false, HitResult.Miss)]
    [TestCase(true, HitResult.Perfect)]
    [TestCase(true, HitResult.Miss)]
    public void NativeSelectorCannotReplayResolvedTapButRewindRestoresIt(bool maniaScore, HitResult result)
    {
        _ = new O2LazerRuleset();
        Note note = maniaScore ? new Note() : new O2JamNote();
        note.StartTime = 1000;
        note.Samples = [new O2JamHitSampleInfo(7, 100, 0)];
        note.ApplyDefaults(new osu.Game.Beatmaps.ControlPoints.ControlPointInfo(), new osu.Game.Beatmaps.BeatmapDifficulty());
        var entry = new HitObjectLifetimeEntry(note);
        var resultField = typeof(HitObjectLifetimeEntry).GetField("Result", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var selectedField = typeof(GameplaySampleTriggerSource).GetField("mostValidObject", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var select = typeof(GameplaySampleTriggerSource).GetMethod("GetMostValidObject", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var parentProperty = typeof(Drawable).GetProperty(nameof(Drawable.Parent))!;
        var clock = new FramedClock(new ManualClock { CurrentTime = 1100 });
        clock.ProcessFrame();
        using var container = new HitObjectContainer();
        container.Add(entry);
        using var column = new O2JamManiaColumn(0, false);
        using var nativeColumn = new Column(0, false);
        using var source = new GameplaySampleTriggerSource(container) { Clock = clock };
        selectedField.SetValue(source, entry);
        parentProperty.SetValue(source, column);

        Assert.That(select.Invoke(source, null), Is.SameAs(note));
        resultField.SetValue(entry, new JudgementResult(note, note.CreateJudgement()) { Type = result });
        Assert.That(O2JamResolvedKeySoundPatch.ShouldSuppress(note, new HitObjectLifetimeEntry(note)), Is.False);
        Assert.That(select.Invoke(source, null), Is.Null);

        parentProperty.SetValue(source, null);
        parentProperty.SetValue(source, nativeColumn);
        Assert.That(select.Invoke(source, null), Is.SameAs(note), "Native Mania retains its fallback.");

        parentProperty.SetValue(source, null);
        parentProperty.SetValue(source, column);
        resultField.SetValue(entry, null);
        Assert.That(select.Invoke(source, null), Is.SameAs(note), "Rewind restores the unresolved object.");
    }

    [TestCase(HitResult.Perfect)]
    [TestCase(HitResult.Miss)]
    public void ManiaScoreHoldStillUsesTheHeadEligibility(HitResult result)
    {
        var hold = new HoldNote
        {
            StartTime = 1000,
            Duration = 1000,
            Samples = [new O2JamHitSampleInfo(7, 100, 0)],
            NodeSamples = [[new O2JamHitSampleInfo(7, 100, 0)], []],
        };
        hold.ApplyDefaults(new osu.Game.Beatmaps.ControlPoints.ControlPointInfo(), new osu.Game.Beatmaps.BeatmapDifficulty());
        var entry = new HitObjectLifetimeEntry(hold);
        var head = new HitObjectLifetimeEntry(hold.Head);
        entry.NestedEntries.Add(head);
        Assert.That(O2JamResolvedKeySoundPatch.ShouldSuppress(hold, entry), Is.False);
        var resultField = typeof(HitObjectLifetimeEntry).GetField("Result", BindingFlags.Instance | BindingFlags.NonPublic)!;
        resultField.SetValue(head, new JudgementResult(hold.Head, hold.Head.CreateJudgement()) { Type = result });
        Assert.That(O2JamResolvedKeySoundPatch.ShouldSuppress(hold, entry), Is.True);
        resultField.SetValue(head, null);
        Assert.That(O2JamResolvedKeySoundPatch.ShouldSuppress(hold, entry), Is.False);
    }
}
