using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NUnit.Framework;
using O2Jam.Core;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Graphics;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mania.Scoring;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play.HUD.HitErrorMeters;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
public partial class O2JamHitErrorMeterTest
{
    [Test]
    public void TemporaryHitErrorListenerHasBeenRemoved()
    {
        var probe = typeof(O2LazerRuleset).Assembly.GetType("osu.Game.Rulesets.O2Lazer.UI.O2JamHitErrorMeterProbe");
        Assert.That(probe, Is.Null, "The temporary listener must not be included in the ruleset.");
    }

    [Test]
    public void CoolDisplayUsesNativeManiaOd7GreatWidthAndO2JamColourRatios()
    {
        var mania = new ManiaHitWindows();
        mania.SetDifficulty(7);
        var display = O2JamHitErrorDisplayWindows.Instance;
        var outer = display.WindowFor(HitResult.Ok);
        Assert.That(display.WindowFor(HitResult.Perfect) - display.EarlyWindowFor(HitResult.Perfect),
            Is.EqualTo(O2JamHitErrorDisplayWindows.UnitsPerTick));
        Assert.That(display.EarlyWindowFor(HitResult.Perfect), Is.EqualTo(mania.WindowFor(HitResult.Great)));
        Assert.That(outer, Is.EqualTo(mania.WindowFor(HitResult.Great) * 26 / 6));
        Assert.That(display.WindowFor(HitResult.Perfect) / outer, Is.EqualTo(7d / 26).Within(1e-12));
        Assert.That(display.WindowFor(HitResult.Good) / outer, Is.EqualTo(19d / 26).Within(1e-12));
    }

    [Test]
    public void TickProjectionRetainsDirectionAcrossBpmChanges(
        [Values(0.98, 1.02)] double targetPosition,
        [Values(-20, -8, 0, 8, 20)] double offsetTicks,
        [Values(O2JamEndpointKind.Tap, O2JamEndpointKind.HoldHead, O2JamEndpointKind.HoldRelease)] O2JamEndpointKind endpoint)
    {
        var map = new O2JamTimingMap(120, [new O2JamBpmEvent(1, 240)]);
        IO2JamJudgedObject judgedObject = endpoint switch
        {
            O2JamEndpointKind.Tap => new O2JamNote(),
            O2JamEndpointKind.HoldHead => new O2JamHoldHead(),
            _ => new O2JamHoldTail(),
        };
        judgedObject.ChartPosition = targetPosition;
        judgedObject.TimingMap = map;
        var note = (Note)judgedObject;
        note.StartTime = map.TimeAt(targetPosition);
        note.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty());
        var result = createResult(note, map.TimeAt(targetPosition + O2JamTimingMap.TicksToPosition(offsetTicks)) - note.StartTime);
        var originalOffset = result.TimeOffset;
        var originalWindows = note.HitWindows;

