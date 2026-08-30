using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Objects;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamPreviewScheduleTest
{
    [TestCase(7)]
    [TestCase(8)]
    public void LongNotesOnlyScheduleHeadKeysounds(int tailSampleId)
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.HitObjects.Add(new O2JamHoldNote
        {
            StartTime = 100,
            Duration = 1000,
            NodeSamples =
            [
                [new O2JamHitSampleInfo(7, 70, 0.5f)],
                [new O2JamHitSampleInfo(tailSampleId, 90, -0.5f)],
            ],
        });

        var schedule = O2JamPreviewSchedule.Create(beatmap, true);

        Assert.Multiple(() =>
        {
            Assert.That(schedule.BackgroundEvents, Is.Empty);
            Assert.That(schedule.PreviewEvents, Is.EqualTo(new[]
            {
                new O2JamPreviewEvent(100, 7, 70, 0.5f, true, false),
            }));
        });
    }

    [Test]
    public void LongNoteWithoutNodeSamplesOnlySchedulesItsHead()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.HitObjects.Add(new O2JamHoldNote
        {
            StartTime = 100,
            Duration = 1000,
            Samples = [new O2JamHitSampleInfo(7, 70, 0.5f)],
        });

        var schedule = O2JamPreviewSchedule.Create(beatmap, true);

        Assert.That(schedule.PreviewEvents, Is.EqualTo(new[]
        {
            new O2JamPreviewEvent(100, 7, 70, 0.5f, true, false),
        }));
    }

    [Test]
    public void PreviewKeepsBackgroundAndKeysoundsOnOneOrderedTimeline()
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(100, 1000, 80, -0.5f));
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(100, 1, 60, 0, O2JamAudioEventKind.KeySound));
        beatmap.HitObjects.Add(new O2JamNote
        {
            StartTime = 100,
            Samples = [new O2JamHitSampleInfo(2, 70, 0.5f)],
        });

        var withKeysounds = O2JamPreviewSchedule.Create(beatmap, true);
        var withoutKeysounds = O2JamPreviewSchedule.Create(beatmap, false);

        Assert.Multiple(() =>
        {
            Assert.That(withKeysounds.BackgroundEvents.Select(evt => evt.SampleId), Is.EqualTo(new[] { 1000 }));
            Assert.That(withKeysounds.PreviewEvents.Select(evt => evt.SampleId), Is.EqualTo(new[] { 1000, 1, 2 }));
            Assert.That(withKeysounds.PreviewEvents.Select(evt => evt.IsKeySound), Is.EqualTo(new[] { false, true, true }));
            Assert.That(withKeysounds.PreviewEvents.Select(evt => evt.IsAutomatic), Is.EqualTo(new[] { true, true, false }));
            Assert.That(withoutKeysounds.PreviewEvents.Select(evt => evt.SampleId), Is.EqualTo(new[] { 1000, 1 }));
        });
    }
}
