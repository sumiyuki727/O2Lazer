using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using O2Jam.Core;
using O2Jam.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Objects;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class OjnBeatmapFactoryTest
{
    [Test]
    public void FormatTypesAreEmbeddedInTheDeployableRuleset()
    {
        Assert.That(typeof(OjnReader).Assembly, Is.SameAs(typeof(O2LazerRuleset).Assembly));
        Assert.That(typeof(O2LazerRuleset).Assembly.GetReferencedAssemblies().Select(reference => reference.Name),
            Does.Not.Contain("O2Jam.Formats"));
    }

    [Test]
    public void FactoryProducesNativeManiaCompatibleHold()
    {
        var document = new OjnReader().Read(new MemoryStream(OjnTestData.CreateChart()));
        var beatmap = new OjnBeatmapFactory().Create(document, O2JamDifficulty.EX);
        var hold = beatmap.HitObjects.Single() as O2JamHoldNote;
        var schedule = O2JamPreviewSchedule.Create(beatmap, true);
        Assert.That(hold!.GetNodeSamples(1), Is.Empty);
        var converted = (O2JamBeatmap)new O2JamBeatmapConverter(beatmap, new O2LazerRuleset()).Convert();
        var convertedHold = (O2JamHoldNote)converted.HitObjects.Single();
        convertedHold.ApplyDefaults(converted.ControlPointInfo, converted.Difficulty);

        Assert.Multiple(() =>
        {
            Assert.That(hold, Is.Not.Null);
            Assert.That(hold!.StartTime, Is.Zero);
            Assert.That(hold.EndTime, Is.EqualTo(1000).Within(0.001));
            Assert.That(hold.GetNodeSamples(0).OfType<O2JamHitSampleInfo>().Select(sample => sample.SampleId), Is.EqualTo(new[] { 0 }));
            Assert.That(hold.GetNodeSamples(1), Is.Empty);
            Assert.That(schedule.PreviewEvents.Select(evt => (evt.Time, evt.SampleId)), Is.EqualTo(new[] { (0d, 0) }));
            Assert.That(beatmap.TimingMap.EffectiveBpmAtPosition(0.5), Is.EqualTo(240));
            Assert.That(beatmap.Stages.Single().Columns, Is.EqualTo(7));
            Assert.That(beatmap.MeasureLineTimes, Is.EqualTo(new[] { 0d, 1000d, 2000d }).Within(0.001));
            Assert.That(converted.MeasureLineTimes, Is.EqualTo(beatmap.MeasureLineTimes));
            Assert.That(convertedHold.NestedHitObjects.Select(nested => nested.GetType()), Is.EquivalentTo(new[]
            {
                typeof(O2JamHoldHead),
                typeof(O2JamHoldBody),
                typeof(O2JamHoldTail),
            }));
            Assert.That(convertedHold.Head.Samples.OfType<O2JamHitSampleInfo>().Select(sample => sample.SampleId), Is.EqualTo(new[] { 0 }));
            Assert.That(convertedHold.Tail.Samples, Is.Empty);
            Assert.That(convertedHold.Body.Samples, Is.Empty);
        });
    }

    [Test]
    public void NonPlayableReleaseProducesBackgroundAudio()
    {
        var bytes = OjnTestData.CreateChart();
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(316, 2), 9);
        bytes[323] = 7;
        var document = new OjnReader().Read(bytes);
        var automaticAudio = new OjnBeatmapFactory().Create(document, OjnDifficulty.EX).AutomaticAudioEvents.Single();
        Assert.That(automaticAudio.Kind, Is.EqualTo(O2JamAudioEventKind.Background));
    }
}
