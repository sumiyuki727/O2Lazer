using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using osu.Framework.Extensions;
using osu.Framework.IO.Stores;
using osu.Game.Extensions;
using osu.Game.IO.Archives;
using osu.Game.IO.Serialization;
using osu.Game.Replays;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Replays;

public static class O2LazerReplayArchive
{
    public const string FILENAME = "replay.osr";

    // New archives are gzip-compressed JSON; old archives are plain JSON (which starts with '{').
    // The reader tells them apart by gzip's two magic bytes, so pre-gzip scores/replays still
    // load instead of crashing — no version handshake needed.
    private const byte gzip_magic_1 = 0x1f;
    private const byte gzip_magic_2 = 0x8b;
    private const int current_version = 3;

    public static ArchiveReader Create(Score score)
        => new ByteArrayArchiveReader(createReplayData(score), FILENAME);

    public static string ComputeHash(Score score)
    {
        using var stream = new MemoryStream(createReplayData(score));
        return stream.ComputeSHA2Hash();
    }

    private static byte[] createReplayData(Score score)
    {
        var json = serializePayload(score);

        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(json, 0, json.Length);

        return ms.ToArray();
    }

    private static byte[] serializePayload(Score score)
    {
        O2LazerJudgementEventStore.TryGet(score.ScoreInfo, out var judgementEvents);

        var payload = new Payload
        {
            HasReceivedAllFrames = score.Replay.HasReceivedAllFrames,
            Frames = score.Replay.Frames.OfType<O2LazerReplayFrame>().ToList(),
            JudgementEvents = judgementEvents.Select(JudgementEventData.From).ToList(),
        };

        return Encoding.UTF8.GetBytes(payload.Serialize());
    }

    public static Score ReadScore(ScoreInfo scoreInfo, IResourceStore<byte[]> store)
    {
        var score = new Score
        {
            ScoreInfo = scoreInfo,
        };

        var replayFile = scoreInfo.Files.FirstOrDefault(f => f.Filename == FILENAME);

        if (replayFile == null)
            return score;

        using var stream = store.GetStream(replayFile.File.GetStoragePath());

        if (stream == null)
            return score;

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();

        if (bytes.Length == 0)
            return score;

        // gzip-wrapped JSON for new archives; plain JSON for archives written before the gzip
        // switch. Falling back keeps old scores/replays working instead of throwing.
        Stream payloadStream = bytes.Length >= 2 && bytes[0] == gzip_magic_1 && bytes[1] == gzip_magic_2
            ? new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress)
            : new MemoryStream(bytes);

        Payload payload;

        using (payloadStream)
        using (var reader = new StreamReader(payloadStream, Encoding.UTF8))
            payload = reader.ReadToEnd().Deserialize<Payload>() ?? new Payload();

        score.Replay = new Replay
        {
            HasReceivedAllFrames = payload.HasReceivedAllFrames,
            // Frames default to [] on the Payload, but osu!'s serializer uses
            // DefaultValueHandling.IgnoreAndPopulate, which populates an absent field with the
            // type's default (null for List<>) — overriding the = [] initialiser. Older archives
            // may omit frames entirely, so guard against the serializer replacing the
            // collection initialiser with null.
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            Frames = (payload.Frames ?? []).Cast<osu.Game.Rulesets.Replays.ReplayFrame>().ToList(),
        };

        if (payload.Version == current_version)
        {
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            var judgementEvents = (payload.JudgementEvents ?? []).Select(e => e.ToJudgementEvent()).ToArray();
            score.ScoreInfo.HitEvents = O2LazerJudgementEventProjection.CreateTimingHitEvents(judgementEvents);
            O2LazerJudgementEventStore.Set(score.ScoreInfo, judgementEvents);

        }
        else
        {
            // Input frames remain useful across schema changes; derived statistics are rebuilt
            // by replay playback instead of guessing at a no-longer-compatible event model.
            score.ScoreInfo.HitEvents = [];
            O2LazerJudgementEventStore.Clear(score.ScoreInfo);
        }

        return score;
    }

    private class Payload
    {
        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
        public int Version { get; set; } = current_version;

        public bool HasReceivedAllFrames { get; init; } = true;

        public List<O2LazerReplayFrame> Frames { get; init; } = [];

        public List<JudgementEventData> JudgementEvents { get; init; } = [];

    }

    private class JudgementEventData
    {
        public HitResult Result { get; init; }

        public JudgementSourceData Source { get; init; } = new();

        public List<TimingObservationData> TimingObservations { get; init; } = [];

        public static JudgementEventData From(O2LazerJudgementEvent judgementEvent) => new()
        {
            Result = judgementEvent.Result,
            Source = JudgementSourceData.From(judgementEvent.Source),
            TimingObservations = judgementEvent.TimingObservations.Select(TimingObservationData.From).ToList(),
        };

        public O2LazerJudgementEvent ToJudgementEvent() => new(
            Source.ToJudgementSource(),
            Result,
            TimingObservations.Select(observation => observation.ToTimingObservation()));
    }

    private class TimingObservationData
    {
        public O2LazerTimingObservationKind Kind { get; init; }

        public double ExpectedTime { get; init; }

        public double ActualTime { get; init; }

        public double? GameplayRate { get; init; }

        public HitResult Result { get; init; }

        public static TimingObservationData From(O2LazerTimingObservation observation) => new()
        {
            Kind = observation.Kind,
            ExpectedTime = observation.ExpectedTime,
            ActualTime = observation.ActualTime,
            GameplayRate = observation.GameplayRate,
            Result = observation.Result,
        };

        public O2LazerTimingObservation ToTimingObservation() => new(
            Kind,
            ExpectedTime,
            ActualTime,
            GameplayRate,
            Result);
    }

    private class JudgementSourceData
    {
        public double StartTime { get; init; }

        public int Column { get; init; }

        public O2LazerJudgementSourceKind Kind { get; init; }

        public double Duration { get; init; }

        public static JudgementSourceData From(O2LazerJudgementSource source) => new()
        {
            StartTime = source.StartTime,
            Column = source.Column,
            Kind = source.Kind,
            Duration = source.Duration,
        };

        public O2LazerJudgementSource ToJudgementSource() => new(
            StartTime,
            Column,
            Kind,
            Duration);
    }

}
