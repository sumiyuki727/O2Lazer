using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace osu.Game.Rulesets.O2Lazer.O2Jam;

/// <summary>
/// Decodes the M30, OMC and OJM sample containers used by O2Jam.
/// </summary>
internal static class OjmDecoder
{
    internal sealed record OjmArchive(IReadOnlyDictionary<ushort, byte[]> Samples);

    internal sealed record OjmSampleEntry(ushort SampleId, long Offset, int Length, OjmSampleKind Kind);

    internal enum OjmSampleKind
    {
        M30,
        OmcOgg,
    }

    internal sealed record OjmArchiveInfo(
        IReadOnlyDictionary<ushort, OjmSampleEntry> SampleEntries,
        int M30Encryption = 0,
        IReadOnlySet<ushort>? KeySoundIds = null);

    private const int m30_header_size = 28;
    private const int m30_sample_header_size = 52;
    private const int omc_header_size = 20;
    private const int omc_wave_header_size = 56;
    private const int omc_ogg_header_size = 36;
    private const int maximum_sample_count = 65_536;
    private const int max_cached_archives = 64;
    private const int max_cached_lazy_samples = 512;

    private static readonly ConcurrentDictionary<string, Lazy<OjmArchive>> archives =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, Lazy<OjmArchiveInfo>> archive_infos =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, Lazy<byte[]?>> lazy_samples =
        new(StringComparer.Ordinal);

    private static readonly byte[] nami_mask = Encoding.ASCII.GetBytes("nami");
    private static readonly byte[] encryption_0412_mask = Encoding.ASCII.GetBytes("0412");

    private static readonly byte[] rearrange_table =
    [
        0x10, 0x0E, 0x02, 0x09, 0x04, 0x00, 0x07, 0x01,
        0x06, 0x08, 0x0F, 0x0A, 0x05, 0x0C, 0x03, 0x0D,
        0x0B, 0x07, 0x02, 0x0A, 0x0B, 0x03, 0x05, 0x0D,
        0x08, 0x04, 0x00, 0x0C, 0x06, 0x0F, 0x0E, 0x10,
        0x01, 0x09, 0x0C, 0x0D, 0x03, 0x00, 0x06, 0x09,
        0x0A, 0x01, 0x07, 0x08, 0x10, 0x02, 0x0B, 0x0E,
        0x04, 0x0F, 0x05, 0x08, 0x03, 0x04, 0x0D, 0x06,
        0x05, 0x0B, 0x10, 0x02, 0x0C, 0x07, 0x09, 0x0A,
        0x0F, 0x0E, 0x00, 0x01, 0x0F, 0x02, 0x0C, 0x0D,
        0x00, 0x04, 0x01, 0x05, 0x07, 0x03, 0x09, 0x10,
        0x06, 0x0B, 0x0A, 0x08, 0x0E, 0x00, 0x04, 0x0B,
        0x10, 0x0F, 0x0D, 0x0C, 0x06, 0x05, 0x07, 0x01,
        0x02, 0x03, 0x08, 0x09, 0x0A, 0x0E, 0x03, 0x10,
        0x08, 0x07, 0x06, 0x09, 0x0E, 0x0D, 0x00, 0x0A,
        0x0B, 0x04, 0x05, 0x0C, 0x02, 0x01, 0x0F, 0x04,
        0x0E, 0x10, 0x0F, 0x05, 0x08, 0x07, 0x0B, 0x00,
        0x01, 0x06, 0x02, 0x0C, 0x09, 0x03, 0x0A, 0x0D,
        0x06, 0x0D, 0x0E, 0x07, 0x10, 0x0A, 0x0B, 0x00,
        0x01, 0x0C, 0x0F, 0x02, 0x03, 0x08, 0x09, 0x04,
        0x05, 0x0A, 0x0C, 0x00, 0x08, 0x09, 0x0D, 0x03,
        0x04, 0x05, 0x10, 0x0E, 0x0F, 0x01, 0x02, 0x0B,
        0x06, 0x07, 0x05, 0x06, 0x0C, 0x04, 0x0D, 0x0F,
        0x07, 0x0E, 0x08, 0x01, 0x09, 0x02, 0x10, 0x0A,
        0x0B, 0x00, 0x03, 0x0B, 0x0F, 0x04, 0x0E, 0x03,
        0x01, 0x00, 0x02, 0x0D, 0x0C, 0x06, 0x07, 0x05,
        0x10, 0x09, 0x08, 0x0A, 0x03, 0x02, 0x01, 0x00,
        0x04, 0x0C, 0x0D, 0x0B, 0x10, 0x05, 0x06, 0x0F,
        0x0E, 0x07, 0x09, 0x0A, 0x08, 0x09, 0x0A, 0x00,
        0x07, 0x08, 0x06, 0x10, 0x03, 0x04, 0x01, 0x02,
        0x05, 0x0B, 0x0E, 0x0F, 0x0D, 0x0C, 0x0A, 0x06,
        0x09, 0x0C, 0x0B, 0x10, 0x07, 0x08, 0x00, 0x0F,
        0x03, 0x01, 0x02, 0x05, 0x0D, 0x0E, 0x04, 0x0D,
        0x00, 0x01, 0x0E, 0x02, 0x03, 0x08, 0x0B, 0x07,
        0x0C, 0x09, 0x05, 0x0A, 0x0F, 0x04, 0x06, 0x10,
        0x01, 0x0E, 0x02, 0x03, 0x0D, 0x0B, 0x07, 0x00,
        0x08, 0x0C, 0x09, 0x06, 0x0F, 0x10, 0x05, 0x0A,
        0x04, 0x00,
    ];

