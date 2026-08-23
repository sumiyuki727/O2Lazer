using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Objects;

namespace osu.Game.Rulesets.O2Lazer.O2Jam;

public enum OjnDifficulty
{
    EX = 0,
    NX = 1,
    HX = 2,
}

public sealed record OjnHeader(
    int Id,
    float EncodingVersion,
    int Genre,
    float Bpm,
    short[] Levels,
    int[] EventCounts,
    int[] NoteCounts,
    int[] MeasureCounts,
    int[] BlockCounts,
    string Title,
    string Artist,
    string Noter,
    string OjmFileName,
    int[] Durations,
    int[] BlockOffsets,
    byte[] CoverArt,
    byte[] Thumbnail);

public sealed record OjnDecodedChart(
    OjnHeader Header,
    OjnDifficulty Difficulty,
    O2LazerParseResult ParseResult,
    double Length);

internal readonly record struct ChartCacheKey(string SourcePath, long LastWriteTicks, long Length, OjnDifficulty Difficulty);

/// <summary>
/// Decodes native O2Jam OJN charts into the ruleset's internal vertical-scroll model.
/// Both the classic plain container and the newer reversed/XOR container are supported.
/// </summary>
public static class OjnDecoder
{
    public const int TICK_RESOLUTION = 192;

    private const int header_size = 300;
    private const int maximum_block_count = 2_000_000;
    private const int maximum_block_divisions = short.MaxValue;
    private const int max_cached_charts = 128;
    private const bool enable_dynamic_sample_mapping = true;

    private static readonly ConcurrentDictionary<ChartCacheKey, Lazy<OjnDecodedChart>> chart_cache = new();

    private static readonly string[] difficulty_names = ["EX", "NX", "HX"];
    private static readonly Encoding strict_utf8 = new UTF8Encoding(false, true);
    private static readonly Encoding strict_cp949;
    private static readonly Encoding strict_gbk;

