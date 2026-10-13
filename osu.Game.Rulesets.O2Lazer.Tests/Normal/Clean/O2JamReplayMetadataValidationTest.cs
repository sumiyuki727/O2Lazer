using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamReplayMetadataValidationTest
{
    private const string envelope = """
        {"version":5,"ruleset":"o2lazer","beatmap_hash":"metadata-chart","frames":[{"time":-10,"actions":[0,6]}]
        """;

    private static IEnumerable<TestCaseData> invalidFields()
    {
        (string, string)[] fields =
        [
            ("accuracy", "-0.01"), ("accuracy", "1.25"), ("accuracy", "\"NaN\""),
            ("accuracy", "\"Infinity\""), ("accuracy", "\"-Infinity\""), ("accuracy", "1e400"),
            ("max_combo", "-1"), ("max_combo", "2147483648"), ("max_combo", "-2147483649"),
            ("total_score", "-7"), ("total_score", "9223372036854775808"), ("total_score", "1e400"),
            ("total_score_without_mods", "-1"), ("total_score_without_mods", "9223372036854775808"),
            ("rank", "-2"), ("rank", "2147483647"), ("rank", "\"unknown_rank\""),
            ("statistics", "{\"Perfect\":-5}"), ("statistics", "{\"Perfect\":2147483648}"),
            ("statistics", "{\"-1\":1}"), ("statistics", "{\"100\":0}"),
            ("statistics", "{\"unknown_result\":1}"),
            ("maximum_statistics", "{\"Perfect\":-1}"),
            ("maximum_statistics", "{\"100\":0}"),
            ("maximum_statistics", "{\"Perfect\":2147483648}"),
            ("statistics", "{\"Perfect\":2147483647,\"Miss\":1}"),
            ("maximum_statistics", "{\"Perfect\":2147483647,\"Great\":1}"),
        ];
        bool[] compressionModes = [false, true];
        foreach (var (property, value) in fields)
        {
            foreach (var compressed in compressionModes)
                yield return new TestCaseData(property, value, compressed);
        }
    }

    [TestCaseSource(nameof(invalidFields))]
    public void InvalidScoreMetadataIsRejectedWithoutReturningPartialMetadata(string property, string value, bool compressed)
    {
        using var stream = new MemoryStream(encode($"{envelope},\"{property}\":{value}}}", compressed));
        Assert.That(O2JamReplayArchive.TryReadMetadata(stream, out var metadata, out var failure), Is.False);
        Assert.That(metadata, Is.EqualTo(default(O2JamReplayMetadata)));
        Assert.That(failure.IdentifiedO2JamEnvelope, Is.True);
        Assert.That(failure.ResourceLimitExceeded, Is.False);
        Assert.That(stream.CanRead, Is.True);
    }

    [TestCase(0)]
    [TestCase(1)]
    public void AccurateBoundariesAndStorageCapacityAreAccepted(double accuracy)
    {
        var json = $"{envelope},\"accuracy\":{accuracy},\"max_combo\":2147483647," +
                   "\"total_score\":9223372036854775807,\"total_score_without_mods\":9223372036854775807," +
                   "\"statistics\":{\"Perfect\":2147483646,\"Meh\":1}," +
                   "\"maximum_statistics\":{\"Perfect\":2147483647}}";
        Assert.That(O2JamReplayArchive.TryReadMetadata(Encoding.UTF8.GetBytes(json), out var metadata), Is.True);
        Assert.That(metadata.Accuracy, Is.EqualTo(accuracy));
        Assert.That(metadata.MaxCombo, Is.EqualTo(int.MaxValue));
        Assert.That(metadata.TotalScore, Is.EqualTo(long.MaxValue));
        Assert.That(metadata.TotalScoreWithoutMods, Is.EqualTo(long.MaxValue));
        Assert.That(metadata.Statistics[HitResult.Meh], Is.EqualTo(1));
    }

    [TestCase("", "")]
    [TestCase("statistics", "null")]
    [TestCase("maximum_statistics", "null")]
    [TestCase("statistics", "{}")]
    [TestCase("maximum_statistics", "{}")]
    public void OmittedAndNullOptionalFieldsKeepTheirV5Defaults(string property, string value)
    {
        var json = property.Length == 0 ? $"{envelope}}}" : $"{envelope},\"{property}\":{value}}}";
        Assert.That(O2JamReplayArchive.TryReadMetadata(Encoding.UTF8.GetBytes(json), out var metadata), Is.True);
        Assert.That(metadata.Accuracy, Is.Zero);
        Assert.That(metadata.MaxCombo, Is.Zero);
        Assert.That(metadata.TotalScore, Is.Zero);
        Assert.That(metadata.TotalScoreWithoutMods, Is.Zero);
        Assert.That(metadata.UserId, Is.Zero);
        Assert.That(metadata.Rank, Is.EqualTo(ScoreRank.D));
        Assert.That(metadata.Statistics, Is.Empty);
        Assert.That(metadata.MaximumStatistics, Is.Empty);
    }

    [Test]
    public void DefinedNativeRanksIncludingFailureRemainValid()
    {
        foreach (var rank in Enum.GetValues<ScoreRank>())
        {
            var json = $"{envelope},\"rank\":{(int)rank}}}";
            Assert.That(O2JamReplayArchive.TryReadMetadata(Encoding.UTF8.GetBytes(json), out var metadata), Is.True, rank.ToString());
            Assert.That(metadata.Rank, Is.EqualTo(rank));
        }
    }

    [Test]
    public void AllDefinedNativeJudgementsRemainAcceptedWithoutAProfileWhitelist()
    {
        foreach (var result in Enum.GetValues<HitResult>())
        {
            var json = $"{envelope},\"statistics\":{{\"{(int)result}\":1}},\"maximum_statistics\":{{\"{(int)result}\":1}}}}";
            Assert.That(O2JamReplayArchive.TryReadMetadata(Encoding.UTF8.GetBytes(json), out var metadata), Is.True, result.ToString());
            Assert.That(metadata.Statistics[result], Is.EqualTo(1));
            Assert.That(metadata.MaximumStatistics[result], Is.EqualTo(1));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RecordedModeAndUnrelatedNegativeSentinelsRemainUntouched(bool maniaScore)
    {
        var mods = maniaScore ? "[{\"acronym\":\"MS\"},{\"acronym\":\"NF\"}]" : "[{\"acronym\":\"EX\"}]";
        var json = $"{envelope},\"rank\":-1,\"user_id\":-1,\"accuracy\":0.5," +
                   $"\"mods_json\":{System.Text.Json.JsonSerializer.Serialize(mods)}," +
                   "\"max_combo\":0,\"statistics\":{\"Meh\":1},\"maximum_statistics\":{\"Perfect\":1},\"pauses\":[-20]}";
        var bytes = Encoding.UTF8.GetBytes(json);
        Assert.That(O2JamReplayArchive.TryReadMetadata(bytes, out var metadata), Is.True);
        Assert.That(metadata.Rank, Is.EqualTo(ScoreRank.F));
        Assert.That(metadata.UserId, Is.EqualTo(-1));
        Assert.That(metadata.ModsJson, Is.EqualTo(mods));
        Assert.That(metadata.Pauses, Is.EqualTo(new[] { -20 }));
        var info = new ScoreInfo { Rank = ScoreRank.F };
        Assert.That(O2JamReplayArchive.TryReadScore(info, bytes, out var score), Is.True);
        Assert.That(score.Replay.Frames[0].Time, Is.EqualTo(-10));
        Assert.That(score.ScoreInfo.Passed, Is.False);
    }

    [Test]
    public void ExistingStoredScorePlaybackDoesNotReimportUnusedEnvelopeMetadata()
    {
        var bytes = Encoding.UTF8.GetBytes($"{envelope},\"accuracy\":1.25,\"max_combo\":-12}}");
        var info = new ScoreInfo { Accuracy = 0.9, MaxCombo = 8, TotalScore = 123, Rank = ScoreRank.A, Combo = -1 };
        Assert.That(O2JamReplayArchive.TryReadMetadata(bytes, out _), Is.False);
        Assert.That(O2JamReplayArchive.TryReadScore(info, bytes, out var score), Is.True);
        Assert.That(score.ScoreInfo, Is.SameAs(info));
        Assert.That(info.Accuracy, Is.EqualTo(0.9));
        Assert.That(info.MaxCombo, Is.EqualTo(8));
        Assert.That(info.TotalScore, Is.EqualTo(123));
        Assert.That(info.Combo, Is.EqualTo(-1));
        Assert.That(info.Passed, Is.True);
    }

    private static byte[] encode(string json, bool compressed)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        if (!compressed)
            return bytes;
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true)) gzip.Write(bytes);
        return output.ToArray();
    }
}