    internal static byte[]? GetSample(string path, ushort sampleId)
    {
        if (TryGetArchiveInfo(path, out var archiveInfo) && archiveInfo is not null && archiveInfo.SampleEntries.TryGetValue(sampleId, out var entry))
        {
            var fullPath = Path.GetFullPath(path);
            var cacheKey = $"{fullPath}|{sampleId}";

            if (!lazy_samples.ContainsKey(cacheKey) && lazy_samples.Count >= max_cached_lazy_samples)
                evictOneLazySample();

            var sample = lazy_samples.GetOrAdd(cacheKey, _ => new Lazy<byte[]?>(
                () => readSample(fullPath, entry, archiveInfo.M30Encryption),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

            if (sample != null)
                return sample;
        }

        return getArchive(path).Samples.GetValueOrDefault(sampleId);
    }

    internal static bool TryGetArchiveInfo(string? path, out OjmArchiveInfo? archiveInfo)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            archiveInfo = null;
            return false;
        }

        try
        {
            archiveInfo = getArchiveInfo(path);
            return true;
        }
        catch (Exception)
        {
            archiveInfo = null;
            return false;
        }
    }

    internal static void ClearCache()
    {
        archives.Clear();
        archive_infos.Clear();
        lazy_samples.Clear();
    }

    private static OjmArchive getArchive(string path)
    {
        var fullPath = Path.GetFullPath(path);

        // Decoded archives are expensive (multi-megabyte OJM payloads), so keep recently used
        // ones alive, but bound the cache so a full library cannot accumulate unbounded audio.
        if (!archives.ContainsKey(fullPath) && archives.Count >= max_cached_archives)
            evictOneArchive();

        return archives.GetOrAdd(fullPath, static archivePath => new Lazy<OjmArchive>(
            () => decodeArchive(File.ReadAllBytes(archivePath)),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static void evictOneArchive()
    {
        foreach (var fullPath in archives.Keys)
        {
            if (archives.TryRemove(fullPath, out _))
                return;
        }
    }

    private static void evictOneLazySample()
    {
        foreach (var cacheKey in lazy_samples.Keys)
        {
            if (lazy_samples.TryRemove(cacheKey, out _))
                return;
        }
    }

    private static OjmArchiveInfo getArchiveInfo(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return archive_infos.GetOrAdd(fullPath, static archivePath => new Lazy<OjmArchiveInfo>(
            () => readArchiveInfo(archivePath),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static byte[]? readSample(string path, OjmSampleEntry entry, int encryption)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (entry.Offset < 0 || entry.Length < 0 || entry.Offset + entry.Length > stream.Length)
                return null;

            stream.Position = entry.Offset;
            var sample = new byte[entry.Length];
            var read = 0;

            while (read < sample.Length)
            {
                var count = stream.Read(sample, read, sample.Length - read);
                if (count == 0)
                    return null;
                read += count;
            }

            if (entry.Kind == OjmSampleKind.M30)
                applyM30Xor(sample, encryption);

            return sample;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Choosing the reference mapping only needs archive metadata. Decoding the full audio
    // payloads here makes song-select stutter on multi-megabyte OJM files.
    private static OjmArchiveInfo readArchiveInfo(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

        if (stream.Length < 4)
            throw new InvalidDataException("The OJM header is truncated.");

        var signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
        return signature switch
        {
            "M30\0" => readM30Info(stream, reader),
            "OMC\0" or "OJM\0" => readOmcInfo(stream, reader),
            _ => throw new InvalidDataException("The OJM container signature is not supported."),
        };
    }

    private static OjmArchiveInfo readM30Info(Stream stream, BinaryReader reader)
    {
        if (stream.Length < m30_header_size)
            throw new InvalidDataException("The M30 header is truncated.");

        stream.Position = 8;
        var encryption = reader.ReadInt32();
        var declaredCount = reader.ReadInt32();
        var sampleOffset = reader.ReadInt32();
        var payloadSize = reader.ReadInt32();
        reader.ReadInt32();

        if (declaredCount is < 0 or > maximum_sample_count || sampleOffset < m30_header_size || sampleOffset > stream.Length || payloadSize < 0)
            throw new InvalidDataException("The M30 archive header is invalid.");

        var payloadEnd = payloadSize > 0 && (long)sampleOffset + payloadSize <= stream.Length
            ? sampleOffset + payloadSize
            : (int)stream.Length;
        stream.Position = sampleOffset;
        var sampleEntries = new Dictionary<ushort, OjmSampleEntry>();
        var keySoundIds = new HashSet<ushort>();
        var parsedCount = 0;

        // Some original archives report an inaccurate sample count, so the payload bounds are authoritative.
        while (stream.Position + m30_sample_header_size <= payloadEnd && parsedCount < maximum_sample_count)
        {
            ensureRemaining(stream, m30_sample_header_size, payloadEnd);
            reader.ReadBytes(32);
            var sampleSize = reader.ReadInt32();
            var sampleType = reader.ReadInt16();
            reader.ReadInt16();
            reader.ReadInt32();
            var sampleId = reader.ReadUInt16();
            reader.ReadInt16();
            reader.ReadInt32();

            if (sampleSize < 0 || stream.Position + sampleSize > payloadEnd)
                throw new InvalidDataException("An M30 sample payload is outside the archive bounds.");

            if (sampleSize > 0 && sampleType is 0 or 5)
            {
                var entryId = sampleId;
                if (sampleType == 0 && sampleId <= ushort.MaxValue - 1000)
                    entryId += 1000;

                sampleEntries[entryId] = new OjmSampleEntry(entryId, stream.Position, sampleSize, OjmSampleKind.M30);
                if (sampleType == 5)
                    keySoundIds.Add(sampleId);
            }

            stream.Position += sampleSize;
            parsedCount++;
        }

        return new OjmArchiveInfo(sampleEntries, encryption, keySoundIds);
    }

    private static OjmArchiveInfo readOmcInfo(Stream stream, BinaryReader reader)
    {
        if (stream.Length < omc_header_size)
            throw new InvalidDataException("The OMC header is truncated.");

        stream.Position = 4;
        var waveCount = reader.ReadInt16();
        var oggCount = reader.ReadInt16();
        var waveOffset = reader.ReadInt32();
        var oggOffset = reader.ReadInt32();
        var declaredSize = reader.ReadInt32();

        if (waveCount < 0 || oggCount < 0 || waveOffset < omc_header_size || waveOffset > oggOffset
            || oggOffset > stream.Length || declaredSize < oggOffset || declaredSize > stream.Length)
            throw new InvalidDataException("The OMC archive header is invalid.");

        stream.Position = waveOffset;
        var sampleEntries = new Dictionary<ushort, OjmSampleEntry>();
        var keySoundIds = new HashSet<ushort>();

        for (var sampleId = 0; sampleId < waveCount; sampleId++)
        {
            ensureRemaining(stream, omc_wave_header_size, oggOffset);
            reader.ReadBytes(32);
            reader.ReadInt16(); // audio format
            reader.ReadInt16(); // channels
            reader.ReadInt32(); // sample rate
            reader.ReadInt32(); // byte rate
            reader.ReadInt16(); // block align
            reader.ReadInt16(); // bits per sample
            reader.ReadInt32();
            var sampleSize = reader.ReadInt32();

            if (sampleSize < 0 || stream.Position + sampleSize > oggOffset)
                throw new InvalidDataException("An OMC wave sample size is negative or outside the archive bounds.");

            if (sampleSize > 0)
                keySoundIds.Add((ushort)sampleId);

            stream.Position += sampleSize;
        }

        stream.Position = oggOffset;
        for (var index = 0; index < oggCount; index++)
        {
            ensureRemaining(stream, omc_ogg_header_size, declaredSize);
            reader.ReadBytes(32);
            var sampleSize = reader.ReadInt32();

            if (sampleSize < 0 || stream.Position + sampleSize > declaredSize)
                throw new InvalidDataException("An OMC OGG sample size is negative or outside the archive bounds.");

            if (sampleSize > 0)
            {
                var sampleId = (ushort)(1000 + index);
                sampleEntries[sampleId] = new OjmSampleEntry(sampleId, stream.Position, sampleSize, OjmSampleKind.OmcOgg);
            }

            stream.Position += sampleSize;
        }

        return new OjmArchiveInfo(sampleEntries, KeySoundIds: keySoundIds);
    }

    private static OjmArchive decodeArchive(byte[] data)
    {
        if (data.Length < 4)
            throw new InvalidDataException("The OJM header is truncated.");

        var signature = Encoding.ASCII.GetString(data, 0, 4);
        return signature switch
        {
            "M30\0" => decodeM30(data),
            "OMC\0" or "OJM\0" => decodeOmc(data),
            _ => throw new InvalidDataException("The OJM container signature is not supported."),
        };
    }

    private static OjmArchive decodeM30(byte[] data)
    {
        if (data.Length < m30_header_size)
            throw new InvalidDataException("The M30 header is truncated.");

        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        stream.Position = 8;
        var encryption = reader.ReadInt32();
        var declaredCount = reader.ReadInt32();
        var sampleOffset = reader.ReadInt32();
        var payloadSize = reader.ReadInt32();
        reader.ReadInt32();

        if (declaredCount is < 0 or > maximum_sample_count || sampleOffset < m30_header_size || sampleOffset > data.Length || payloadSize < 0)
            throw new InvalidDataException("The M30 archive header is invalid.");

        var payloadEnd = payloadSize > 0 && (long)sampleOffset + payloadSize <= data.Length
            ? sampleOffset + payloadSize
            : data.Length;
        stream.Position = sampleOffset;
        var output = new Dictionary<ushort, byte[]>();
        var parsedCount = 0;

        // Some original archives report an inaccurate sample count, so the payload bounds are authoritative.
        while (stream.Position + m30_sample_header_size <= payloadEnd && parsedCount < maximum_sample_count)
        {
            reader.ReadBytes(32);
            var sampleSize = reader.ReadInt32();
            // This field is the M30 sample category (BGM=0, Keysound=5), not an audio codec.
            // M30 payloads are already OGG (or WAV in early archives), so they pass through
            // directly to the BASS-backed PCM decoder.
            var sampleType = reader.ReadInt16();
            reader.ReadInt16();
            reader.ReadInt32();
            var sampleId = reader.ReadUInt16();
            reader.ReadInt16();
            reader.ReadInt32();
            parsedCount++;

            if (sampleSize < 0 || stream.Position + sampleSize > payloadEnd)
                throw new InvalidDataException("An M30 sample payload is outside the archive bounds.");

            var sample = reader.ReadBytes(sampleSize);
            applyM30Xor(sample, encryption);

            // OJN event references are one-based because zero denotes an empty cell. Most official
            // archives store the normalised zero-based reference directly, while converted charts
            // may store the one-based reference unchanged; OjnDecoder chooses per archive.
            if (sampleType == 0 && sampleId <= ushort.MaxValue - 1000)
                sampleId += 1000;

            if (sample.Length > 0 && sampleType is 0 or 5)
                output.TryAdd(sampleId, sample);
        }

        return new OjmArchive(output);
    }

    private static OjmArchive decodeOmc(byte[] data)
    {
        if (data.Length < omc_header_size)
            throw new InvalidDataException("The OMC header is truncated.");

        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        stream.Position = 4;
        var waveCount = reader.ReadInt16();
        var oggCount = reader.ReadInt16();
        var waveOffset = reader.ReadInt32();
        var oggOffset = reader.ReadInt32();
        var declaredSize = reader.ReadInt32();

        if (waveCount < 0 || oggCount < 0 || waveOffset < omc_header_size || waveOffset > oggOffset
            || oggOffset > data.Length || declaredSize < oggOffset || declaredSize > data.Length)
            throw new InvalidDataException("The OMC archive header is invalid.");

        var output = new Dictionary<ushort, byte[]>();
        stream.Position = waveOffset;
        var xorState = new AccXorState();

        for (var sampleId = 0; sampleId < waveCount; sampleId++)
        {
            ensureRemaining(stream, omc_wave_header_size, oggOffset);
            reader.ReadBytes(32);
            var audioFormat = reader.ReadInt16();
            var channels = reader.ReadInt16();
            var sampleRate = reader.ReadInt32();
            var byteRate = reader.ReadInt32();
            var blockAlign = reader.ReadInt16();
            var bitsPerSample = reader.ReadInt16();
            reader.ReadInt32();
            var sampleSize = reader.ReadInt32();

            if (sampleSize < 0)
                throw new InvalidDataException("An OMC wave sample size is negative.");

            ensureRemaining(stream, sampleSize, oggOffset);
            var sample = reader.ReadBytes(sampleSize);

            if (sample.Length == 0)
                continue;

            rearrange(sample);
            xorState.Decode(sample);
            output.TryAdd((ushort)sampleId, createWave(sample, audioFormat, channels, sampleRate, byteRate, blockAlign, bitsPerSample));
        }

        stream.Position = oggOffset;
        for (var index = 0; index < oggCount; index++)
        {
            ensureRemaining(stream, omc_ogg_header_size, declaredSize);
            reader.ReadBytes(32);
            var sampleSize = reader.ReadInt32();

            if (sampleSize < 0)
                throw new InvalidDataException("An OMC OGG sample size is negative.");

            ensureRemaining(stream, sampleSize, declaredSize);
            var sample = reader.ReadBytes(sampleSize);
            if (sample.Length > 0)
                output.TryAdd((ushort)(1000 + index), sample);
        }

        return new OjmArchive(output);
    }

    private static void applyM30Xor(byte[] sample, int encryption)
    {
        var mask = encryption switch
        {
            16 => nami_mask,
            32 => encryption_0412_mask,
            _ => null,
        };

        if (mask == null)
            return;

        for (var i = 0; i + mask.Length <= sample.Length; i += mask.Length)
        {
            for (var maskIndex = 0; maskIndex < mask.Length; maskIndex++)
                sample[i + maskIndex] ^= mask[maskIndex];
        }
    }

    private static void rearrange(byte[] encoded)
    {
        var remainder = encoded.Length % 17;
        var key = remainder * 17;
        var blockSize = encoded.Length / 17;
        var original = (byte[])encoded.Clone();

        for (var block = 0; block < 17; block++)
        {
            var sourceOffset = blockSize * block;
            var destinationOffset = blockSize * rearrange_table[key + block];
            Array.Copy(original, sourceOffset, encoded, destinationOffset, blockSize);
        }
    }

    private static byte[] createWave(byte[] pcm, short audioFormat, short channels, int sampleRate, int byteRate, short blockAlign, short bitsPerSample)
    {
        using var stream = new MemoryStream(pcm.Length + 44);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(pcm.Length + 36);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write(audioFormat);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(pcm.Length);
        writer.Write(pcm);
        writer.Flush();
        return stream.ToArray();
    }

    private static void ensureRemaining(Stream stream, int count, int end)
    {
        if (count < 0 || stream.Position + count > end)
            throw new EndOfStreamException("The OJM sample payload is truncated.");
    }

    private sealed class AccXorState
    {
        private int keyByte = 0xff;
        private int counter;

        public void Decode(byte[] buffer)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                var encoded = buffer[i];
                if (((keyByte << counter) & 0x80) != 0)
                    buffer[i] = (byte)~encoded;

                counter++;
                if (counter <= 7)
                    continue;

                counter = 0;
                keyByte = encoded;
            }
        }
    }
}