    static OjnDecoder()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        strict_cp949 = createStrictCp949();
        strict_gbk = createStrictEncoding(936);
    }

    public static OjnHeader DecodeHeader(byte[] source)
    {
        var data = decryptIfRequired(source);

        if (data.Length < header_size)
            throw new InvalidDataException("The OJN header is truncated.");

        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

        var id = reader.ReadInt32();
        var signature = Encoding.ASCII.GetString(reader.ReadBytes(4));

        if (signature != "ojn\0")
            throw new InvalidDataException("The file does not contain a classic OJN payload.");

        var encodingVersion = reader.ReadSingle();
        var genre = reader.ReadInt32();
        var bpm = reader.ReadSingle();

        if (!float.IsFinite(bpm) || bpm <= 0)
            throw new InvalidDataException("The OJN base BPM is invalid.");

        var levels = readInt16Array(reader, 4);
        var eventCounts = readInt32Array(reader, 3);
        var noteCounts = readInt32Array(reader, 3);
        var measureCounts = readInt32Array(reader, 3);
        var blockCounts = readInt32Array(reader, 3);

        reader.ReadInt16(); // legacy encoding version
        reader.ReadInt16(); // legacy song id
        reader.ReadBytes(20); // legacy genre

        var thumbnailSize = readNonNegative(reader, "thumbnail size");
        reader.ReadInt32(); // file version

        var title = readFixedString(reader, 64, encodingVersion);
        var artist = readFixedString(reader, 32, encodingVersion);
        var noter = readFixedString(reader, 32, encodingVersion);
        var ojmFileName = readFixedString(reader, 32, encodingVersion);

        var coverSize = readNonNegative(reader, "cover size");
        var durations = readInt32Array(reader, 3);
        var blockOffsets = readInt32Array(reader, 3);
        var coverOffset = reader.ReadInt32();

        validateRanges(data.Length, blockOffsets, blockCounts, coverOffset, coverSize, thumbnailSize);

        stream.Position = coverOffset;
        var cover = reader.ReadBytes(coverSize);
        var thumbnail = reader.ReadBytes(thumbnailSize);

        return new OjnHeader(
            id,
            encodingVersion,
            genre,
            bpm,
            levels,
            eventCounts,
            noteCounts,
            measureCounts,
            blockCounts,
            title,
            artist,
            noter,
            ojmFileName,
            durations,
            blockOffsets,
            cover,
            thumbnail);
    }

    public static IReadOnlyList<OjnDecodedChart> DecodeAll(byte[] source, string? sourcePath = null)
    {
        var data = decryptIfRequired(source);
        var header = DecodeHeader(data);
        var result = new List<OjnDecodedChart>(3);

        foreach (var difficulty in Enum.GetValues<OjnDifficulty>())
        {
            var index = (int)difficulty;
            if (header.BlockCounts[index] <= 0 && header.NoteCounts[index] <= 0)
                continue;

            result.Add(decodeDifficulty(data, header, difficulty, sourcePath));
        }

        return result;
    }

    public static OjnDecodedChart Decode(byte[] source, OjnDifficulty difficulty, string? sourcePath = null)
    {
        var data = decryptIfRequired(source);
        return decodeDifficulty(data, DecodeHeader(data), difficulty, sourcePath);
    }

    internal static OjnDecodedChart DecodeCached(string sourcePath, OjnDifficulty difficulty)
    {
        var info = new FileInfo(sourcePath);
        var key = new ChartCacheKey(sourcePath, info.LastWriteTimeUtc.Ticks, info.Length, difficulty);

        if (chart_cache.Count >= max_cached_charts)
            evictOneChart();

        return chart_cache.GetOrAdd(key, static cacheKey => new Lazy<OjnDecodedChart>(
            () => Decode(File.ReadAllBytes(cacheKey.SourcePath), cacheKey.Difficulty, cacheKey.SourcePath),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static void evictOneChart()
    {
        foreach (var key in chart_cache.Keys)
        {
            if (chart_cache.TryRemove(key, out _))
                return;
        }
    }

    internal static Beatmap DecodeBeatmap(byte[] source, BeatmapInfo beatmapInfo, string? sourcePath = null)
    {
        var difficulty = ResolveDifficulty(beatmapInfo.DifficultyName);
        var resolvedSourcePath = sourcePath ?? beatmapInfo.Path;
        var decoded = !string.IsNullOrEmpty(resolvedSourcePath) && File.Exists(resolvedSourcePath)
            ? DecodeCached(resolvedSourcePath, difficulty)
            : Decode(source, difficulty, resolvedSourcePath);
        var output = new O2LazerDecodedBeatmap
        {
            BeatmapInfo = beatmapInfo,
            ControlPointInfo = new ControlPointInfo(),
        };

        output.CopyFrom(decoded.ParseResult);
        O2LazerBeatmapDecoder.PopulateTiming(output, decoded.ParseResult.TimingMap.BpmEvents);

        output.Metadata.Title = decoded.Header.Title;
        output.Metadata.Artist = decoded.Header.Artist;
        output.Metadata.Author.Username = string.IsNullOrWhiteSpace(decoded.Header.Noter) ? Constant.AUTHOR : decoded.Header.Noter;
        output.Metadata.Tags = $"o2jam {genreName(decoded.Header.Genre)}";

        if (string.IsNullOrWhiteSpace(output.Metadata.Source))
            output.Metadata.Source = "O2Jam";

        var level = decoded.Header.Levels[(int)difficulty];
        new O2LazerDifficultyInfo
        {
            ParsedName = DifficultyDisplayName(difficulty, level),
            PlayLevel = level,
            Rank = 2,
            Total = level,
            KeyCount = O2LazerLayout.O2JAM_KEY_COLUMNS,
            LockedLongNoteMode = O2LazerLongNoteMode.Undefined,
        }.WriteToOsuDifficulty(output);

        foreach (var parsedObject in decoded.ParseResult.HitObjects)
            output.HitObjects.Add(O2LazerBeatmapDecoder.CreateHitObject(parsedObject, output));

        return output;
    }

    public static OjnDifficulty ResolveDifficulty(string? difficultyName)
    {
        if (!string.IsNullOrWhiteSpace(difficultyName))
        {
            for (var i = 0; i < difficulty_names.Length; i++)
            {
                if (difficultyName.StartsWith(difficulty_names[i], StringComparison.OrdinalIgnoreCase))
                    return (OjnDifficulty)i;
            }
        }

        return OjnDifficulty.HX;
    }

    public static string DifficultyDisplayName(OjnDifficulty difficulty, int level) => $"{difficulty_names[(int)difficulty]} Lv.{level}";

    private static string resolveOjmFileName(OjnHeader header, string? sourcePath)
    {
        if (!string.IsNullOrWhiteSpace(header.OjmFileName))
            return header.OjmFileName;

        return !string.IsNullOrWhiteSpace(sourcePath)
            ? Path.ChangeExtension(Path.GetFileName(sourcePath), ".ojm") ?? string.Empty
            : string.Empty;
    }

    private static OjnDecodedChart decodeDifficulty(byte[] data, OjnHeader header, OjnDifficulty difficulty, string? sourcePath)
    {
        var index = (int)difficulty;
        var ojmFileName = resolveOjmFileName(header, sourcePath);
        var rawEvents = readEvents(data, header.BlockOffsets[index], header.BlockCounts[index]);
        var archiveInfo = loadArchive(sourcePath, ojmFileName);
        if (archiveInfo != null)
            rawEvents = mapEventsToArchive(rawEvents, archiveInfo);

        var maxMeasure = Math.Max(header.MeasureCounts[index], rawEvents.Count == 0 ? 0 : rawEvents.Max(e => e.Measure) + 1);
        var measures = Enumerable.Range(0, Math.Max(1, maxMeasure) + 1)
            .Select(measure => new O2LazerMeasureInfo(measure, (long)measure * TICK_RESOLUTION, TICK_RESOLUTION, 1));
        var bpmEvents = buildBpmEvents(rawEvents, header.Bpm);
        var timingMap = new O2LazerTimingMap(TICK_RESOLUTION, measures, bpmEvents, [], [], [], header.Bpm);

        var sounds = rawEvents.OfType<RawSoundEvent>().OrderBy(e => e.Tick).ThenBy(e => e.Sequence).ToArray();
        var hitObjects = buildHitObjects(sounds, timingMap);
        var backgroundEvents = sounds
            .Where(e => !e.IsPlayable)
            .Select(e => new O2LazerSampleEvent(timingMap.ProjectTickToTime(e.Tick), e.Tick, e.SampleKey, e.Volume))
            .ToArray();

        var referencedSamples = sounds.Select(e => e.SampleKey).Distinct();
        var sampleDefinitions = referencedSamples.ToDictionary(
            sampleKey => sampleKey,
            sampleKey => OjnSampleReference.Create(ojmFileName, sampleKey));

        var parseResult = new O2LazerParseResult(
            header.Title,
            header.Artist,
            header.Levels[index],
            2,
            header.Levels[index],
            TICK_RESOLUTION,
            timingMap,
            O2LazerLayoutVariant.O2Jam7K,
            O2LazerLayout.O2JAM_KEY_COLUMNS,
            sampleDefinitions,
            backgroundEvents,
            hitObjects,
            [],
            new O2LazerTextEvents(string.Empty, []),
            new O2LazerBgaTimeline(new Dictionary<ushort, string>(), new Dictionary<ushort, O2LazerBgaDefinition>(), [], [], O2LazerPoorBgaMode.Replace),
            Genre: genreName(header.Genre),
            Maker: header.Noter);

        var objectLength = hitObjects.Length == 0 ? 0 : hitObjects.Max(hit => hit.StartTime + hit.Duration) + 5000;
        var declaredLength = Math.Max(0, header.Durations[index]) * 1000d;
        return new OjnDecodedChart(header, difficulty, parseResult, Math.Max(objectLength, declaredLength));
    }

    private static List<RawEvent> readEvents(byte[] data, int offset, int blockCount)
    {
        if (blockCount is < 0 or > maximum_block_count)
            throw new InvalidDataException("The OJN block count is outside the supported range.");

        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        stream.Position = offset;

        var output = new List<RawEvent>();
        var sequence = 0;

        for (var block = 0; block < blockCount; block++)
        {
            ensureRemaining(stream, 8);
            var measure = reader.ReadInt32();
            var channel = reader.ReadInt16();
            var divisions = reader.ReadInt16();

            if (measure < 0 || divisions is <= 0 or > maximum_block_divisions)
                throw new InvalidDataException("The OJN event block header is invalid.");

            ensureRemaining(stream, (long)divisions * 4);

            for (var cell = 0; cell < divisions; cell++)
            {
                var tick = (long)measure * TICK_RESOLUTION
                           + (long)Math.Round(cell * (TICK_RESOLUTION / (double)divisions));

                if (channel is 0 or 1)
                {
                    var value = reader.ReadSingle();
                    if (channel == 1 && float.IsFinite(value) && value > 0)
                        output.Add(new RawBpmEvent(measure, tick, sequence++, value));

                    continue;
                }

                var reference = reader.ReadUInt16();
                var volumePan = reader.ReadByte();
                var flag = reader.ReadByte();

                if (reference == 0 || channel < 0)
                    continue;

                var bankOffset = flag % 8 > 3 ? 1000 : 0;
                var defaultSample = reference - 1 + bankOffset;
                if (defaultSample is < 0 or > ushort.MaxValue)
                    continue;

                var volumeNibble = (volumePan >> 4) & 0x0f;
                var volume = volumeNibble == 0 ? 100 : (int)Math.Round(volumeNibble / 16d * 100);
                output.Add(new RawSoundEvent(
                    measure,
                    tick,
                    sequence++,
                    channel,
                    reference,
                    (ushort)defaultSample,
                    bankOffset,
                    flag % 4,
                    volume));
            }
        }

        return output;
    }

    private static OjmDecoder.OjmArchiveInfo? loadArchive(string? sourcePath, string ojmFileName)
    {
        if (string.IsNullOrWhiteSpace(ojmFileName) || string.IsNullOrWhiteSpace(sourcePath))
            return null;

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
            if (string.IsNullOrEmpty(directory))
                return null;

            var ojmPath = Path.Combine(directory, ojmFileName);
            return OjmDecoder.TryGetArchiveInfo(ojmPath, out var archiveInfo) ? archiveInfo : null;
        }
        catch
        {
            // Without a decodable companion archive the previous zero-based mapping is the
            // closest fallback; the audio resource store will still report the real failure.
            return null;
        }
    }

    private static List<RawEvent> mapEventsToArchive(List<RawEvent> rawEvents, OjmDecoder.OjmArchiveInfo archiveInfo)
    {
        var keySoundIds = archiveInfo.KeySoundIds;
        var useRawReference = enable_dynamic_sample_mapping && keySoundIds != null && !keySoundIds.Contains(0)
            ? rawReferenceFitsArchive(rawEvents, keySoundIds)
            : false;

        return rawEvents
            .Select(rawEvent => rawEvent is RawSoundEvent sound ? mapSound(sound, useRawReference) : rawEvent)
            .ToList();
    }

    private static bool rawReferenceFitsArchive(List<RawEvent> rawEvents, IReadOnlySet<ushort> keySoundIds)
    {
        var rawMisses = rawEvents.OfType<RawSoundEvent>().Count(sound => sound.BankOffset == 0 && !keySoundIds.Contains(sound.Reference));
        var canonicalMisses = rawEvents.OfType<RawSoundEvent>().Count(sound => sound.BankOffset == 0 && !keySoundIds.Contains((ushort)(sound.Reference - 1)));
        return rawMisses <= canonicalMisses;
    }

    private static RawSoundEvent mapSound(RawSoundEvent sound, bool useRawReference)
    {
        if (!useRawReference || sound.BankOffset != 0)
            return sound;

        return sound with { SampleKey = sound.Reference };
    }

    private static O2LazerBpmEvent[] buildBpmEvents(IEnumerable<RawEvent> events, double baseBpm)
    {
        var changes = events.OfType<RawBpmEvent>()
            .OrderBy(e => e.Tick)
            .ThenBy(e => e.Sequence)
            .ToArray();
        var output = new List<O2LazerBpmEvent>(changes.Length + 1)
        {
            new(0, baseBpm, 0),
        };

        long previousTick = 0;
        var currentBpm = baseBpm;
        double currentTime = 0;

        foreach (var change in changes)
        {
            currentTime += ticksToMilliseconds(change.Tick - previousTick, currentBpm);
            output.Add(new O2LazerBpmEvent(change.Tick, change.Bpm, currentTime, change.Sequence + 1));
            previousTick = change.Tick;
            currentBpm = change.Bpm;
        }

        return output.ToArray();
    }

    private static O2LazerParsedHitObject[] buildHitObjects(IEnumerable<RawSoundEvent> events, O2LazerTimingMap timingMap)
    {
        var output = new List<O2LazerParsedHitObject>();
        var pendingHolds = new Dictionary<int, RawSoundEvent>();

        foreach (var sound in events.Where(e => e.IsPlayable))
        {
            var column = sound.Channel - 2;

            switch (sound.Signature)
            {
                case 2:
                    if (pendingHolds.TryGetValue(column, out var previous))
                        output.Add(createShortNote(previous, column, timingMap));

                    pendingHolds[column] = sound;
                    break;

                case 3:
                    if (!pendingHolds.Remove(column, out var head))
                        break;

                    var startTime = timingMap.ProjectTickToTime(head.Tick);
                    var endTime = timingMap.ProjectTickToTime(sound.Tick);
                    output.Add(new O2LazerParsedHitObject(
                        head.Tick,
                        sound.Tick,
                        startTime,
                        Math.Max(0, endTime - startTime),
                        column,
                        (ushort)head.Channel,
                        head.SampleKey,
                        true,
                        head.Volume));
                    break;

                default:
                    output.Add(createShortNote(sound, column, timingMap));
                    break;
            }
        }

        foreach (var (column, head) in pendingHolds)
            output.Add(createShortNote(head, column, timingMap));

        return output.OrderBy(e => e.StartTime).ThenBy(e => e.Column).ToArray();
    }

    private static O2LazerParsedHitObject createShortNote(RawSoundEvent sound, int column, O2LazerTimingMap timingMap) => new(
        sound.Tick,
        sound.Tick,
        timingMap.ProjectTickToTime(sound.Tick),
        0,
        column,
        (ushort)sound.Channel,
        sound.SampleKey,
        false,
        sound.Volume);

    private static byte[] decryptIfRequired(byte[] source)
    {
        if (source.Length < 3 || source[0] != (byte)'n' || source[1] != (byte)'e' || source[2] != (byte)'w')
            return source;

        if (source.Length < 8)
            throw new InvalidDataException("The encrypted OJN header is truncated.");

        var blockSize = source[3];
        if (blockSize == 0)
            throw new InvalidDataException("The encrypted OJN block size is zero.");

        var key = Enumerable.Repeat(source[4], blockSize).ToArray();
        key[0] = source[6];
        key[blockSize / 2] = source[5];

        var output = new byte[source.Length - 8];
        for (var i = 0; i < output.Length; i++)
            output[i] = (byte)(source[source.Length - i - 1] ^ key[i % blockSize]);

        return output;
    }

    private static short[] readInt16Array(BinaryReader reader, int count)
    {
        var output = new short[count];
        for (var i = 0; i < count; i++)
            output[i] = reader.ReadInt16();
        return output;
    }

    private static int[] readInt32Array(BinaryReader reader, int count)
    {
        var output = new int[count];
        for (var i = 0; i < count; i++)
            output[i] = reader.ReadInt32();
        return output;
    }

    private static int readNonNegative(BinaryReader reader, string field)
    {
        var value = reader.ReadInt32();
        return value >= 0 ? value : throw new InvalidDataException($"The OJN {field} is negative.");
    }

    private static string readFixedString(BinaryReader reader, int length, float encodingVersion)
    {
        var bytes = reader.ReadBytes(length);
        var terminator = Array.IndexOf(bytes, (byte)0);
        if (terminator >= 0)
            bytes = bytes[..terminator];

        if (bytes.Length == 0)
            return string.Empty;

        var utf8 = tryDecode(strict_utf8, bytes);
        var cp949 = tryDecode(strict_cp949, bytes);
        var gbk = tryDecode(strict_gbk, bytes);

        if (encodingVersion >= 2.9f && gbk != null)
        {
            // Chinese O2Jam 2.9 clients wrote their localised metadata in CP936/GBK,
            // but Korean charts saved by the same client can still use CP949 Hanja.
            // Validity alone is ambiguous, so use cross-encoding round trips: a string
            // readable as Korean Hanja is usually not representable as simplified GBK
            // Chinese and vice versa (e.g. 国 is absent from CP949).
            if (cp949 != null)
            {
                var gbkNotCp949 = !canEncode(strict_cp949, gbk);
                var cp949NotGbk = !canEncode(strict_gbk, cp949);

                if (gbkNotCp949 && !cp949NotGbk)
                    return cp949.Trim();

                if (cp949NotGbk && !gbkNotCp949)
                    return gbk.Trim();
            }

            return gbk.Trim();
        }

        // Official OJN metadata is predominantly CP949. Some CP949 byte pairs are also
        // legal UTF-8 (for example 징 = C2 A1), so validity alone cannot choose correctly.
        if (cp949 != null && containsHangul(cp949) && (utf8 == null || !containsHangul(utf8)))
            return cp949.Trim();

        return (utf8 ?? cp949 ?? gbk ?? Encoding.Latin1.GetString(bytes)).Trim();
    }

    private static string? tryDecode(Encoding encoding, byte[] bytes)
    {
        try
        {
            return encoding.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool canEncode(Encoding encoding, string value)
    {
        try
        {
            encoding.GetBytes(value);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static bool containsHangul(string value)
        => value.Any(character => character is >= '\u1100' and <= '\u11ff'
                                  or >= '\u3130' and <= '\u318f'
                                  or >= '\uac00' and <= '\ud7a3');

    private static Encoding createStrictCp949()
        => createStrictEncoding(949);

    private static Encoding createStrictEncoding(int codePage)
    {
        var encoding = Encoding.GetEncoding(codePage);
        encoding = (Encoding)encoding.Clone();
        encoding.DecoderFallback = DecoderFallback.ExceptionFallback;
        encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
        return encoding;
    }

    private static void validateRanges(int fileLength, int[] offsets, int[] blockCounts, int coverOffset, int coverSize, int thumbnailSize)
    {
        for (var i = 0; i < offsets.Length; i++)
        {
            if (blockCounts[i] == 0 && offsets[i] == 0)
                continue;

            if (offsets[i] < header_size || offsets[i] > fileLength)
                throw new InvalidDataException($"OJN difficulty {i} has an invalid block offset.");
            if (blockCounts[i] is < 0 or > maximum_block_count)
                throw new InvalidDataException($"OJN difficulty {i} has an invalid block count.");
        }

        if (coverSize == 0 && thumbnailSize == 0)
            return;

        var imageEnd = (long)coverOffset + coverSize + thumbnailSize;
        if (coverOffset < header_size || imageEnd > fileLength)
            throw new InvalidDataException("The embedded OJN images are outside the file bounds.");
    }

    private static void ensureRemaining(Stream stream, long count)
    {
        if (count < 0 || stream.Position + count > stream.Length)
            throw new EndOfStreamException("The OJN event payload is truncated.");
    }

    private static double ticksToMilliseconds(long ticks, double bpm) =>
        ticks * (60000 / bpm) / (TICK_RESOLUTION / 4d);

    private static string genreName(int genre) => genre switch
    {
        0 => "Ballad",
        1 => "Rock",
        2 => "Dance",
        3 => "Techno",
        4 => "Hip-Hop",
        5 => "Soul",
        6 => "Jazz",
        7 => "Funk",
        8 => "Classical",
        9 => "Traditional",
        _ => "Etc",
    };

    private abstract record RawEvent(int Measure, long Tick, int Sequence);

    private sealed record RawBpmEvent(int Measure, long Tick, int Sequence, double Bpm)
        : RawEvent(Measure, Tick, Sequence);

    private sealed record RawSoundEvent(
        int Measure,
        long Tick,
        int Sequence,
        int Channel,
        ushort Reference,
        ushort SampleKey,
        int BankOffset,
        int Signature,
        int Volume)
        : RawEvent(Measure, Tick, Sequence)
    {
        public bool IsPlayable => Channel is >= 2 and <= 8;
    }
}


