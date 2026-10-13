using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using O2Jam.Core;
using osu.Game.IO.Serialization;
using osu.Game.Replays;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Replays;

/// <summary>
/// Stores O2Jam replay frames without pretending that the community ruleset is a legacy mania ruleset.
/// </summary>
internal static class O2JamReplayArchive
{
    private const byte gzip_magic_1 = 0x1f;
    private const byte gzip_magic_2 = 0x8b;
    internal const int FormatVersion = 5;

    internal static byte[] Create(Score score, O2JamReplayResourceLimits? limits = null) =>
        create(score, limits ?? O2JamReplayResourceLimits.Default, new O2JamReplayResourceBudget());

    internal static bool TryCreate(Score score, out byte[]? bytes, out O2JamReplayWriteFailure failure,
                                   O2JamReplayResourceLimits? limits = null)
    {
        bytes = null;
        failure = default;
        var budget = new O2JamReplayResourceBudget();
        try
        {
            bytes = create(score, limits ?? O2JamReplayResourceLimits.Default, budget);
            return true;
        }
        catch (Exception exception)
        {
            // Never publish the partial memory buffer, even when a compression footer failed.
            failure = new O2JamReplayWriteFailure(budget.Exceeded, exception);
            return false;
        }
    }

    private static byte[] create(Score score, O2JamReplayResourceLimits limits, O2JamReplayResourceBudget budget)
    {
        if (score.Replay == null || score.Replay.Frames.Count == 0)
            throw new InvalidDataException("A saved O2Jam replay requires recorded frames.");
        if (score.Replay.Frames.Count > limits.CollectionEntries || score.ScoreInfo.Pauses.Count > limits.CollectionEntries)
            budget.Reject();

        var payload = new Payload
        {
            Version = FormatVersion,
            JudgementVersion = O2JamJudgementEngine.RulesVersion,
            Ruleset = O2LazerIdentity.ShortName,
            HasReceivedAllFrames = score.Replay.HasReceivedAllFrames,
            // Native Player stops recording before cloning the score. Stream that stable
            // frame collection instead of duplicating all frames/actions before checking budgets.
            Frames = new FrameProjection(score.Replay.Frames, limits, budget),
            BeatmapHash = score.ScoreInfo.BeatmapHash,
            BeatmapMd5 = score.ScoreInfo.BeatmapInfo?.MD5Hash ?? string.Empty,
            Player = score.ScoreInfo.User.Username,
            UserId = score.ScoreInfo.User.OnlineID,
            Date = score.ScoreInfo.Date,
            TotalScore = score.ScoreInfo.TotalScore,
            TotalScoreWithoutMods = score.ScoreInfo.TotalScoreWithoutMods,
            MaxCombo = score.ScoreInfo.MaxCombo,
            Accuracy = score.ScoreInfo.Accuracy,
            Statistics = score.ScoreInfo.Statistics,
            MaximumStatistics = score.ScoreInfo.MaximumStatistics,
            ModsJson = score.ScoreInfo.ModsJson,
            ClientVersion = score.ScoreInfo.ClientVersion,
            Rank = score.ScoreInfo.Rank,
            Pauses = score.ScoreInfo.Pauses.ToArray(),
        };

        using var output = new MemoryStream();
        using var encoded = new O2JamReplayWriteStream(output, limits.EncodedBytes, budget);
        using (var compressed = new GZipStream(encoded, CompressionLevel.Optimal, leaveOpen: true))
        using (var decoded = new O2JamReplayWriteStream(compressed, limits.DecodedBytes, budget))
        using (var text = new StreamWriter(decoded, new UTF8Encoding(false), leaveOpen: true))
        using (var writer = new O2JamReplayJsonWriter(text, limits, budget) { AutoCompleteOnClose = false })
            JsonSerializer.Create(JsonSerializableExtensions.CreateGlobalSettings()).Serialize(writer, payload);

        return output.ToArray();
    }

