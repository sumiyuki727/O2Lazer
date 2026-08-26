using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Replays;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Replays;

[TestFixture]
public class O2LazerReplayArchiveTest
{
    [Test]
    public void TestScoreDataRoundTrips()
    {
        var scoreInfo = new ScoreInfo
        {
            TotalScore = 123456,
            MaxCombo = 789,
            Accuracy = 0.9876,
            Date = new DateTimeOffset(2026, 8, 26, 9, 42, 0, TimeSpan.Zero),
            Statistics = new Dictionary<HitResult, int>
            {
                [HitResult.Perfect] = 10,
                [HitResult.Ok] = 2,
            },
            MaximumStatistics = new Dictionary<HitResult, int>
            {
                [HitResult.Perfect] = 12,
            },
        };

        var score = new Score
        {
            ScoreInfo = scoreInfo,
            Replay = new Replay(),
        };

        using var archive = O2LazerReplayArchive.Create(score);
        var bytes = archive.Get(O2LazerReplayArchive.FILENAME);

        var restoredInfo = new ScoreInfo();
        Assert.That(O2LazerReplayArchive.TryReadScore(restoredInfo, bytes, out var restored, out var hasEmbeddedScoreData), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(hasEmbeddedScoreData, Is.True);
            Assert.That(restored.ScoreInfo.TotalScore, Is.EqualTo(123456));
            Assert.That(restored.ScoreInfo.MaxCombo, Is.EqualTo(789));
            Assert.That(restored.ScoreInfo.Accuracy, Is.EqualTo(0.9876).Within(0.0001));
            Assert.That(restored.ScoreInfo.Date, Is.EqualTo(new DateTimeOffset(2026, 8, 26, 9, 42, 0, TimeSpan.Zero)));
            Assert.That(restored.ScoreInfo.Statistics[HitResult.Perfect], Is.EqualTo(10));
            Assert.That(restored.ScoreInfo.Statistics[HitResult.Ok], Is.EqualTo(2));
            Assert.That(restored.ScoreInfo.MaximumStatistics[HitResult.Perfect], Is.EqualTo(12));
        });
    }
}
