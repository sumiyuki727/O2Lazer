using NUnit.Framework;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamReplayKeySoundHistoryTest
{
    [Test]
    public void MissesAndFutureHitsAreExcludedAndRevertRestoresEarlierVoice()
    {
        var history = new O2JamReplayKeySoundHistory();
        var earlier = result(100, 7, HitResult.Great);
        var later = result(200, 7, HitResult.Great);
        history.Apply(earlier);
        history.Apply(later);
        history.Apply(result(150, 9, HitResult.Miss));
        Assert.That(history.At(199), Is.EqualTo(new[] { new O2JamPreviewEvent(100, 7, 80, 0.25f, true, false) }));
        Assert.That(history.At(250), Is.EqualTo(new[] { new O2JamPreviewEvent(200, 7, 80, 0.25f, true, false) }));
        history.Revert(later);
        Assert.That(history.At(250), Is.EqualTo(history.At(199)));
        history.Revert(earlier);
        Assert.That(history.At(250), Is.Empty);
    }

    private static JudgementResult result(double time, int sampleId, HitResult type)
    {
        var note = new O2JamNote { StartTime = time, Samples = [new O2JamHitSampleInfo(sampleId, 80, 0.25f)] };
        return new JudgementResult(note, note.CreateJudgement()) { Type = type };
    }
}