using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using osu.Game.IO.Serialization;
using osu.Game.Replays;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
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
    private const int current_version = 5;

    internal static byte[] Create(Score score)
    {
        var payload = new Payload
        {
            Version = current_version,
            Ruleset = O2LazerIdentity.ShortName,
            HasReceivedAllFrames = score.Replay.HasReceivedAllFrames,
            Frames = score.Replay.Frames.Select(convertFrame).ToList(),
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

        var json = Encoding.UTF8.GetBytes(payload.Serialize());

        using var output = new MemoryStream();
        using (var compressed = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            compressed.Write(json);

        return output.ToArray();
    }

    private static bool hasJsonEnvelope(byte[] bytes) =>
        bytes.Length >= 2 && bytes[0] == gzip_magic_1 && bytes[1] == gzip_magic_2
        || bytes.Length > 0 && bytes[0] == (byte)'{';

    internal static bool TryReadScore(ScoreInfo scoreInfo, byte[] bytes, out Score score)
    {
        score = new Score { ScoreInfo = scoreInfo, Replay = null! };

        if (!TryReadPayload(bytes, out var payload))
            return false;

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
        metadata = default;
        if (!TryReadPayload(bytes, out var payload)
            || string.IsNullOrEmpty(payload.BeatmapHash) && string.IsNullOrEmpty(payload.BeatmapMd5))
            return false;

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

    private static bool TryReadPayload(byte[] bytes, out Payload payload)
    {
        payload = new Payload();
        if (!hasJsonEnvelope(bytes))
            return false;

        try
        {
            Stream payloadStream = bytes.Length >= 2 && bytes[0] == gzip_magic_1 && bytes[1] == gzip_magic_2
                ? new GZipStream(new MemoryStream(bytes, writable: false), CompressionMode.Decompress)
                : new MemoryStream(bytes, writable: false);

            using (payloadStream)
            using (var reader = new StreamReader(payloadStream, Encoding.UTF8))
                payload = reader.ReadToEnd().Deserialize<Payload>() ?? new Payload();

            // Versions before 5 used the pre-rewrite judgement/input model. Matching the JSON
            // shape is not evidence that such a replay can be played by the current ruleset.
            if (payload.Version != current_version
                || !string.IsNullOrEmpty(payload.Ruleset) && payload.Ruleset != O2LazerIdentity.ShortName
                || payload.Frames == null || payload.Frames.Count == 0)
                return false;

            var previousTime = double.NegativeInfinity;
            foreach (var frame in payload.Frames)
            {
                if (frame == null || !double.IsFinite(frame.Time) || frame.Time < previousTime
                    || frame.Actions == null
                    || frame.Actions.Any(action => (int)action is < 0 or >= O2JamBeatmap.ColumnCount))
                    return false;

                previousTime = frame.Time;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static FrameData convertFrame(osu.Game.Rulesets.Replays.ReplayFrame frame) => frame switch
    {
        O2JamReplayFrame o2Jam => new FrameData { Time = o2Jam.Time, Actions = o2Jam.Actions.ToList() },
        _ => throw new InvalidDataException("Only native O2Jam replay frames can be recorded."),
    };

    private sealed class FrameData
    {
        public double Time { get; set; }

        public List<ManiaAction> Actions { get; set; } = [];
    }

    private sealed class Payload
    {
        public int Version { get; set; }

        // The earliest clean v5 files did not include this marker; their version and chart hashes
        // remain sufficient to import them without reviving the pre-rewrite filename fallback.
        public string Ruleset { get; set; } = string.Empty;

        public bool HasReceivedAllFrames { get; set; } = true;

        public List<FrameData> Frames { get; set; } = [];

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
