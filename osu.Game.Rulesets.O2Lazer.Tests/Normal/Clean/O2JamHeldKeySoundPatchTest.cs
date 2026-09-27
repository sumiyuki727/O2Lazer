using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Timing;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
public class O2JamHeldKeySoundPatchTest
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
        Assert.That(O2JamHeldKeySoundPatch.IsInstalled, Is.True);
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
        Assert.That(O2JamHeldKeySoundPatch.ShouldSuppress(hold, holdEntry), Is.False,
            "A first press must retain the native keysound path.");

        var result = new JudgementResult(hold.Head, hold.Head.CreateJudgement()) { Type = HitResult.Perfect };
        var resultField = typeof(HitObjectLifetimeEntry).GetField("Result", BindingFlags.Instance | BindingFlags.NonPublic)!;
        resultField.SetValue(headEntry, result);

        Assert.Multiple(() =>
        {
            Assert.That(O2JamHeldKeySoundPatch.ShouldSuppress(hold, holdEntry), Is.True,
                "The parent keeps its head sample after release, but must not sound again.");
            Assert.That(O2JamHeldKeySoundPatch.ShouldSuppress(hold.Head, holdEntry), Is.False,
                "Only Mania's fallback to the parent is suppressed.");
            Assert.That(O2JamHeldKeySoundPatch.ShouldSuppress(hold, new HitObjectLifetimeEntry(hold)), Is.False,
                "Another lifetime entry must not borrow the finished head state.");
        });

        resultField.SetValue(headEntry, null);
        Assert.That(O2JamHeldKeySoundPatch.ShouldSuppress(hold, holdEntry), Is.False,
            "Rewinding before the head must restore its original keysound eligibility.");
    }
}
