using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Difficulty;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamManiaStarRatingTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void MatchesNativeManiaForChordsAndHoldsAcrossBpmChanges(bool holds)
    {
        var notes = new List<OjnNoteEvent>();
        (double Position, ushort Channel)[] taps = [(0, 2), (0.125, 3), (0.5, 4), (0.5, 5), (0.75, 8), (0.875, 6), (1, 7), (1.125, 4), (1.5, 3), (1.75, 5), (2, 8)];
        foreach (var (position, channel) in taps)
            notes.Add(new OjnNoteEvent(position, channel, 1, 100, 0, OjnNoteType.Tap, OjnSampleKind.KeySound));
        notes.Add(new OjnNoteEvent(0.25, 2, 1, 100, 0, holds ? OjnNoteType.Hold : OjnNoteType.Tap, OjnSampleKind.KeySound, holds ? 1.5 : null));
        notes.Add(new OjnNoteEvent(0.25, 9, 1, 100, 0, OjnNoteType.Tap, OjnSampleKind.Background));
        var document = new OjnDocument(
            new OjnMetadata(100, 2.9f, 120, "Test", "Test", "Test", "missing.ojm", [75, 0, 0], [3, 0, 0], [], []),
            [new OjnChart(O2JamDifficulty.EX, 75, [new O2JamBpmEvent(1, 240)], notes, [], 2)]);
        var o2jam = new OjnBeatmapFactory().Create(document, O2JamDifficulty.EX);
        var originalInfo = o2jam.BeatmapInfo;

        // These timestamps are specified independently of the factory under test. The native
        // playable-beatmap pipeline supplies the comparison without the import adapter.
        var ruleset = new ManiaRuleset();
        var mania = new ManiaBeatmap(new StageDefinition(7))
        {
            BeatmapInfo = new BeatmapInfo(ruleset.RulesetInfo),
        };
        mania.Difficulty.CircleSize = 7;
        (double Time, int Column)[] expectedTaps = [(0, 0), (250, 1), (1000, 2), (1000, 3), (1500, 6), (1750, 4), (2000, 5), (2125, 2), (2500, 1), (2750, 3), (3000, 6)];
        foreach (var (time, column) in expectedTaps)
            mania.HitObjects.Add(new Note { StartTime = time, Column = column });
        mania.HitObjects.Add(holds
            ? new HoldNote { StartTime = 500, Duration = 2000, Column = 0 }
            : new Note { StartTime = 500, Column = 0 });
        mania.HitObjects.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

        var native = new ManiaDifficultyCalculator(ruleset.RulesetInfo, new FlatWorkingBeatmap(mania));
        var expected = native.Calculate().StarRating;
        var actual = O2JamManiaStarRating.Calculate(o2jam);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EqualTo(expected).Within(1e-12));
            Assert.That(actual, Is.GreaterThan(0));
            Assert.That(O2JamManiaStarRating.Version, Is.EqualTo(native.Version));
            Assert.That(o2jam.BeatmapInfo, Is.SameAs(originalInfo));
            Assert.That(o2jam.HitObjects, Has.Count.EqualTo(12));
            Assert.That(o2jam.AutomaticAudioEvents, Has.Count.EqualTo(1));
        });
    }
}