        Assert.That(O2JamHitErrorProjection.OffsetTicks(result), Is.EqualTo(offsetTicks).Within(1e-10));
        Assert.That(result.TimeOffset, Is.EqualTo(originalOffset));
        Assert.That(note.HitWindows, Is.SameAs(originalWindows));
        Assert.That(note.HitWindows, Is.TypeOf<O2JamFrameworkHitWindows>());
    }

    [Test]
    public void RateIsAlreadyRepresentedByNativeChartOffset(
        [Values(60, 120, 240)] double bpm, [Values(0.75, 1, 1.5)] double rate)
    {
        var map = new O2JamTimingMap(bpm);
        var note = new O2JamNote { TimingMap = map, ChartPosition = 1, StartTime = map.TimeAt(1) };
        note.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty());
        var inputTime = map.TimeAt(1 + O2JamTimingMap.TicksToPosition(20));
        var result = createResult(note, inputTime - note.StartTime);
        AccessTools.PropertySetter(typeof(JudgementResult), nameof(JudgementResult.GameplayRate)).Invoke(result, [rate]);
        var offset = result.TimeOffset;

        Assert.That(O2JamHitErrorProjection.OffsetTicks(result), Is.EqualTo(20).Within(1e-10));
        HitEvent[] events = [new(0, rate, HitResult.Perfect, note, null, null), new(offset, rate, HitResult.Ok, note, null, null)];
        Assert.That(events.CalculateUnstableRate()!.Result, Is.EqualTo(offset / rate * 5).Within(1e-9));
        Assert.That(events[1].TimeOffset, Is.EqualTo(offset));
    }

    [Test]
    public void LoadedNativeMetersUseProjectedMarkersAndPreserveNativeMode(
        [Values(false, true)] bool argon, [Values(MeterProfile.O2Jam, MeterProfile.ManiaScore, MeterProfile.Mania)] MeterProfile mode, [Values(-20, 20)] double ticks,
        [Values(0.75, 1, 1.5)] double rate)
    {
        var messages = new List<string>();
        void log(LogEntry entry) => messages.Add(entry.Message + Environment.NewLine + entry.Exception);
        Logger.NewEntry += log;
        bool installed;
        try
        {
            installed = O2JamHitErrorMeterPatch.InstallOnce();
            Logger.Flush();
        }
        finally
        {
            Logger.NewEntry -= log;
        }
        Assert.That(installed, Is.True, string.Join(Environment.NewLine, messages));
        using var host = new TestRunHeadlessGameHost($"O2JamHitErrorMeter-{Guid.NewGuid():N}");
        var game = new MeterGame(argon, mode, ticks, rate: rate);
        host.Run(game);
        if (game.Failure != null)
            throw game.Failure;
        Assert.That(game.Completed, Is.True);
    }

    [Test]
    public void ContinuousMarkersAgreeWithAsymmetricCoreAtColourBoundaries(
        [Values(false, true)] bool argon, [Values(-25, -6, 6.5, 7, 18.5, 19, 25.5)] double ticks)
    {
        var map = new O2JamTimingMap(160);
        var accuracy = new O2JamJudgementEngine(new O2JamPositionClock(map)).Judge(1,
            map.TimeAt(1 + O2JamTimingMap.TicksToPosition(ticks)), O2JamEndpointKind.Tap).Accuracy;
        Assert.That(O2JamHitErrorMeterPatch.InstallOnce(), Is.True);
        using var host = new TestRunHeadlessGameHost($"O2JamAsymmetricMeter-{Guid.NewGuid():N}");
        var game = new MeterGame(argon, MeterProfile.O2Jam, ticks, hitResult: O2JamResultMapper.ToFramework(accuracy));
        host.Run(game);
        if (game.Failure != null)
            throw game.Failure;
        Assert.That(game.Completed, Is.True);
    }

    [Test]
    public void LoadedBadColourUsesNativeMania50OnlyForO2Jam(
        [Values(false, true)] bool argon,
        [Values(MeterProfile.O2Jam, MeterProfile.ManiaScore, MeterProfile.Mania)] MeterProfile mode)
    {
        Assert.That(O2JamHitErrorMeterPatch.InstallOnce(), Is.True);
        using var host = new TestRunHeadlessGameHost($"O2JamBadColour-{Guid.NewGuid():N}");
        var game = new MeterGame(argon, mode, 20, hitResult: HitResult.Ok);
        host.Run(game);
        if (game.Failure != null)
            throw game.Failure;
        Assert.That(game.Completed, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingRulesetKeepsNativeEmptyWindows(bool argon)
    {
        Assert.That(O2JamHitErrorMeterPatch.InstallOnce(), Is.True);
        using HitErrorMeter meter = argon ? new BarHitErrorMeter() : new LegacyBarHitErrorMeter();
        AccessTools.Method(typeof(HitErrorMeter), "load").Invoke(meter, [null]);
        Assert.That(AccessTools.PropertyGetter(typeof(HitErrorMeter), "HitWindows").Invoke(meter, null), Is.SameAs(HitWindows.Empty));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NonPositionalAndCustomMetersKeepTheirOriginalWindows(bool custom)
    {
        Assert.That(O2JamHitErrorMeterPatch.InstallOnce(), Is.True);
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        var note = new O2JamNote();
        note.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        beatmap.HitObjects.Add(note);
        using var drawable = new O2JamDrawableRuleset(new O2LazerRuleset(), beatmap);
        using HitErrorMeter meter = custom ? new CustomLegacyMeter() : new ColourHitErrorMeter();
        AccessTools.Method(typeof(HitErrorMeter), "load").Invoke(meter, [drawable]);
        Assert.That(AccessTools.PropertyGetter(typeof(HitErrorMeter), "HitWindows").Invoke(meter, null), Is.SameAs(note.HitWindows));
    }

    public enum MeterProfile
    {
        O2Jam,
        ManiaScore,
        Mania,
    }

    private partial class CustomLegacyMeter : LegacyBarHitErrorMeter
    {
    }

    private static JudgementResult createResult(Note note, double offset, HitResult type = HitResult.Ok)
    {
        var result = new JudgementResult(note, note.CreateJudgement()) { Type = type };
        AccessTools.PropertySetter(typeof(JudgementResult), nameof(JudgementResult.TimeOffset)).Invoke(result, [offset]);
        return result;
    }

    private partial class MeterGame : Framework.Game
    {
        private readonly bool argon;
        private readonly bool usesNativeWindows;
        private readonly DrawableRuleset drawableRuleset;
        private readonly O2JamTimingMap timingMap;
        private readonly double ticks;
        private readonly double playbackRate;
        private readonly HitResult hitResult;
        private readonly IApplicableToHitObject? rateMod;
        private readonly O2JamScoreProcessor processor = new(new O2LazerRuleset());
        private readonly HitErrorMeter meter;
        private readonly Note note;
        private readonly ManualClock clock = new();
        private int phase;
        private int frames;
        private float originalWidth;
        private float originalHeight;
        private HitWindows originalWindows = null!;
        private JudgementResult result = null!;
        public Exception? Failure;
        public bool Completed;

        public MeterGame(bool argon, MeterProfile mode, double ticks, double rate = 1, HitResult hitResult = HitResult.Perfect)
        {
            this.argon = argon;
            usesNativeWindows = mode != MeterProfile.O2Jam;
            this.ticks = ticks;
            playbackRate = rate;
            this.hitResult = hitResult;
            var map = new O2JamTimingMap(120, [new O2JamBpmEvent(1, 240)]);
            timingMap = map;
            var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, map);
            beatmap.Difficulty.OverallDifficulty = 7;
            note = usesNativeWindows ? new Note { StartTime = map.TimeAt(0.98) }
                : new O2JamNote { TimingMap = map, ChartPosition = 0.98, StartTime = map.TimeAt(0.98) };
            note.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
            if (rate != 1)
            {
                ModRateAdjust mod = mode == MeterProfile.Mania
                    ? rate < 1 ? new ManiaModHalfTime() : new ManiaModDoubleTime()
                    : rate < 1 ? new O2JamModHalfTime() : new O2JamModDoubleTime();
                mod.SpeedChange.Value = rate;
                rateMod = (IApplicableToHitObject)mod;
                rateMod.ApplyToHitObject(note);
            }
            beatmap.HitObjects.Add(note);
            Mod[] mods = mode == MeterProfile.ManiaScore ? [new O2JamModManiaScore()] : [];
            drawableRuleset = mode == MeterProfile.Mania ? new DrawableManiaRuleset(new ManiaRuleset(), beatmap)
                : new O2JamDrawableRuleset(new O2LazerRuleset(), beatmap, mods);
            meter = argon ? new BarHitErrorMeter() : new LegacyBarHitErrorMeter();
        }

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
            dependencies.CacheAs<DrawableRuleset>(drawableRuleset);
            dependencies.CacheAs<ScoreProcessor>(processor);
            dependencies.Cache(new OsuColour());
            return dependencies;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Add(new Container { Clock = new FramedClock(clock), RelativeSizeAxes = Axes.Both, Child = meter });
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();
            if (Failure != null || Completed)
                return;
            try
            {
                if (++frames > 100)
                    throw new InvalidOperationException("Native hit error meter did not complete its load.");
                if (!meter.IsLoaded)
                    return;

                switch (phase++)
                {
                    case 0:
                        break;
                    case 1:
                        originalWidth = meter.DrawWidth;
                        originalHeight = meter.DrawHeight;
                        originalWindows = note.HitWindows;
                        var displayWindows = (HitWindows)AccessTools.PropertyGetter(typeof(HitErrorMeter), "HitWindows").Invoke(meter, null)!;
                        Assert.That(displayWindows, usesNativeWindows ? Is.SameAs(originalWindows) : Is.TypeOf<O2JamHitErrorDisplayWindows>());
                        var map = timingMap;
                        result = createResult(note, map.TimeAt(0.98 + O2JamTimingMap.TicksToPosition(ticks)) - note.StartTime,
                            hitResult);
                        // Marker colour follows the final result, including a BAD rescued to COOL.
                        AccessTools.Method(meter.GetType(), "OnNewJudgement").Invoke(meter, [result]);
                        break;
                    case 2:
                        verifyMarker();
                        break;
                    case 3:
                        var containerName = argon ? "judgementsContainer" : "judgementContainer";
                        var container = (Container)AccessTools.Field(meter.GetType(), containerName).GetValue(meter)!;
                        Assert.That(container, Is.Empty, "Native seek clearing must expire pooled markers.");
                        var next = usesNativeWindows ? new Note { StartTime = timingMap.TimeAt(1.5) }
                            : new O2JamNote { TimingMap = timingMap, ChartPosition = 1.5, StartTime = timingMap.TimeAt(1.5) };
                        next.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty { OverallDifficulty = 7 });
                        rateMod?.ApplyToHitObject(next);
                        result = createResult(next, timingMap.TimeAt(1.5 + O2JamTimingMap.TicksToPosition(ticks)) - next.StartTime,
                            hitResult);
                        AccessTools.Method(meter.GetType(), "OnNewJudgement").Invoke(meter, [result]);
                        break;
                    case 4:
                        // At BPM240 the same tick error must retain its marker position/width.
                        verifyMarker(false);
                        break;
                    case 5:
                        Completed = true;
                        Exit();
                        break;
                }
            }
            catch (Exception exception)
            {
                Failure = exception;
                Exit();
            }
        }

        private void verifyMarker(bool verifyAverage = true)
        {
            var container = (Container)AccessTools.Field(meter.GetType(), argon ? "judgementsContainer" : "judgementContainer").GetValue(meter)!;
            var marker = container.Single();
            var maxWindow = (double)AccessTools.Field(meter.GetType(), "maxHitWindow").GetValue(meter)!;
            var nativeWindows = new ManiaHitWindows { SpeedMultiplier = playbackRate };
            nativeWindows.SetDifficulty(7);
            var expectedOuter = usesNativeWindows ? nativeWindows.WindowFor(HitResult.Meh) : O2JamHitErrorDisplayWindows.Instance.WindowFor(HitResult.Ok);
            Assert.That(maxWindow, Is.EqualTo(expectedOuter).Within(1e-9));
            if (!argon)
                Assert.That(originalWidth, Is.EqualTo(expectedOuter * LegacySkin.STABLE_MAGIC_SCALE_FACTOR).Within(0.001));
            else
            {
                var root = (Drawable)AccessTools.PropertyGetter(typeof(CompositeDrawable), "InternalChild").Invoke(meter, null)!;
                var nativeOd7 = new ManiaHitWindows();
                nativeOd7.SetDifficulty(7);
                var lengthMultiplier = usesNativeWindows ? 1 : nativeOd7.WindowFor(HitResult.Great) * 26 / 6 / nativeOd7.WindowFor(HitResult.Meh);
                Assert.That(root.Height, Is.EqualTo(200 * lengthMultiplier).Within(0.001));
            }
            var offset = usesNativeWindows ? result.TimeOffset : ticks * O2JamHitErrorDisplayWindows.UnitsPerTick;
            var expectedPosition = argon ? Math.Clamp((offset / maxWindow + 1) / 2, 0, 1) : Math.Clamp(offset / maxWindow / 2, -0.5, 0.5);
            Assert.That(argon ? marker.Y : marker.X, Is.EqualTo(expectedPosition).Within(1e-6));
            var colourResult = !usesNativeWindows && result.Type == HitResult.Ok ? HitResult.Meh : result.Type;
            Assert.That(marker.Colour.AverageColour.SRGB, Is.EqualTo(new OsuColour().ForHitResult(colourResult)));
            Assert.That(result.Type, Is.EqualTo(hitResult), "Display colour adaptation must not rewrite the judgement.");
            verifyColourRegions();
            if (verifyAverage && argon)
                Assert.That((double)AccessTools.Field(meter.GetType(), "floatingAverage").GetValue(meter)!, Is.EqualTo(offset * 0.1).Within(1e-8));
            else if (verifyAverage)
                Assert.That((float)AccessTools.Field(meter.GetType(), "floatingError").GetValue(meter)!, Is.EqualTo(expectedPosition * 0.2).Within(1e-6));
            Assert.That(note.HitWindows, Is.SameAs(originalWindows));
            Assert.That(meter.DrawWidth, Is.EqualTo(originalWidth));
            Assert.That(meter.DrawHeight, Is.EqualTo(originalHeight));
            meter.Clear();
        }

        private void verifyColourRegions()
        {
            var windows = (HitWindows)AccessTools.PropertyGetter(typeof(HitErrorMeter), "HitWindows").Invoke(meter, null)!;
            var regions = windows.GetAllAvailableWindows().Where(window => window.result.IsHit()).OrderByDescending(window => window.length).ToArray();
            Container[] groups = argon
                ? [(Container)AccessTools.Field(typeof(BarHitErrorMeter), "colourBarsEarly").GetValue(meter)!,
                    (Container)AccessTools.Field(typeof(BarHitErrorMeter), "colourBarsLate").GetValue(meter)!]
                : [(Container)((IReadOnlyList<Drawable>)AccessTools.PropertyGetter(typeof(CompositeDrawable), "InternalChildren").Invoke(meter, null)!)[1]];
            for (var side = 0; side < groups.Length; side++)
            {
                var group = groups[side];
                Assert.That(group.Count, Is.EqualTo(regions.Length));
                for (var i = 0; i < regions.Length; i++)
                {
                    var region = group.Children[i];
                    var late = regions[i].length;
                    var early = usesNativeWindows ? late : ((O2JamHitErrorDisplayWindows)windows).EarlyWindowFor(regions[i].result);
                    if (!argon)
                    {
                        var scale = LegacySkin.STABLE_MAGIC_SCALE_FACTOR / 2;
                        Assert.That(region.X - region.Width / 2, Is.EqualTo(-early * scale).Within(0.001));
                        Assert.That(region.X + region.Width / 2, Is.EqualTo(late * scale).Within(0.001));
                    }
                    else
                    {
                        var maximum = regions[0].length;
                        var extent = side == 0 ? early : late;
                        Assert.That(region.Height, Is.EqualTo(extent / maximum).Within(1e-6));
                    }
                    if (region is Container gradient)
                        region = gradient.Children[0];
                    var colourResult = !usesNativeWindows && regions[i].result == HitResult.Ok ? HitResult.Meh : regions[i].result;
                    Assert.That(region.Colour.AverageColour.SRGB, Is.EqualTo(new OsuColour().ForHitResult(colourResult)));
                }
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            drawableRuleset.Dispose();
            processor.Dispose();
        }
    }
}
