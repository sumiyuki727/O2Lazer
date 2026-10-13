using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using NUnit.Framework;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Replays;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Replays;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamReplayWriteBudgetTest
{
    private static O2JamReplayResourceLimits limits => new(1024 * 1024, 1024 * 1024, 100, 10000, 4096);

    [TestCase("encoded", -1)]
    [TestCase("encoded", 0)]
    [TestCase("encoded", 1)]
    [TestCase("decoded", -1)]
    [TestCase("decoded", 0)]
    [TestCase("decoded", 1)]
    [TestCase("tokens", -1)]
    [TestCase("tokens", 0)]
    [TestCase("tokens", 1)]
    public void SavedArchivesFitTheSameExactReadBudget(string kind, int margin)
    {
        var score = createScore();
        var baseline = O2JamReplayArchive.Create(score);
        var json = decode(baseline);
        using var text = new StringReader(json);
        using var reader = new JsonTextReader(text);
        var tokens = 0;
        while (reader.Read())
            tokens++;
        var policy = kind switch
        {
            "encoded" => limits with { EncodedBytes = baseline.Length + margin },
            "decoded" => limits with { DecodedBytes = Encoding.UTF8.GetByteCount(json) + margin },
            _ => limits with { Tokens = tokens + margin },
        };

        var accepted = O2JamReplayArchive.TryCreate(score, out var bytes, out var failure, policy);
        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.EqualTo(margin >= 0));
            Assert.That(failure.ResourceLimitExceeded, Is.EqualTo(margin < 0));
            Assert.That(bytes == null, Is.EqualTo(margin < 0), "No truncated archive may escape, including compression footer failures.");
        });
        if (!accepted)
            return;

        using var source = new MemoryStream(bytes!);
        Assert.That(O2JamReplayArchive.TryReadScore(new ScoreInfo(), source, out var restored, out _, policy), Is.True);
        Assert.That(restored.Replay.Frames.Select(frame => frame.Time), Is.EqualTo(score.Replay.Frames.Select(frame => frame.Time)));
        using var metadataSource = new MemoryStream(bytes!);
        Assert.That(O2JamReplayArchive.TryReadMetadata(metadataSource, out var metadata, out _, policy), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(metadata.UserId, Is.EqualTo(-1));
            Assert.That(metadata.TotalScore, Is.EqualTo(score.ScoreInfo.TotalScore));
            Assert.That(metadata.TotalScoreWithoutMods, Is.EqualTo(score.ScoreInfo.TotalScoreWithoutMods));
            Assert.That(metadata.Accuracy, Is.EqualTo(score.ScoreInfo.Accuracy));
            Assert.That(metadata.Date, Is.EqualTo(score.ScoreInfo.Date));
            Assert.That(metadata.Statistics, Is.EqualTo(score.ScoreInfo.Statistics));
            Assert.That(metadata.Pauses, Is.EqualTo(score.ScoreInfo.Pauses));
            Assert.That(metadata.Rank, Is.EqualTo(ScoreRank.F));
        });
    }

    [TestCase("frames", 32)]
    [TestCase("frames", 33)]
    [TestCase("actions", 32)]
    [TestCase("actions", 33)]
    [TestCase("pauses", 32)]
    [TestCase("pauses", 33)]
    [TestCase("statistics", 32)]
    [TestCase("statistics", 33)]
    [TestCase("maximum_statistics", 32)]
    [TestCase("maximum_statistics", 33)]
    public void EveryWrittenCollectionIsBounded(string kind, int count)
    {
        var score = createScore();
        switch (kind)
        {
            case "frames":
                score.Replay.Frames = Enumerable.Range(0, count).Select(time => (osu.Game.Rulesets.Replays.ReplayFrame)new O2JamReplayFrame(time, ManiaAction.Key1)).ToList();
                break;
            case "actions":
                ((O2JamReplayFrame)score.Replay.Frames[0]).Actions = Enumerable.Repeat(ManiaAction.Key1, count).ToList();
                break;
            case "pauses":
                score.ScoreInfo.Pauses.Clear();
                foreach (var pause in Enumerable.Range(0, count))
                    score.ScoreInfo.Pauses.Add(pause);
                break;
            case "statistics":
                score.ScoreInfo.Statistics = Enumerable.Range(0, count).ToDictionary(value => (HitResult)value, _ => 1);
                break;
            default:
                score.ScoreInfo.MaximumStatistics = Enumerable.Range(0, count).ToDictionary(value => (HitResult)value, _ => 1);
                break;
        }

        var policy = limits with { CollectionEntries = 32 };
        Assert.That(O2JamReplayArchive.TryCreate(score, out var bytes, out var failure, policy), Is.EqualTo(count <= 32));
        Assert.That(failure.ResourceLimitExceeded, Is.EqualTo(count > 32));
        if (bytes != null)
        {
            using var stream = new MemoryStream(bytes);
            Assert.That(O2JamReplayArchive.TryReadScore(new ScoreInfo(), stream, out _, out _, policy), Is.True);
        }
    }

    [TestCase(32)]
    [TestCase(33)]
    public void TextIsBoundedBeforeNativeEscaping(int count)
    {
        var score = createScore();
        score.ScoreInfo.User = new APIUser { Id = -1, Username = new string('"', count) };
        var policy = limits with { TextCharacters = 32 };
        Assert.That(O2JamReplayArchive.TryCreate(score, out var bytes, out var failure, policy), Is.EqualTo(count <= 32));
        Assert.That(failure.ResourceLimitExceeded, Is.EqualTo(count > 32));
        if (bytes != null)
        {
            using var stream = new MemoryStream(bytes);
            Assert.That(O2JamReplayArchive.TryReadMetadata(stream, out var metadata, out _, policy), Is.True);
            Assert.That(metadata.Player, Is.EqualTo(score.ScoreInfo.User.Username));
        }
    }

    [TestCase("missing")]
    [TestCase("empty")]
    [TestCase("foreign")]
    [TestCase("nan")]
    [TestCase("infinite")]
    [TestCase("unordered")]
    [TestCase("action")]
    [TestCase("null-actions")]
    public void InvalidRecordingDoesNotReturnAnyArchive(string kind)
    {
        var score = createScore();
        switch (kind)
        {
            case "missing": score.Replay = null!; break;
            case "empty": score.Replay.Frames.Clear(); break;
            case "foreign": score.Replay.Frames = [new ManiaReplayFrame(1, ManiaAction.Key1)]; break;
            case "nan": score.Replay.Frames[0].Time = double.NaN; break;
            case "infinite": score.Replay.Frames[0].Time = double.PositiveInfinity; break;
            case "unordered": score.Replay.Frames[1].Time = -100; break;
            case "action": ((O2JamReplayFrame)score.Replay.Frames[0]).Actions = [(ManiaAction)7]; break;
            default: ((O2JamReplayFrame)score.Replay.Frames[0]).Actions = null!; break;
        }
        Assert.That(O2JamReplayArchive.TryCreate(score, out var bytes, out var failure), Is.False);
        Assert.That(bytes, Is.Null);
        Assert.That(failure.ResourceLimitExceeded, Is.False);
        Assert.That(failure.Error, Is.Not.Null);
    }

    [Test]
    public void ByteLimiterDoesNotGrowDestinationOnRejectedWrite()
    {
        using var output = new MemoryStream();
        var budget = new O2JamReplayResourceBudget();
        using var writer = new O2JamReplayWriteStream(output, 3, budget);
        writer.Write([1, 2]);
        Assert.Throws<InvalidDataException>(() => writer.Write([3, 4]));
        Assert.That(output.ToArray(), Is.EqualTo(new byte[] { 1, 2 }));
        Assert.That(budget.Exceeded, Is.True);
        writer.Dispose();
        Assert.That(output.CanWrite, Is.True);
    }

    [Test]
    public void NegativeEqualAndLongSpanTimesAndDuplicateActionsRemainValid()
    {
        var score = createScore();
        score.Replay.Frames =
        [
            new O2JamReplayFrame(-100, ManiaAction.Key1, ManiaAction.Key1),
            new O2JamReplayFrame(-100, ManiaAction.Key7),
            new O2JamReplayFrame(24 * 60 * 60 * 1000, ManiaAction.Key1),
        ];
        Assert.That(O2JamReplayArchive.TryCreate(score, out var bytes, out _), Is.True);
        Assert.That(O2JamReplayArchive.TryReadScore(new ScoreInfo(), bytes!, out var restored), Is.True);
        Assert.That(restored.Replay.Frames.Select(frame => frame.Time), Is.EqualTo(new double[] { -100, -100, 86400000 }));
        Assert.That(((O2JamReplayFrame)restored.Replay.Frames[0]).Actions, Is.EqualTo(new[] { ManiaAction.Key1, ManiaAction.Key1 }));
    }

    [Test]
    public void NullableStringWriteCountsTheNativeNullTokenOnlyOnce()
    {
        using var text = new StringWriter();
        var budget = new O2JamReplayResourceBudget();
        using var writer = new O2JamReplayJsonWriter(text, limits with { Tokens = 4 }, budget);
        writer.WriteStartArray();
        writer.WriteValue((string?)null);
        writer.WriteValue("value");
        writer.WriteEndArray();
        writer.Flush();
        Assert.That(text.ToString(), Is.EqualTo("[null,\"value\"]"));
        Assert.That(budget.Exceeded, Is.False);
    }
    private static Score createScore()
    {
        var score = new Score
        {
            ScoreInfo = new ScoreInfo
            {
                BeatmapHash = "writer-chart",
                User = new APIUser { Id = -1, Username = "player" },
                Date = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.FromHours(9)),
                TotalScore = 12345678901,
                TotalScoreWithoutMods = 12345678900,
                Accuracy = 0.95,
                MaxCombo = 42,
                Rank = ScoreRank.F,
                Statistics = new Dictionary<HitResult, int> { [HitResult.Meh] = 1 },
                MaximumStatistics = new Dictionary<HitResult, int> { [HitResult.Perfect] = 1 },
                ModsJson = "[]",
                ClientVersion = "2026.1005.0",
            },
            Replay = new Replay { Frames = [new O2JamReplayFrame(-10, ManiaAction.Key1), new O2JamReplayFrame(100.5, ManiaAction.Key7)] },
        };
        score.ScoreInfo.Pauses.Add(1);
        return score;
    }

    private static string decode(byte[] bytes)
    {
        using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
        using var text = new StreamReader(gzip, Encoding.UTF8);
        return text.ReadToEnd();
    }
}