    internal static bool TryReadScore(ScoreInfo scoreInfo, byte[] bytes, out Score score)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return TryReadScore(scoreInfo, stream, out score, out _);
    }

    internal static bool TryReadScore(ScoreInfo scoreInfo, Stream stream, out Score score,
                                     out O2JamReplayReadFailure failure, O2JamReplayResourceLimits? limits = null)
    {
        score = new Score { ScoreInfo = scoreInfo, Replay = null! };

        if (!TryReadPayload(stream, out var payload, out failure, limits ?? O2JamReplayResourceLimits.Default))
            return false;

        // Passed is transient in native ScoreInfo; the persisted grade survives Realm reload
        // and the existing v5 envelope without adding a second outcome format.
        scoreInfo.Passed = scoreInfo.Rank != ScoreRank.F;
        score.Replay = new Replay
        {
            HasReceivedAllFrames = payload.HasReceivedAllFrames,
            Frames = payload.Frames
                     .Select(frame => (osu.Game.Rulesets.Replays.ReplayFrame)new O2JamReplayFrame(frame.Time, frame.Actions.ToArray()))
                     .ToList(),
        };
        return true;
    }

    internal static bool TryReadMetadata(byte[] bytes, out O2JamReplayMetadata metadata)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return TryReadMetadata(stream, out metadata, out _);
    }

    internal static bool TryReadMetadata(Stream stream, out O2JamReplayMetadata metadata,
                                        out O2JamReplayReadFailure failure, O2JamReplayResourceLimits? limits = null)
    {
        metadata = default;
        if (!TryReadPayload(stream, out var payload, out failure, limits ?? O2JamReplayResourceLimits.Default))
            return false;

        if (!hasValidScoreMetadata(payload)
            || string.IsNullOrEmpty(payload.BeatmapHash) && string.IsNullOrEmpty(payload.BeatmapMd5))
        {
            failure = new O2JamReplayReadFailure(false, true);
            return false;
        }

        metadata = new O2JamReplayMetadata(
            payload.BeatmapHash ?? string.Empty,
            payload.BeatmapMd5 ?? string.Empty,
            payload.Player ?? string.Empty,
            payload.UserId,
            payload.Date,
            payload.TotalScore,
            payload.TotalScoreWithoutMods,
            payload.MaxCombo,
            payload.Accuracy,
            payload.Statistics ?? [],
            payload.MaximumStatistics ?? [],
            payload.ModsJson ?? string.Empty,
            payload.ClientVersion ?? string.Empty,
            payload.Rank,
            payload.Pauses ?? []);
        return true;
    }

    // External v5 metadata becomes a new native score without the legacy decoder's reconstruction.
    // Keep this boundary separate from playback, whose authoritative metadata is the saved ScoreInfo.
    private static bool hasValidScoreMetadata(Payload payload) =>
        double.IsFinite(payload.Accuracy) && payload.Accuracy is >= 0 and <= 1
        && payload.TotalScore >= 0 && payload.TotalScoreWithoutMods >= 0 && payload.MaxCombo >= 0
        && Enum.IsDefined(payload.Rank)
        && hasValidStatistics(payload.Statistics) && hasValidStatistics(payload.MaximumStatistics);

    private static bool hasValidStatistics(IReadOnlyDictionary<HitResult, int>? statistics)
    {
        if (statistics == null)
            return true;

        var total = 0L;
        foreach (var (result, count) in statistics)
        {
            // Native background score processing sums these counts as an int.
            if (!Enum.IsDefined(result) || count < 0 || (total += count) > int.MaxValue)
                return false;
        }
        return true;
    }

    private static bool TryReadPayload(Stream stream, out Payload payload,
                                       out O2JamReplayReadFailure failure, O2JamReplayResourceLimits limits)
    {
        payload = new Payload();
        failure = default;
        var budget = new O2JamReplayResourceBudget();
        O2JamReplayJsonReader? jsonReader = null;

        try
        {
            var prefix = new byte[2];
            var prefixLength = stream.ReadAtLeast(prefix, 2, throwOnEndOfStream: false);
            var compressed = prefixLength == 2 && prefix[0] == gzip_magic_1 && prefix[1] == gzip_magic_2;
            if (!compressed && (prefixLength == 0 || prefix[0] != (byte)'{'))
                return false;

            using var encoded = new O2JamReplayReadStream(stream, limits.EncodedBytes, budget, prefix.AsMemory(0, prefixLength));
            using var gzip = compressed ? new GZipStream(encoded, CompressionMode.Decompress, leaveOpen: true) : null;
            using var decoded = new O2JamReplayReadStream(gzip ?? (Stream)encoded, limits.DecodedBytes, budget);
            using var text = new StreamReader(decoded, Encoding.UTF8);
            using var reader = jsonReader = new O2JamReplayJsonReader(text, limits, budget);
            var serializer = JsonSerializer.Create(JsonSerializableExtensions.CreateGlobalSettings());
            serializer.CheckAdditionalContent = true;
            payload = serializer.Deserialize<Payload>(reader) ?? new Payload();

            // GZip may stop before trailing archive bytes. Include those bytes in the
            // encoded budget too, without decompressing or materialising them again.
            if (compressed)
                encoded.CopyTo(Stream.Null);

            // The marker keeps this global importer from claiming another ruleset's JSON/gzip
            // envelope. Unmarked test builds are intentionally treated as unsupported replays.
            // Existing unversioned v5 recordings are test data and use the current rule.
            // Reject unknown tagged policies instead of silently rejudging future releases.
            if (payload.Version != FormatVersion
                || payload.JudgementVersion != 0 && payload.JudgementVersion != O2JamJudgementEngine.RulesVersion
                || !string.Equals(payload.Ruleset, O2LazerIdentity.ShortName, StringComparison.Ordinal)
                || payload.Frames == null || payload.Frames.Count == 0)
            {
                failure = new O2JamReplayReadFailure(false, jsonReader.IdentifiedO2JamEnvelope);
                return false;
            }

            var previousTime = double.NegativeInfinity;
            foreach (var frame in payload.Frames)
            {
                if (frame == null || !double.IsFinite(frame.Time) || frame.Time < previousTime
                    || frame.Actions == null
                    || frame.Actions.Any(action => (int)action is < 0 or >= O2JamBeatmap.ColumnCount))
                {
                    failure = new O2JamReplayReadFailure(false, jsonReader.IdentifiedO2JamEnvelope);
                    return false;
                }

                previousTime = frame.Time;
            }

            return true;
        }
        catch
        {
            failure = new O2JamReplayReadFailure(budget.Exceeded, jsonReader?.IdentifiedO2JamEnvelope == true);
            return false;
        }
    }

    private sealed class FrameProjection(IReadOnlyList<ReplayFrame> frames, O2JamReplayResourceLimits limits,
                                        O2JamReplayResourceBudget budget) : IReadOnlyList<FrameData>
    {
        public int Count => frames.Count;
        public FrameData this[int index] => convertFrame(frames[index]);

        public IEnumerator<FrameData> GetEnumerator()
        {
            var previousTime = double.NegativeInfinity;
            foreach (var frame in frames)
            {
                var data = convertFrame(frame);
                if (!double.IsFinite(data.Time) || data.Time < previousTime || data.Actions == null)
                    throw new InvalidDataException("O2Jam replay frames must have valid ordered times and key states.");
                if (data.Actions.Count > limits.CollectionEntries)
                    budget.Reject();
                if (data.Actions.Any(action => (int)action is < 0 or >= O2JamBeatmap.ColumnCount))
                    throw new InvalidDataException("O2Jam replay frames must have valid ordered times and key states.");
                previousTime = data.Time;
                yield return data;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private static FrameData convertFrame(ReplayFrame frame) => frame switch
        {
            O2JamReplayFrame o2Jam => new FrameData { Time = o2Jam.Time, Actions = o2Jam.Actions },
            _ => throw new InvalidDataException("Only native O2Jam replay frames can be recorded."),
        };
    }

    private sealed class FrameData
    {
        public double Time { get; set; }

        public IReadOnlyList<ManiaAction> Actions { get; set; } = [];
    }

    private sealed class Payload
    {
        public int Version { get; set; }

        public int JudgementVersion { get; set; }

        public string Ruleset { get; set; } = string.Empty;

        public bool HasReceivedAllFrames { get; set; } = true;

        public IReadOnlyList<FrameData> Frames { get; set; } = [];

        public string BeatmapHash { get; set; } = string.Empty;

        public string BeatmapMd5 { get; set; } = string.Empty;

        public string Player { get; set; } = string.Empty;

        public int UserId { get; set; } = -1;

        public DateTimeOffset Date { get; set; }

        public long TotalScore { get; set; }

        public long TotalScoreWithoutMods { get; set; }

        public int MaxCombo { get; set; }

        public double Accuracy { get; set; }

        public Dictionary<HitResult, int> Statistics { get; set; } = [];

        public Dictionary<HitResult, int> MaximumStatistics { get; set; } = [];

        public string ModsJson { get; set; } = string.Empty;

        public string ClientVersion { get; set; } = string.Empty;

        public ScoreRank Rank { get; set; }

        public int[] Pauses { get; set; } = [];
    }
}

internal readonly record struct O2JamReplayMetadata(
    string BeatmapHash,
    string BeatmapMd5,
    string Player,
    int UserId,
    DateTimeOffset Date,
    long TotalScore,
    long TotalScoreWithoutMods,
    int MaxCombo,
    double Accuracy,
    Dictionary<HitResult, int> Statistics,
    Dictionary<HitResult, int> MaximumStatistics,
    string ModsJson,
    string ClientVersion,
    ScoreRank Rank,
    int[] Pauses);
