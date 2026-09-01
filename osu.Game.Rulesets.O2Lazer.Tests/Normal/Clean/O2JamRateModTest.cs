using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Audio;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Core;
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
        var mod = (ModRateAdjust)Activator.CreateInstance(modType)!;
        mod.SpeedChange.Value = speed;

        var playable = new FlatWorkingBeatmap(source).GetPlayableBeatmap(ruleset.RulesetInfo, [mod], default);

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
