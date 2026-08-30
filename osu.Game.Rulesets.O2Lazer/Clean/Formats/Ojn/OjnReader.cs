using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using osu.Game.Rulesets.O2Lazer.Core;

namespace osu.Game.Rulesets.O2Lazer.Formats.Ojn;

public sealed class OjnReader
{
    private const int headerSize = 300;
    private const uint maximumBlockCount = 2_000_000;
    private const uint maximumMeasureCount = 2_000_000;

    private static readonly Encoding strictUtf8 = new UTF8Encoding(false, true);
    private static readonly Encoding strictCp949;
    private static readonly Encoding strictGbk;

    private readonly OjnMetadataEncoding metadataEncoding;
    private readonly Func<OjnMetadataEncoding>? encodingFallback;

    static OjnReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        strictCp949 = createStrictEncoding(949);
        strictGbk = createStrictEncoding(936);
    }

    public OjnReader(OjnMetadataEncoding metadataEncoding = OjnMetadataEncoding.Automatic)
        : this(metadataEncoding, null)
    {
    }

    internal OjnReader(OjnMetadataEncoding metadataEncoding, Func<OjnMetadataEncoding>? encodingFallback)
    {
        this.metadataEncoding = metadataEncoding;
        this.encodingFallback = encodingFallback;
    }

    internal static bool RequiresLegacyEncodingMigration(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 268, FileOptions.RandomAccess);
        Span<byte> header = stackalloc byte[268];
        if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            return true;

        // Encrypted headers cannot expose a version without a full decode, so migrate them once.
        if (header[..3].SequenceEqual("new"u8))
            return true;

        // Source timestamps do not change when our decoder changes. Revisit non-ASCII metadata
        // once per encoding revision, including Korean packs labelled with Chinese-client versions.
        return getHeaderFields(header).Any(field => field.Any(value => value >= 0x80));
    }

    public OjnDocument Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Read(memory.ToArray());
    }

    /// <summary>
    /// Reads only the requested chart without copying unencrypted OJN data or materialising its embedded images.
    /// Song select already uses imported background resources, so decoding the other two charts and cover payloads
    /// here only delays preview startup.
    /// </summary>
    public OjnDocument ReadChart(Stream stream, O2JamDifficulty difficulty)
    {
        ArgumentNullException.ThrowIfNull(stream);

        MemoryStream? buffered = null;
        var source = stream;
        if (!source.CanSeek)
        {
            buffered = new MemoryStream();
            source.CopyTo(buffered);
            source = buffered;
        }

        try
        {
            source.Position = 0;
            Span<byte> prefix = stackalloc byte[3];
            var prefixLength = source.Read(prefix);
            source.Position = 0;

            if (prefixLength == prefix.Length && prefix.SequenceEqual("new"u8))
            {
                using var encrypted = new MemoryStream();
                source.CopyTo(encrypted);
                return readChart(decryptIfNeeded(encrypted.ToArray()), difficulty);
            }

            using var reader = new BinaryReader(source, Encoding.UTF8, leaveOpen: true);
            var header = readHeader(reader, null);
            var chart = readChart(reader, header, (int)difficulty);
            return new OjnDocument(header.Metadata, [chart]);
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    public OjnDocument Read(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var data = decryptIfNeeded(source);

        if (data.Length < headerSize)
            throw new InvalidDataException("The OJN header is truncated.");

        using var reader = new BinaryReader(new MemoryStream(data, writable: false), Encoding.UTF8, leaveOpen: false);
        var header = readHeader(reader, data);
        var charts = new List<OjnChart>(3);

        for (var index = 0; index < 3; index++)
            charts.Add(readChart(reader, header, index));

        return new OjnDocument(header.Metadata, charts);
    }

    private OjnDocument readChart(byte[] source, O2JamDifficulty difficulty)
    {
        if (source.Length < headerSize)
            throw new InvalidDataException("The OJN header is truncated.");

        using var reader = new BinaryReader(new MemoryStream(source, writable: false), Encoding.UTF8, leaveOpen: false);
        var header = readHeader(reader, null);
        var chart = readChart(reader, header, (int)difficulty);
        return new OjnDocument(header.Metadata, [chart]);
    }

    private Header readHeader(BinaryReader reader, byte[]? data)
    {
        var songId = reader.ReadUInt32();
        var signature = reader.ReadBytes(4);
        if (signature.Length != 4 || signature[0] is not ((byte)'o' or (byte)'O') || signature[1] is not ((byte)'j' or (byte)'J') || signature[2] is not ((byte)'n' or (byte)'N'))
            throw new InvalidDataException("The file does not contain an OJN signature.");

        var encodingVersion = reader.ReadSingle();
        _ = reader.ReadInt32();
        var initialBpm = reader.ReadSingle();
        if (!float.IsFinite(initialBpm) || initialBpm <= 0)
            throw new InvalidDataException("The OJN initial BPM is invalid.");

        ushort[] levels = [reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16()];
        _ = reader.ReadInt16();

        uint[] eventCounts = [reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32()];
        uint[] noteCounts = [reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32()];
        uint[] measureCounts = [reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32()];
        uint[] blockCounts = [reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32()];
        _ = reader.ReadInt16();
        _ = reader.ReadInt16();
        _ = reader.ReadBytes(20);
        var thumbnailSize = reader.ReadUInt32();
        _ = reader.ReadUInt32();
        byte[][] encodedMetadata =
        [
            trimNullTerminator(reader.ReadBytes(64)),
            trimNullTerminator(reader.ReadBytes(32)),
            trimNullTerminator(reader.ReadBytes(32)),
            trimNullTerminator(reader.ReadBytes(32)),
        ];
        var fallback = new Lazy<OjnMetadataEncoding>(() =>
        {
            // A translated catalogue may retain the original artist/charter strings. A consistent
            // pack is therefore a safer tie-breaker than another field, but field evidence wins below.
            var directoryEncoding = encodingFallback?.Invoke() ?? OjnMetadataEncoding.Automatic;
            return directoryEncoding != OjnMetadataEncoding.Automatic
                ? directoryEncoding
                : inferMetadataEncoding(encodedMetadata.Take(3));
        });
        var title = decodeMetadata(encodedMetadata[0], encodingVersion, fallback);
        var artist = decodeMetadata(encodedMetadata[1], encodingVersion, fallback);
        var arranger = decodeMetadata(encodedMetadata[2], encodingVersion, fallback);
        var ojm = decodeMetadata(encodedMetadata[3], encodingVersion, fallback);
        var coverSize = reader.ReadUInt32();
        uint[] durations = [reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32()];
        uint[] blockOffsets = [reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32()];
        var coverOffset = reader.ReadUInt32();

        var cover = data == null ? [] : readOptionalImage(data, coverOffset, coverSize);
        var thumbnail = data == null ? [] : readOptionalImage(data, (long)coverOffset + coverSize, thumbnailSize);
        var metadata = new OjnMetadata(songId, encodingVersion, initialBpm, title, artist, arranger, ojm, levels, durations, cover, thumbnail);

        return new Header(metadata, eventCounts, noteCounts, measureCounts, blockCounts, blockOffsets, coverOffset);
    }

    private static OjnChart readChart(BinaryReader reader, Header header, int difficultyIndex)
    {
        var offset = header.BlockOffsets[difficultyIndex];
        var blockCount = header.BlockCounts[difficultyIndex];

        if (blockCount == 0 || offset == 0)
            return new OjnChart((O2JamDifficulty)difficultyIndex, header.Metadata.Levels[difficultyIndex], [], [], [], 0);
        if (blockCount > maximumBlockCount)
            throw new InvalidDataException("The OJN block count is outside the supported range.");
        if (offset < headerSize || offset >= reader.BaseStream.Length)
            throw new InvalidDataException("An OJN note block range is outside the file.");

        var blockEnd = findBlockEnd(header, difficultyIndex, reader.BaseStream.Length);

        reader.BaseStream.Position = offset;
        var bpmEvents = new List<O2JamBpmEvent>();
        var notes = new List<OjnNoteEvent>();
        var fractions = new SortedDictionary<int, double>();
        var measureCount = 0u;

        for (var blockIndex = 0u; blockIndex < blockCount; blockIndex++)
        {
            ensureRemaining(reader, 8, blockEnd);
            var measure = reader.ReadUInt32();
            if (measure >= maximumMeasureCount)
                throw new InvalidDataException("An OJN measure index is outside the supported range.");

            measureCount = Math.Max(measureCount, checked(measure + 1));
            var channel = reader.ReadUInt16();
            var eventCount = reader.ReadUInt16();

            if (eventCount == 0)
                continue;

            for (var eventIndex = 0; eventIndex < eventCount; eventIndex++)
            {
                var position = measure + eventIndex / (double)eventCount;

                if (channel is 0 or 1)
                {
                    ensureRemaining(reader, sizeof(float), blockEnd);
                    var value = reader.ReadSingle();
                    if (!float.IsFinite(value) || value == 0)
                        continue;

                    if (channel == 0)
                    {
                        if (value > 0)
                            fractions[checked((int)measure + 1)] = value;
                    }
                    else if (value > 0)
                        bpmEvents.Add(new O2JamBpmEvent(position, value));

                    continue;
                }

                ensureRemaining(reader, 4, blockEnd);
                var rawId = reader.ReadUInt16();
                var audio = reader.ReadByte();
                var rawType = reader.ReadByte();

                if (rawId == 0)
                    continue;

                var volumeNibble = (audio >> 4) & 0x0f;
                var volume = volumeNibble == 0 ? 100 : (int)Math.Round(volumeNibble / 16d * 100);
                var panNibble = audio & 0x0f;
                var panValue = panNibble == 0 ? 8 : panNibble;
                var pan = (float)(((panValue - 1) / 14d) * 2 - 1);
                var sampleKind = rawType % 8 > 3 ? OjnSampleKind.Background : OjnSampleKind.KeySound;
                // Zero remains a valid normalised reference even when the archive omits it. Falling
                // forward to id 1 changes authored hitsounds; player verification confirms ref - 1.
                var sampleId = rawId - 1 + (sampleKind == OjnSampleKind.Background ? 1000 : 0);
                var noteType = (rawType % 4) switch
                {
                    2 => OjnNoteType.Hold,
                    3 => OjnNoteType.Release,
                    _ => OjnNoteType.Tap,
                };

                notes.Add(new OjnNoteEvent(position, channel, sampleId, volume, pan, noteType, sampleKind));
            }
        }

        var measureFractions = fractions.Select(pair => new OjnMeasureFraction(pair.Key, pair.Value)).ToArray();
        var normalisedBpmEvents = bpmEvents.Select(evt => evt with { Position = normalisedPosition(evt.Position, fractions) });
        var normalisedNotes = notes.Select(note => note with
        {
            Position = normalisedPosition(note.Position, fractions),
        });
        return new OjnChart((O2JamDifficulty)difficultyIndex, header.Metadata.Levels[difficultyIndex],
            normalisedBpmEvents.OrderBy(evt => evt.Position).ToArray(), normaliseLongNotes(normalisedNotes.ToList()), measureFractions, measureCount);
    }

    private static OjnNoteEvent[] normaliseLongNotes(List<OjnNoteEvent> input)
    {
        var notes = input.OrderBy(note => note.Position).ToList();
        var openHolds = new Dictionary<ushort, int>();
        var removed = new HashSet<int>();

        for (var index = 0; index < notes.Count; index++)
        {
            var note = notes[index];

            if (!note.IsPlayable)
            {
                // The low flag bits describe playable LN endpoints, but non-playable channels
                // are independent automatic audio events and must never be consumed as a pair.
                notes[index] = note with { Type = OjnNoteType.Tap };
                continue;
            }

            switch (note.Type)
            {
                case OjnNoteType.Tap:
                    break;

                case OjnNoteType.Hold:
                    if (openHolds.TryGetValue(note.Channel, out var previousHeadIndex))
                        notes[previousHeadIndex] = notes[previousHeadIndex] with { Type = OjnNoteType.Tap };

                    openHolds[note.Channel] = index;
                    break;

                case OjnNoteType.Release:
                    if (!openHolds.Remove(note.Channel, out var headIndex))
                    {
                        // A release without an active head is control data, not a playable tap.
                        removed.Add(index);
                        break;
                    }

                    var head = notes[headIndex];
                    notes[headIndex] = head with
                    {
                        Type = OjnNoteType.Hold,
                        EndPosition = note.Position,
                        TailSampleId = note.SampleId,
                        TailVolume = note.Volume,
                        TailPan = note.Pan,
                    };
                    removed.Add(index);
                    break;
            }
        }

        foreach (var (_, headIndex) in openHolds)
            notes[headIndex] = notes[headIndex] with { Type = OjnNoteType.Tap };

        return notes.Where((_, index) => !removed.Contains(index)).ToArray();
    }

    private static double normalisedPosition(double rawPosition, SortedDictionary<int, double> fractions)
    {
        var position = rawPosition;
        var measure = (int)Math.Floor(rawPosition);

        foreach (var (fractionMeasure, fraction) in fractions)
        {
            if (fractionMeasure > measure)
                break;

            position -= 1 - fraction;
        }

        return position;
    }

    private static byte[] decryptIfNeeded(byte[] data)
    {
        if (data.Length < 8 || data[0] != 'n' || data[1] != 'e' || data[2] != 'w')
            return data;

        var blockSize = data[3];
        if (blockSize == 0)
            throw new InvalidDataException("The encrypted OJN key block size is zero.");

        var key = Enumerable.Repeat(data[4], blockSize).ToArray();
        key[0] = data[6];
        key[blockSize / 2] = data[5];

        var output = new byte[data.Length - 8];
        for (var index = 0; index < output.Length; index++)
            output[index] = (byte)(data[data.Length - 1 - index] ^ key[index % blockSize]);

        return output;
    }

    private string decodeMetadata(byte[] value, float encodingVersion, Lazy<OjnMetadataEncoding> fallback)
    {
        switch (metadataEncoding)
        {
            case OjnMetadataEncoding.Gbk:
                return decodeText(value, strictGbk);

            case OjnMetadataEncoding.Cp949:
                return decodeText(value, strictCp949);

            case OjnMetadataEncoding.Utf8:
                return decodeText(value, strictUtf8);
        }

        if (value.Length == 0)
            return string.Empty;
        if (value.All(character => character < 0x80))
            return Encoding.ASCII.GetString(value).Trim();
        if (hasUtf8Bom(value))
            return decodeText(value, strictUtf8);

        var utf8 = tryDecodeAllowingTruncatedSuffix(strictUtf8, value);
        var cp949 = tryDecodeAllowingTruncatedSuffix(strictCp949, value);
        var gbk = tryDecodeAllowingTruncatedSuffix(strictGbk, value);

        // One file may mix code pages (for example a Chinese charter in a Korean song pack).
        // Clear field evidence wins over context, which is only a tie-breaker for ambiguous bytes.
        var evidence = getEncodingEvidence(cp949, gbk);
        if (evidence == OjnMetadataEncoding.Cp949)
            return cp949!.Trim();
        if (evidence == OjnMetadataEncoding.Gbk)
            return gbk!.Trim();

        var preferred = fallback.Value;
        if (preferred == OjnMetadataEncoding.Cp949 && cp949 != null)
            return cp949.Trim();
        if (preferred == OjnMetadataEncoding.Gbk && gbk != null)
            return gbk.Trim();

        // Version is a last-resort compatibility prior, not an encoding declaration. Both the
        // Chinese catalogue and newer Korean/Japanese community packs contain version 2.9 files.
        if (gbk != null && (encodingVersion <= 2.1f || encodingVersion >= 2.9f))
        {
            if (cp949 != null)
            {
                var gbkNotCp949 = !canEncode(strictCp949, gbk);
                var cp949NotGbk = !canEncode(strictGbk, cp949);

                if (gbkNotCp949 && !cp949NotGbk)
                    return cp949.Trim();
                if (cp949NotGbk && !gbkNotCp949)
                    return gbk.Trim();
            }

            return gbk.Trim();
        }

        // Some CP949 byte pairs are also valid UTF-8. Hangul is the strongest signal available
        // for charts written by the original Korean clients.
        if (cp949 != null && containsHangul(cp949) && (utf8 == null || !containsHangul(utf8)))
            return cp949.Trim();

        return (utf8 ?? cp949 ?? gbk ?? Encoding.Latin1.GetString(value)).TrimStart('\ufeff').Trim();
    }

    internal static OjnMetadataEncoding InspectHeaderEncoding(ReadOnlySpan<byte> header)
    {
        // A small directory sample must never read chart/image payloads just to infer a code page.
        // Encrypted files remain readable normally, but do not contribute speculative evidence.
        if (header.Length < 268 || !header.Slice(4, 3).SequenceEqual("ojn"u8))
            return OjnMetadataEncoding.Automatic;

        return inferMetadataEncoding(getHeaderFields(header).Take(3));
    }

    private static byte[][] getHeaderFields(ReadOnlySpan<byte> header) =>
    [
        trimNullTerminator(header.Slice(108, 64).ToArray()),
        trimNullTerminator(header.Slice(172, 32).ToArray()),
        trimNullTerminator(header.Slice(204, 32).ToArray()),
        trimNullTerminator(header.Slice(236, 32).ToArray()),
    ];

    private static OjnMetadataEncoding inferMetadataEncoding(IEnumerable<byte[]> fields)
    {
        var result = OjnMetadataEncoding.Automatic;
        foreach (var field in fields)
        {
            if (hasUtf8Bom(field) || field.All(value => value < 0x80))
                continue;

            var evidence = getEncodingEvidence(tryDecodeAllowingTruncatedSuffix(strictCp949, field), tryDecodeAllowingTruncatedSuffix(strictGbk, field));
            if (evidence == OjnMetadataEncoding.Automatic)
                continue;
            if (result != OjnMetadataEncoding.Automatic && result != evidence)
                return OjnMetadataEncoding.Automatic;
            result = evidence;
        }

        return result;
    }

    private static OjnMetadataEncoding getEncodingEvidence(string? cp949, string? gbk)
    {
        // These regional catalogue labels remain meaningful even when their bytes also form Hangul.
        // Recognising the labels, rather than individual songs or IDs, preserves mixed directories.
        if (gbk != null && (gbk.Contains("[国服]", StringComparison.Ordinal)
                            || gbk.Contains("[台服]", StringComparison.Ordinal)
                            || gbk.Contains("[荣誉]", StringComparison.Ordinal)
                            || gbk.Contains("[韩]", StringComparison.Ordinal)
                            || gbk.Contains("[超]", StringComparison.Ordinal)))
            return OjnMetadataEncoding.Gbk;

        var cleanKorean = cp949 != null && !containsPrivateUse(cp949);
        var cleanChinese = gbk != null && !containsPrivateUse(gbk);
        if (cleanKorean && !cleanChinese)
            return OjnMetadataEncoding.Cp949;
        if (cleanChinese && !cleanKorean)
            return OjnMetadataEncoding.Gbk;

        return OjnMetadataEncoding.Automatic;
    }

    private static bool containsPrivateUse(string value) => value.Any(character => character is >= '\ue000' and <= '\uf8ff');

    private static string decodeText(byte[] value, Encoding encoding)
    {
        if (value.Length == 0)
            return string.Empty;

        var decoded = tryDecode(encoding, value);
        if (decoded != null)
            return decoded.TrimStart('\ufeff').Trim();

        // Fixed-width OJN fields occasionally cut the first byte of their final character at
        // the boundary. Only discard a suffix when doing so makes the entire prefix strict-valid.
        var withoutTruncatedSuffix = tryDecodeWithoutTruncatedSuffix(encoding, value);
        if (withoutTruncatedSuffix != null)
            return withoutTruncatedSuffix.TrimStart('\ufeff').Trim();

        // A single malformed field should not make the rest of an otherwise usable chart fail.
        var forgiving = encoding.CodePage switch
        {
            936 => Encoding.GetEncoding(936),
            949 => Encoding.GetEncoding(949),
            65001 => new UTF8Encoding(false, false),
            _ => Encoding.Latin1,
        };
        return forgiving.GetString(value).TrimStart('\ufeff').Trim();
    }

    private static string? tryDecodeWithoutTruncatedSuffix(Encoding encoding, byte[] bytes)
    {
        var maximumSuffixLength = encoding.CodePage == Encoding.UTF8.CodePage ? 3 : 1;

        for (var length = bytes.Length - 1; length >= Math.Max(0, bytes.Length - maximumSuffixLength); length--)
        {
            var decoded = tryDecode(encoding, bytes.AsSpan(0, length).ToArray());
            if (decoded != null)
                return decoded;
        }

        return null;
    }

    private static string? tryDecodeAllowingTruncatedSuffix(Encoding encoding, byte[] bytes) =>
        tryDecode(encoding, bytes) ?? tryDecodeWithoutTruncatedSuffix(encoding, bytes);

    private static uint findBlockEnd(Header header, int difficultyIndex, long streamLength)
    {
        var offset = header.BlockOffsets[difficultyIndex];
        var candidates = header.BlockOffsets
                               .Skip(difficultyIndex + 1)
                               .Append(header.CoverOffset)
                               .Where(candidate => candidate > offset && candidate <= streamLength)
                               .Select(candidate => (long)candidate)
                               .Append(streamLength);

        return checked((uint)candidates.Min());
    }

    private static byte[] readOptionalImage(byte[] data, long offset, uint length)
    {
        if (length == 0 || offset < 0 || offset >= data.Length)
            return [];

        var available = (int)Math.Min(length, data.Length - offset);
        return data.AsSpan(checked((int)offset), available).ToArray();
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

    private static bool containsHangul(string value) =>
        value.Any(character => character is >= '\u1100' and <= '\u11ff'
                                      or >= '\u3130' and <= '\u318f'
                                      or >= '\uac00' and <= '\ud7a3');

    private static byte[] trimNullTerminator(byte[] bytes)
    {
        var length = Array.IndexOf(bytes, (byte)0);
        return length < 0 ? bytes : bytes.AsSpan(0, length).ToArray();
    }

    private static bool hasUtf8Bom(byte[] value) => value is [0xef, 0xbb, 0xbf, ..];

    private static Encoding createStrictEncoding(int codePage)
    {
        var encoding = (Encoding)Encoding.GetEncoding(codePage).Clone();
        encoding.DecoderFallback = DecoderFallback.ExceptionFallback;
        encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
        return encoding;
    }

    private static void ensureRemaining(BinaryReader reader, int length, uint blockEnd)
    {
        if (reader.BaseStream.Position + length > blockEnd)
            throw new InvalidDataException("An OJN note block is truncated.");
    }

    private sealed record Header(
        OjnMetadata Metadata,
        uint[] EventCounts,
        uint[] NoteCounts,
        uint[] MeasureCounts,
        uint[] BlockCounts,
        uint[] BlockOffsets,
        uint CoverOffset);
}
