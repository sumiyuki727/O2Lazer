using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using O2Jam.Formats.Ojn;
using osu.Framework.Extensions;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Integration.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Integration.Beatmaps.Transforms;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamRandomTest
{
    // Fixed vectors from the two clients' CRT recurrence and full-range swap instructions.
    [TestCase(0, new[] { 6, 0, 1, 5, 2, 3, 4 })]
    [TestCase(1, new[] { 6, 1, 3, 4, 5, 2, 0 })]
    [TestCase(12345, new[] { 2, 5, 6, 1, 0, 4, 3 })]
    [TestCase(-1, new[] { 2, 3, 5, 6, 1, 0, 4 })]
    [TestCase(int.MaxValue, new[] { 2, 3, 5, 6, 1, 0, 4 })]
    public void OriginalShuffleMatchesKnownSeedVectors(int seed, int[] expected) =>
        Assert.That(O2JamColumnRandomizer.Shuffle(seed), Is.EqualTo(expected));

    [Test]
    public void PanicComposesInitialShuffleAndConsumesEmptyMeasures()
    {
        O2JamRandomObject[] objects = [new(0, new(2, 0)), new(0, new(0, 0))];
        Assert.That(O2JamColumnRandomizer.Panic(objects, [192, 192, 192], 1), Is.EqualTo(new[] { 1, 4 }));
    }

    [TestCase(192, 187, new[] { 4, 4, 6, 1 })]
    [TestCase(192, 186, new[] { 4, 6, 1, 6 })]
    [TestCase(192, 192, new[] { 4, 6, 1, 6 })]
    [TestCase(96, 91, new[] { 4, 4, 6, 1 })]
    [TestCase(96, 90, new[] { 4, 6, 1, 6 })]
    public void PanicProtectsLessThanSixTicksAndExtendsExactlyOneMeasure(int length, int tick, int[] expected)
    {
        O2JamRandomObject[] objects = [new(0, new(0, tick)), new(0, new(1, 0)), new(0, new(2, 0)), new(0, new(3, 0))];
        Assert.That(O2JamColumnRandomizer.Panic(objects, [length, 192, 192, 192], 1), Is.EqualTo(expected));
    }

    [Test]
    public void PanicKeepsCrossMeasureHoldAndChordsInOneProtectedMapping()
    {
        O2JamRandomObject[] objects = [new(0, new(0, 48), new(2, 0)), new(1, new(0, 48)),
            new(1, new(1, 0)), new(1, new(2, 0)), new(1, new(3, 0))];
        Assert.That(O2JamColumnRandomizer.Panic(objects, [192, 192, 192, 192], 1), Is.EqualTo(new[] { 4, 5, 5, 5, 1 }));
    }

    [Test]
    public void PanicProtectsTailNearBoundaryAsWellAsHead()
    {
        O2JamRandomObject[] objects = [new(0, new(0, 0), new(0, 187)), new(0, new(1, 0)), new(0, new(2, 0))];
        Assert.That(O2JamColumnRandomizer.Panic(objects, [192, 192, 192], 1), Is.EqualTo(new[] { 4, 4, 6 }));
    }

    [Test]
    public void PanicPreservesAllSevenChordColumnsPerMeasure()
    {
        var objects = Enumerable.Range(0, 4).SelectMany(measure => Enumerable.Range(0, 7)
            .Select(column => new O2JamRandomObject(column, new O2JamSourcePosition(measure, 48)))).ToArray();
        var columns = O2JamColumnRandomizer.Panic(objects, [192, 192, 192, 192], 1);
        Assert.That(columns, Is.EqualTo(new[] { 4, 5, 0, 1, 2, 6, 3, 6, 1, 0, 5, 4, 2, 3,
            1, 3, 5, 0, 4, 6, 2, 6, 4, 5, 2, 1, 3, 0 }));
    }

    [Test]
    public void SettingsUseNativeDropdownAndExistingSeedControl()
    {
        var mod = new O2JamModRandom();
        var controls = mod.CreateSettingsControls().ToArray();
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(controls.OfType<SettingsDropdown<O2JamRandomAlgorithm>>(), Has.Exactly(1).Items);
                Assert.That(controls.OfType<SettingsNumberBox>(), Has.Exactly(1).Items);
                Assert.That(mod.Algorithm.Value, Is.EqualTo(O2JamRandomAlgorithm.Native));
            });
            var dropdown = controls.OfType<SettingsDropdown<O2JamRandomAlgorithm>>().Single();
            Assert.That(dropdown.Items, Is.EqualTo(new[] { O2JamRandomAlgorithm.Native, O2JamRandomAlgorithm.RRandom,
                O2JamRandomAlgorithm.Panic }));
            dropdown.Current.Value = O2JamRandomAlgorithm.RRandom;
            Assert.That(mod.Algorithm.Value, Is.EqualTo(O2JamRandomAlgorithm.RRandom));
            mod.Algorithm.Value = O2JamRandomAlgorithm.Panic;
            Assert.That(dropdown.Current.Value, Is.EqualTo(O2JamRandomAlgorithm.Panic));
            Assert.That(mod.SettingDescription.Single().value, Is.EqualTo(O2LazerStrings.RandomAlgorithmPanic));
            Assert.That(mod.Algorithm.Value.GetLocalisableDescription(), Is.EqualTo(O2LazerStrings.RandomAlgorithmPanic));
        }
        finally
        {
            foreach (var control in controls)
                control.Dispose();
        }
    }

    [Test]
    public void RetiredAlgorithmIsVisibleOnlyWhenSelectedAndIsNotOverwritten()
    {
        var mod = new O2JamModRandom { Algorithm = { Value = O2JamRandomAlgorithm.O2Jam } };
        var controls = mod.CreateSettingsControls().ToArray();
        try
        {
            var dropdown = controls.OfType<SettingsDropdown<O2JamRandomAlgorithm>>().Single();
            Assert.That(dropdown.Current.Value, Is.EqualTo(O2JamRandomAlgorithm.O2Jam));
            Assert.That(dropdown.Items, Does.Contain(O2JamRandomAlgorithm.O2Jam));
            dropdown.Current.Value = O2JamRandomAlgorithm.RRandom;
            Assert.That(mod.Algorithm.Value, Is.EqualTo(O2JamRandomAlgorithm.RRandom));
            Assert.That(dropdown.Items, Does.Not.Contain(O2JamRandomAlgorithm.O2Jam));
            mod.Algorithm.Value = O2JamRandomAlgorithm.O2Jam;
            Assert.That(dropdown.Items, Does.Contain(O2JamRandomAlgorithm.O2Jam));
            Assert.That(mod.Algorithm.Value, Is.EqualTo(O2JamRandomAlgorithm.O2Jam));
        }
        finally
        {
            foreach (var control in controls)
                control.Dispose();
        }
    }

    [Test]
    public void HiddenNoteShuffleStaysOutOfMenuWithoutChangingRecordedAlgorithm()
    {
        var mod = new O2JamModRandom { Algorithm = { Value = O2JamRandomAlgorithm.SRandom }, Seed = { Value = 12345 } };
        var controls = mod.CreateSettingsControls().ToArray();
        try
        {
            var dropdown = controls.OfType<SettingsDropdown<O2JamRandomAlgorithm>>().Single();
            Assert.That(dropdown.Current.Value, Is.EqualTo(O2JamRandomAlgorithm.SRandom));
            Assert.That(dropdown.Items, Does.Not.Contain(O2JamRandomAlgorithm.SRandom));
            Assert.That(mod.SettingDescription.First().value, Is.EqualTo(O2LazerStrings.RandomAlgorithmSRandom));

            var changes = new List<O2JamRandomAlgorithm>();
            mod.Algorithm.BindValueChanged(change => changes.Add(change.NewValue));
            mod.Algorithm.Value = O2JamRandomAlgorithm.O2Jam;
            mod.Algorithm.Value = O2JamRandomAlgorithm.SRandom;
            Assert.That(changes, Is.EqualTo(new[] { O2JamRandomAlgorithm.O2Jam, O2JamRandomAlgorithm.SRandom }));
            Assert.That(dropdown.Items, Does.Not.Contain(O2JamRandomAlgorithm.O2Jam));
            Assert.That(dropdown.Items, Does.Not.Contain(O2JamRandomAlgorithm.SRandom));
            Assert.That(dropdown.Current.Value, Is.EqualTo(O2JamRandomAlgorithm.SRandom));

            dropdown.Current.Value = O2JamRandomAlgorithm.Panic;
            Assert.That(mod.Algorithm.Value, Is.EqualTo(O2JamRandomAlgorithm.Panic));
            Assert.That(mod.Seed.Value, Is.EqualTo(12345));
            Assert.That(mod.SettingDescription.First().value, Is.EqualTo(O2LazerStrings.RandomAlgorithmPanic));
        }
        finally
        {
            foreach (var control in controls)
                control.Dispose();
        }
    }

    [TestCase(O2JamRandomAlgorithm.O2Jam)]
    [TestCase(O2JamRandomAlgorithm.Panic)]
    [TestCase(O2JamRandomAlgorithm.RRandom)]
    [TestCase(O2JamRandomAlgorithm.SRandom)]
    public void DirectAndNativeInterfaceEntryUseTheSameAlgorithm(O2JamRandomAlgorithm algorithm)
    {
        var ruleset = new O2LazerRuleset();
        var direct = createSource(ruleset);
        var viaInterface = createSource(ruleset);
        var mod = new O2JamModRandom { Algorithm = { Value = algorithm }, Seed = { Value = 0 } };
        mod.ApplyToBeatmap(direct);
        ((IApplicableToBeatmap)mod).ApplyToBeatmap(viaInterface);
        Assert.That(direct.HitObjects.Select(hitObject => hitObject.Column),
            Is.EqualTo(viaInterface.HitObjects.Select(hitObject => hitObject.Column)));
        Assert.That(mod.Seed.Value, Is.Zero);
    }

    [Test]
    public void NormalisedHoldWithBackwardsRawPackagesUsesMusicalFallback()
    {
        var ruleset = new O2LazerRuleset();
        var metadata = new OjnReader().Read(new MemoryStream(OjnTestData.CreateChart())).Metadata;
        OjnNoteEvent[] notes =
        [
            new(0.5, 2, 0, 100, 0, OjnNoteType.Hold, OjnSampleKind.KeySound, 0.75)
            {
                RawPosition = new OjnRawPosition(1, 0),
                RawEndPosition = new OjnRawPosition(0, 144),
            },
        ];
        var source = new OjnBeatmapFactory().Create(new OjnDocument(metadata,
            [new OjnChart(OjnDifficulty.EX, 5, [], notes, [new OjnMeasureFraction(1, 0.5)], 2)]), OjnDifficulty.EX);
        source.BeatmapInfo.Ruleset = ruleset.RulesetInfo;
        var mod = new O2JamModRandom { Algorithm = { Value = O2JamRandomAlgorithm.Panic }, Seed = { Value = 1 } };
        var playable = (O2JamBeatmap)new FlatWorkingBeatmap(source).GetPlayableBeatmap(ruleset.RulesetInfo, [mod], default);
        var hold = (HoldNote)playable.HitObjects.Single();
        Assert.Multiple(() =>
        {
            Assert.That(hold.Column, Is.EqualTo(6));
            Assert.That(hold.StartTime, Is.EqualTo(1000));
            Assert.That(hold.Duration, Is.EqualTo(500));
            Assert.That(hold.Head.Column, Is.EqualTo(hold.Tail.Column));
        });
    }

    [Test]
    public void ReplayWithoutAlgorithmSettingRetainsNativeLayout()
    {
        var ruleset = new O2LazerRuleset();
        var score = new ScoreInfo(ruleset: ruleset.RulesetInfo) { ModsJson = "[{\"acronym\":\"RD\",\"settings\":{\"seed\":12345}}]" };
        var mod = score.Mods.OfType<O2JamModRandom>().Single();
        var source = createSource(ruleset);
        var expected = createSource(ruleset);
        new ManiaModRandom { Seed = { Value = 12345 } }.ApplyToBeatmap(expected);
        var actual = (O2JamBeatmap)new FlatWorkingBeatmap(source).GetPlayableBeatmap(ruleset.RulesetInfo, [mod], default);
        Assert.That(mod.Algorithm.Value, Is.EqualTo(O2JamRandomAlgorithm.Native));
        Assert.That(actual.HitObjects.Select(hitObject => hitObject.Column), Is.EqualTo(expected.HitObjects.Select(hitObject => hitObject.Column)));
    }

    [TestCase(O2JamRandomAlgorithm.O2Jam, false)]
    [TestCase(O2JamRandomAlgorithm.Panic, false)]
    [TestCase(O2JamRandomAlgorithm.RRandom, false)]
    [TestCase(O2JamRandomAlgorithm.SRandom, false)]
    [TestCase(O2JamRandomAlgorithm.RRandom, true)]
    [TestCase(O2JamRandomAlgorithm.SRandom, true)]
    [TestCase(O2JamRandomAlgorithm.O2Jam, true)]
    [TestCase(O2JamRandomAlgorithm.Panic, true)]
    public void ImportedMetadataSurvivesConversionAndManiaScoreWithoutChangingAudio(O2JamRandomAlgorithm algorithm, bool maniaScore)
    {
        var ruleset = new O2LazerRuleset();
        var source = createSource(ruleset);
        var columns = source.HitObjects.Select(hitObject => hitObject.Column).ToArray();
        var working = new FlatWorkingBeatmap(source);
        var random = new O2JamModRandom { Algorithm = { Value = algorithm }, Seed = { Value = -12345 } };
        Mod[] mods = maniaScore ? [new O2JamModManiaScore(), random] : [random];
        var playable = (O2JamBeatmap)working.GetPlayableBeatmap(ruleset.RulesetInfo, mods, default);
        var repeated = (O2JamBeatmap)working.GetPlayableBeatmap(ruleset.RulesetInfo, mods, default);

        Assert.Multiple(() =>
        {
            Assert.That(playable.HitObjects.Select(hitObject => hitObject.Column), Is.EqualTo(repeated.HitObjects.Select(hitObject => hitObject.Column)));
            Assert.That(source.HitObjects.Select(hitObject => hitObject.Column), Is.EqualTo(columns));
            Assert.That(playable.AutomaticAudioEvents, Is.EqualTo(source.AutomaticAudioEvents));
            Assert.That(playable.SourceMeasureTickLengths, Is.EqualTo(new[] { 96, 192 }));
            Assert.That(playable.SourcePositions.Keys, Is.EquivalentTo(playable.HitObjects));
            Assert.That(playable.SourcePositions.Keys.Intersect(source.HitObjects), Is.Empty);
            Assert.That(playable.HitObjects.Select(hitObject => playable.SourcePositions[hitObject]),
                Is.EqualTo(source.HitObjects.Select(hitObject => source.SourcePositions[hitObject])));
        });
        foreach (var (hitObject, original) in playable.HitObjects.Zip(source.HitObjects))
        {
            Assert.That(hitObject.StartTime, Is.EqualTo(original.StartTime));
            Assert.That(hitObject.Samples, Is.EqualTo(original.Samples));
            if (hitObject is not HoldNote hold)
                continue;
            Assert.Multiple(() =>
            {
                Assert.That(hold.Duration, Is.EqualTo(((HoldNote)original).Duration));
                Assert.That(hold.Head.Column, Is.EqualTo(hold.Column));
                Assert.That(hold.Tail.Column, Is.EqualTo(hold.Column));
                Assert.That(hold.Head.Samples, Is.EqualTo(((HoldNote)original).GetNodeSamples(0)));
                Assert.That(hold.Tail.Samples, Is.Empty);
                Assert.That(hold.GetType(), Is.EqualTo(maniaScore ? typeof(HoldNote) : typeof(O2JamHoldNote)));
            });
        }
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void FractionalOverlappingTimesKeepTheirAuthoredMeasures(bool maniaScore, bool doubleTime)
    {
        var ruleset = new O2LazerRuleset();
        var metadata = new OjnReader().Read(new MemoryStream(OjnTestData.CreateChart())).Metadata;
        OjnNoteEvent[] notes =
        [
            new(0.75, 2, 0, 100, 0, OjnNoteType.Tap, OjnSampleKind.KeySound) { RawPosition = new OjnRawPosition(0, 144) },
            new(0.75, 3, 1, 100, 0, OjnNoteType.Tap, OjnSampleKind.KeySound) { RawPosition = new OjnRawPosition(1, 48) },
        ];
        var source = new OjnBeatmapFactory().Create(new OjnDocument(metadata,
            [new OjnChart(OjnDifficulty.EX, 5, [], notes, [new OjnMeasureFraction(1, 0.5)], 2)]), OjnDifficulty.EX);
        source.BeatmapInfo.Ruleset = ruleset.RulesetInfo;
        Mod[] mods = [new O2JamModRandom { Algorithm = { Value = O2JamRandomAlgorithm.Panic }, Seed = { Value = 1 } }];
        if (maniaScore)
            mods = [new O2JamModManiaScore(), .. mods];
        if (doubleTime)
            mods = [new O2JamModDoubleTime(), .. mods];
        var playable = (O2JamBeatmap)new FlatWorkingBeatmap(source).GetPlayableBeatmap(ruleset.RulesetInfo, mods, default);

        Assert.Multiple(() =>
        {
            Assert.That(playable.HitObjects[0].StartTime, Is.EqualTo(playable.HitObjects[1].StartTime));
            Assert.That(playable.HitObjects.Select(hitObject => hitObject.Column), Is.EqualTo(new[] { 4, 1 }));
        });
    }

    [TestCase(O2JamRandomAlgorithm.Panic, false)]
    [TestCase(O2JamRandomAlgorithm.Panic, true)]
    [TestCase(O2JamRandomAlgorithm.RRandom, false)]
    [TestCase(O2JamRandomAlgorithm.RRandom, true)]
    [TestCase(O2JamRandomAlgorithm.SRandom, false)]
    [TestCase(O2JamRandomAlgorithm.SRandom, true)]
    public void RandomCanRandomiseInvertGeneratedObjectsAndRateMods(O2JamRandomAlgorithm algorithm, bool maniaScore)
    {
        var ruleset = new O2LazerRuleset();
        var source = createSource(ruleset);
        source.HitObjects.Add(new O2JamNote { StartTime = 2000, ChartPosition = 1.5, TimingMap = source.TimingMap });
        source.HitObjects.Add(new O2JamNote { StartTime = 3000, ChartPosition = 2.5, TimingMap = source.TimingMap });
        var working = new FlatWorkingBeatmap(source);
        var random = new O2JamModRandom { Algorithm = { Value = algorithm }, Seed = { Value = 1 } };
        Mod[] mods = maniaScore ? [new O2JamModManiaScore(), new O2JamModInvert(), new O2JamModDoubleTime(), random]
            : [new O2JamModInvert(), new O2JamModDoubleTime(), random];
        var playable = (O2JamBeatmap)working.GetPlayableBeatmap(ruleset.RulesetInfo, mods, default);
        var repeated = (O2JamBeatmap)working.GetPlayableBeatmap(ruleset.RulesetInfo, mods, default);
        Assert.That(playable.HitObjects, Has.Count.EqualTo(2));
        Assert.That(playable.HitObjects.Select(hitObject => hitObject.Column), Is.EqualTo(repeated.HitObjects.Select(hitObject => hitObject.Column)));
        foreach (var hold in playable.HitObjects.OfType<HoldNote>())
        {
            Assert.That(hold.Head.Column, Is.EqualTo(hold.Column));
            Assert.That(hold.Tail.Column, Is.EqualTo(hold.Column));
        }
    }

    [TestCase(O2JamRandomAlgorithm.RRandom, false)]
    [TestCase(O2JamRandomAlgorithm.RRandom, true)]
    [TestCase(O2JamRandomAlgorithm.SRandom, false)]
    [TestCase(O2JamRandomAlgorithm.SRandom, true)]
    public void RateModsDoNotChangeSeededColumnLayouts(O2JamRandomAlgorithm algorithm, bool doubleTime)
    {
        var ruleset = new O2LazerRuleset();
        var working = new FlatWorkingBeatmap(createSource(ruleset));
        var random = new O2JamModRandom { Algorithm = { Value = algorithm }, Seed = { Value = -12345 } };
        var expected = (O2JamBeatmap)working.GetPlayableBeatmap(ruleset.RulesetInfo, [random], default);
        Mod rate = doubleTime ? new O2JamModDoubleTime() : new O2JamModHalfTime();
        var actual = (O2JamBeatmap)working.GetPlayableBeatmap(ruleset.RulesetInfo, [rate, random], default);
        Assert.That(actual.HitObjects.Select(hitObject => (hitObject.Column, hitObject.StartTime)),
            Is.EqualTo(expected.HitObjects.Select(hitObject => (hitObject.Column, hitObject.StartTime))));
    }

    private static O2JamBeatmap createSource(O2LazerRuleset ruleset)
    {
        var document = new OjnReader().Read(new MemoryStream(OjnTestData.CreateChart()));
        var source = new OjnBeatmapFactory().Create(document, OjnDifficulty.EX);
        source.BeatmapInfo.Ruleset = ruleset.RulesetInfo;
        source.BeatmapInfo.Hash = "original-random-test";
        return source;
    }
}
