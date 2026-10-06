using System;
using System.Linq;
using NUnit.Framework;
using O2Jam.Core;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Scoring;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamRateModTest
{
    [TestCase(typeof(O2JamModHalfTime), typeof(ManiaModHalfTime))]
    [TestCase(typeof(O2JamModDaycore), typeof(ManiaModDaycore))]
    [TestCase(typeof(O2JamModDoubleTime), typeof(ManiaModDoubleTime))]
    [TestCase(typeof(O2JamModNightcore), typeof(ManiaModNightcore))]
    public void SettingsMenuMatchesMania(Type modType, Type maniaType)
    {
        var mod = (Mod)Activator.CreateInstance(modType)!;
        var mania = (Mod)Activator.CreateInstance(maniaType)!;

        Assert.That(mod.GetOrderedSettingsSourceProperties().Select(setting =>
                (setting.Item2.Name, setting.Item1.Label.ToString(), setting.Item1.Description.ToString(), setting.Item1.SettingControlType)),
            Is.EqualTo(mania.GetOrderedSettingsSourceProperties().Select(setting =>
                (setting.Item2.Name, setting.Item1.Label.ToString(), setting.Item1.Description.ToString(), setting.Item1.SettingControlType))));
    }

    [TestCase(typeof(O2JamModHalfTime), 1, 0.75)]
    [TestCase(typeof(O2JamModDaycore), 0.75, 1)]
    [TestCase(typeof(O2JamModDoubleTime), 1, 1.5)]
    [TestCase(typeof(O2JamModNightcore), 1.5, 1)]
    public void DefaultPitchPolicyReachesGameplayHitSounds(Type modType, double frequency, double tempo)
    {
        var mod = (ModRateAdjust)Activator.CreateInstance(modType)!;
        var adjustments = new O2JamHitSoundRateAdjustments();
        var hitSound = new AudioAdjustments();
        adjustments.Configure([mod]);
        adjustments.Bind(hitSound);

        Assert.Multiple(() =>
        {
            Assert.That(hitSound.AggregateFrequency.Value, Is.EqualTo(frequency).Within(0.000001));
            Assert.That(hitSound.AggregateTempo.Value, Is.EqualTo(tempo).Within(0.000001));
            Assert.That(hitSound.AggregateFrequency.Value * hitSound.AggregateTempo.Value,
                Is.EqualTo(mod.SpeedChange.Value).Within(0.000001));
        });
    }

    [TestCase(typeof(O2JamModHalfTime), 0.8)]
    [TestCase(typeof(O2JamModDoubleTime), 1.8)]
    public void AdjustPitchSettingUpdatesGameplayHitSounds(Type modType, double speed)
    {
        var mod = (ModRateAdjust)Activator.CreateInstance(modType)!;
        var adjustments = new O2JamHitSoundRateAdjustments();
        var hitSound = new AudioAdjustments();
        adjustments.Configure([mod]);
        adjustments.Bind(hitSound);
        mod.SpeedChange.Value = speed;

        Assert.Multiple(() =>
        {
            Assert.That(hitSound.AggregateFrequency.Value, Is.EqualTo(1));
            Assert.That(hitSound.AggregateTempo.Value, Is.EqualTo(speed));
        });

        switch (mod)
        {
            case O2JamModHalfTime halfTime:
                halfTime.AdjustPitch.Value = true;
                break;

            case O2JamModDoubleTime doubleTime:
                doubleTime.AdjustPitch.Value = true;
                break;
        }

        Assert.Multiple(() =>
        {
            Assert.That(hitSound.AggregateFrequency.Value, Is.EqualTo(speed));
            Assert.That(hitSound.AggregateTempo.Value, Is.EqualTo(1));
        });
    }

    [TestCase(typeof(O2JamModDaycore), 0.6, 0.75, 0.8)]
    [TestCase(typeof(O2JamModNightcore), 1.8, 1.5, 1.2)]
    public void CorePitchStaysAtTheNativeDefaultAtCustomSpeed(Type modType, double speed, double frequency, double tempo)
    {
        var mod = (ModRateAdjust)Activator.CreateInstance(modType)!;
        var adjustments = new O2JamHitSoundRateAdjustments();
        var hitSound = new AudioAdjustments();
        adjustments.Configure([mod]);
        adjustments.Bind(hitSound);
        mod.SpeedChange.Value = speed;

        Assert.Multiple(() =>
        {
            Assert.That(hitSound.AggregateFrequency.Value, Is.EqualTo(frequency).Within(0.000001));
            Assert.That(hitSound.AggregateTempo.Value, Is.EqualTo(tempo).Within(0.000001));
            Assert.That(hitSound.AggregateFrequency.Value * hitSound.AggregateTempo.Value, Is.EqualTo(speed).Within(0.000001));
        });
    }

    [TestCase(typeof(O2JamModWindUp), 1.25)]
    [TestCase(typeof(O2JamModWindDown), 0.8)]
    [TestCase(typeof(O2JamModAdaptiveSpeed), 1.3)]
    public void DynamicRateAndPitchPolicyReachGameplayHitSounds(Type modType, double speed)
    {
        var mod = (Mod)Activator.CreateInstance(modType)!;
        var adjustments = new O2JamHitSoundRateAdjustments();
        var hitSound = new AudioAdjustments();
        var speedChange = mod switch
        {
            ModTimeRamp ramp => ramp.SpeedChange,
            ModAdaptiveSpeed adaptive => adaptive.SpeedChange,
            _ => throw new InvalidOperationException(),
        };
        var adjustPitch = mod switch
        {
            ModTimeRamp ramp => ramp.AdjustPitch,
            ModAdaptiveSpeed adaptive => adaptive.AdjustPitch,
            _ => throw new InvalidOperationException(),
        };
        adjustments.Configure([mod]);
        adjustments.Bind(hitSound);
        speedChange.Value = speed;

        Assert.Multiple(() =>
        {
            Assert.That(hitSound.AggregateFrequency.Value, Is.EqualTo(speed));
            Assert.That(hitSound.AggregateTempo.Value, Is.EqualTo(1));
        });

        adjustPitch.Value = false;

        Assert.Multiple(() =>
        {
            Assert.That(hitSound.AggregateFrequency.Value, Is.EqualTo(1));
            Assert.That(hitSound.AggregateTempo.Value, Is.EqualTo(speed));
        });
    }

    [Test]
    public void PauseFreezesAndRestoresGameplayHitSounds()
    {
        var mod = new O2JamModDoubleTime();
        mod.AdjustPitch.Value = true;
        var paused = new BindableBool();
        var adjustments = new O2JamHitSoundRateAdjustments();
        var hitSound = new AudioAdjustments();
        adjustments.Configure([mod]);
        adjustments.BindPlaybackDisabled(paused);
        adjustments.Bind(hitSound);

        Assert.That(hitSound.AggregateFrequency.Value, Is.EqualTo(1.5));

        paused.Value = true;

        Assert.Multiple(() =>
        {
            Assert.That(hitSound.AggregateFrequency.Value, Is.Zero);
            Assert.That(hitSound.AggregateTempo.Value, Is.EqualTo(1));
        });

        paused.Value = false;

        Assert.Multiple(() =>
        {
            Assert.That(hitSound.AggregateFrequency.Value, Is.EqualTo(1.5));
            Assert.That(hitSound.AggregateTempo.Value, Is.EqualTo(1));
        });
    }

    [TestCase(typeof(O2JamModHalfTime), typeof(ManiaModHalfTime), 0.75)]
    [TestCase(typeof(O2JamModHalfTime), typeof(ManiaModHalfTime), 0.8)]
    [TestCase(typeof(O2JamModDaycore), typeof(ManiaModDaycore), 0.75)]
    [TestCase(typeof(O2JamModDaycore), typeof(ManiaModDaycore), 0.8)]
    [TestCase(typeof(O2JamModDoubleTime), typeof(ManiaModDoubleTime), 1.5)]
    [TestCase(typeof(O2JamModDoubleTime), typeof(ManiaModDoubleTime), 1.8)]
    [TestCase(typeof(O2JamModNightcore), typeof(ManiaModNightcore), 1.5)]
    [TestCase(typeof(O2JamModNightcore), typeof(ManiaModNightcore), 1.8)]
    public void ManiaScoreWindowsMatchNativeRateMods(Type modType, Type nativeModType, double speed)
    {
        string[] extras = ["none", "HR", "EZ", "CL"];
        bool[] orders = [false, true];
        foreach (var extra in extras)
        foreach (var rateFirst in orders)
        {
            var ruleset = new O2LazerRuleset();
            var source = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
            source.BeatmapInfo.Ruleset = ruleset.RulesetInfo;
            source.Difficulty.CircleSize = 7;
            source.HitObjects.Add(new O2JamNote { StartTime = 1000 });
            source.HitObjects.Add(new O2JamHoldNote { StartTime = 2000, Duration = 1000 });
            var rateMod = (ModRateAdjust)Activator.CreateInstance(modType)!;
            rateMod.SpeedChange.Value = speed;
            Mod[] mods = [new O2JamModManiaScore(), rateMod];
            if (extra != "none")
                mods = [.. mods, extra switch { "HR" => new O2JamModHardRock(), "EZ" => new O2JamModEasy(), _ => new O2JamModClassic() }];
            if (rateFirst)
                Array.Reverse(mods);
            var playable = new FlatWorkingBeatmap(source).GetPlayableBeatmap(ruleset.RulesetInfo, mods, default);

            var mania = new ManiaRuleset();
            var nativeSource = new ManiaBeatmap(new StageDefinition(7));
            nativeSource.BeatmapInfo.Ruleset = mania.RulesetInfo;
            nativeSource.Difficulty.CircleSize = 7;
            nativeSource.Difficulty.OverallDifficulty = 7;
            nativeSource.Difficulty.DrainRate = 7;
            nativeSource.HitObjects.Add(new Note { StartTime = 1000 });
            nativeSource.HitObjects.Add(new HoldNote { StartTime = 2000, Duration = 1000 });
            var nativeRateMod = (ModRateAdjust)Activator.CreateInstance(nativeModType)!;
            nativeRateMod.SpeedChange.Value = speed;
            Mod[] nativeMods = [nativeRateMod];
            if (extra != "none")
                nativeMods = [.. nativeMods, extra switch { "HR" => new ManiaModHardRock(), "EZ" => new ManiaModEasy(), _ => new ManiaModClassic() }];
            if (rateFirst)
                Array.Reverse(nativeMods);
            var expected = new FlatWorkingBeatmap(nativeSource).GetPlayableBeatmap(mania.RulesetInfo, nativeMods, default);
            if (extra == "CL")
            {
                // Native Classic treats the O2Jam source as converted. Preserve that same
                // policy in the reference, independently of the rate adapter under test.
                expected.BeatmapInfo.Ruleset = ruleset.RulesetInfo;
                new ManiaModClassic().ApplyToBeatmap(expected);
            }
            var actualHold = (HoldNote)playable.HitObjects[1];
            var expectedHold = (HoldNote)expected.HitObjects[1];
            ManiaHitObject[] actualEndpoints = [(Note)playable.HitObjects[0], actualHold.Head, actualHold.Tail];
            ManiaHitObject[] expectedEndpoints = [(Note)expected.HitObjects[0], expectedHold.Head, expectedHold.Tail];

            for (var i = 0; i < actualEndpoints.Length; i++)
            {
                var actualWindows = (ManiaHitWindows)actualEndpoints[i].HitWindows;
                var expectedWindows = (ManiaHitWindows)expectedEndpoints[i].HitWindows;
                var context = $"{modType.Name}, {speed}, {extra}, rateFirst={rateFirst}, endpoint={i}";
                Assert.That(actualWindows.SpeedMultiplier, Is.EqualTo(expectedWindows.SpeedMultiplier), context);
                Assert.That(actualWindows.GetAllAvailableWindows(), Is.EqualTo(expectedWindows.GetAllAvailableWindows()), context);
                Assert.That(actualEndpoints[i].StartTime, Is.EqualTo(expectedEndpoints[i].StartTime), context);
            }
        }
    }

    [TestCase(typeof(O2JamModHalfTime), 0.8)]
    [TestCase(typeof(O2JamModDaycore), 0.8)]
    [TestCase(typeof(O2JamModDoubleTime), 1.8)]
    [TestCase(typeof(O2JamModNightcore), 1.8)]
    public void RateModsCreatePlayableO2JamBeatmapsWithoutChangingChartTimes(Type modType, double speed)
    {
        var ruleset = new O2LazerRuleset();
        var source = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        source.BeatmapInfo.Ruleset = ruleset.RulesetInfo;
        source.BeatmapInfo.Hash = "rate-mod-chart";
        source.HitObjects.Add(new O2JamNote { StartTime = 1000, ChartPosition = source.TimingMap.PositionAt(1000) });
        source.HitObjects.Add(new O2JamHoldNote
        {
            StartTime = 2000,
            Duration = 1000,
            TimingMap = source.TimingMap,
            HeadChartPosition = source.TimingMap.PositionAt(2000),
            TailChartPosition = source.TimingMap.PositionAt(3000),
        });
        var mod = (ModRateAdjust)Activator.CreateInstance(modType)!;
        mod.SpeedChange.Value = speed;

        var playable = new FlatWorkingBeatmap(source).GetPlayableBeatmap(ruleset.RulesetInfo, [mod], default);
        var baseline = new FlatWorkingBeatmap(source).GetPlayableBeatmap(ruleset.RulesetInfo, [], default);
        var hold = (O2JamHoldNote)playable.HitObjects[1];
        var baselineHold = (O2JamHoldNote)baseline.HitObjects[1];
        ManiaHitObject[] actualEndpoints = [(O2JamNote)playable.HitObjects[0], hold.Head, hold.Tail];
        ManiaHitObject[] baselineEndpoints = [(O2JamNote)baseline.HitObjects[0], baselineHold.Head, baselineHold.Tail];
        for (var i = 0; i < actualEndpoints.Length; i++)
        {
            Assert.That(actualEndpoints[i].HitWindows, Is.TypeOf<O2JamFrameworkHitWindows>());
            Assert.That(actualEndpoints[i].HitWindows.GetAllAvailableWindows(), Is.EqualTo(baselineEndpoints[i].HitWindows.GetAllAvailableWindows()));
            Assert.That(actualEndpoints[i].StartTime, Is.EqualTo(baselineEndpoints[i].StartTime));
        }

        Assert.Multiple(() =>
        {
            Assert.That(playable.HitObjects[0].StartTime, Is.EqualTo(1000));
            Assert.That(((IApplicableToRate)mod).ApplyToRate(1000, 1), Is.EqualTo(speed));
        });
    }

    [TestCase(typeof(O2JamModHalfTime), 0.8, true)]
    [TestCase(typeof(O2JamModDaycore), 0.8, false)]
    [TestCase(typeof(O2JamModDoubleTime), 1.8, true)]
    [TestCase(typeof(O2JamModNightcore), 1.8, false)]
    public void SettingsSurviveReplayArchive(Type modType, double speed, bool adjustPitch)
    {
        var ruleset = new O2LazerRuleset();
        var mod = (ModRateAdjust)Activator.CreateInstance(modType)!;
        mod.SpeedChange.Value = speed;
        if (mod is ModHalfTime halfTime)
            halfTime.AdjustPitch.Value = adjustPitch;
        if (mod is ModDoubleTime doubleTime)
            doubleTime.AdjustPitch.Value = adjustPitch;
        var score = new Score
        {
            ScoreInfo = new ScoreInfo(new BeatmapInfo(ruleset.RulesetInfo) { Hash = "rate-mod-replay" }, ruleset.RulesetInfo)
            {
                Mods = [mod],
            },
        };
        score.Replay.Frames.Add(new O2JamReplayFrame(0));

        Assert.That(O2JamReplayArchive.TryReadMetadata(O2JamReplayArchive.Create(score), out var metadata), Is.True);
        var restored = new ScoreInfo(ruleset: ruleset.RulesetInfo) { ModsJson = metadata.ModsJson };
        var restoredMod = restored.Mods.OfType<ModRateAdjust>().Single();

        Assert.That(restoredMod.GetType(), Is.EqualTo(modType));
        Assert.That(restoredMod.SpeedChange.Value, Is.EqualTo(speed));
        if (restoredMod is ModHalfTime restoredHalfTime)
            Assert.That(restoredHalfTime.AdjustPitch.Value, Is.EqualTo(adjustPitch));
        if (restoredMod is ModDoubleTime restoredDoubleTime)
            Assert.That(restoredDoubleTime.AdjustPitch.Value, Is.EqualTo(adjustPitch));
    }

    [Test]
    public void NightcoreKeepsTheNativeBeatOverlay()
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        using var drawable = new O2JamDrawableRuleset(ruleset, beatmap);
        IApplicableToDrawableRuleset<ManiaHitObject> nightcore = new O2JamModNightcore();

        nightcore.ApplyToDrawableRuleset(drawable);

        Assert.That(drawable.Overlays.Children, Has.One.TypeOf<ModNightcore<ManiaHitObject>.NightcoreBeatContainer>());
    }
}
