using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace osu.Game.Rulesets.O2Lazer.Formats.Ojm;

public sealed class OjmReader
{
    private const uint maximumSampleCount = 2_000_000;

    private static readonly byte[] waveRearrangeTable = Convert.FromHexString(
        "100E02090400070106080F0A050C030D0B07020A0B03050D0804000C060F0E1001090C0D030006090A01070810020B0E040F050803040D06050B10020C07090A0F0E00010F020C0D0004010507030910060B0A080E00040B100F0D0C06050701020308090A0E0310080706090E0D000A0B04050C02010F040E100F0508070B000106020C09030A0D060D0E07100A0B00010C0F0203080904050A0C0008090D030405100E0F01020B060705060C040D0F070E08010902100A0B00030B0F040E030100020D0C0607051009080A03020100040C0D0B1005060F0E07090A08090A000708061003040102050B0E0F0D0C0A06090C0B100708000F030102050D0E040D00010E0203080B070C09050A0F040610010E02030D0B0700080C09060F10050A0400");

    public OjmArchive Read(Stream stream) => Read(stream, null);

    /// <summary>
    /// Indexes selected samples without loading their payloads. Each payload is read from the source file
    /// only when osu!framework first requests that specific sound.
    /// </summary>
    internal OjmArchive ReadLazy(string path, IReadOnlySet<int>? sampleIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.RandomAccess);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (stream.Length < 4)
            throw new InvalidDataException("The OJM header is truncated.");

        var signature = Encoding.ASCII.GetString(reader.ReadBytes(3));
        _ = reader.ReadByte();
        return signature switch
        {
            "OMC" or "OJM" => readOmcLazy(reader, path, signature == "OMC", sampleIds),
            "M30" => readM30Lazy(reader, path, sampleIds),
            _ => throw new InvalidDataException("The file does not contain a supported OJM signature."),
        };
    }

    public OjmArchive Read(Stream stream, IReadOnlySet<int>? sampleIds)
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
            using var reader = new BinaryReader(source, Encoding.ASCII, leaveOpen: true);
            if (source.Length < 4)
                throw new InvalidDataException("The OJM header is truncated.");

            var signature = Encoding.ASCII.GetString(reader.ReadBytes(3));
            _ = reader.ReadByte();
            return signature switch
            {
                "OMC" or "OJM" => readOmc(reader, signature == "OMC", sampleIds),
                "M30" => readM30(reader, sampleIds),
                _ => throw new InvalidDataException("The file does not contain a supported OJM signature."),
            };
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    public OjmArchiveIndex ReadIndex(Stream stream)
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
            using var reader = new BinaryReader(source, Encoding.ASCII, leaveOpen: true);
            if (source.Length < 4)
                throw new InvalidDataException("The OJM header is truncated.");

            var signature = Encoding.ASCII.GetString(reader.ReadBytes(3));
            _ = reader.ReadByte();
            return signature switch
            {
                "OMC" or "OJM" => readOmcIndex(reader),
                "M30" => readM30Index(reader),
                _ => throw new InvalidDataException("The file does not contain a supported OJM signature."),
            };
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    private static OjmArchive readOmc(BinaryReader reader, bool encryptedWave, IReadOnlySet<int>? sampleIds)
    {
        var effectCount = reader.ReadUInt16();
        var backgroundCount = reader.ReadUInt16();
        var effectOffset = reader.ReadUInt32();
        var backgroundOffset = reader.ReadUInt32();
        _ = reader.ReadUInt32();

        var samples = new Dictionary<int, OjmSample>();
        var accumulatorKey = 0xff;
        var accumulatorCounter = 0;

        seek(reader, effectOffset, reader.BaseStream.Length);
        for (var index = 0; index < effectCount; index++)
        {
            ensureRemaining(reader, 56);
            var name = decodeName(reader.ReadBytes(32));
            var audioFormat = reader.ReadUInt16();
            var channelCount = reader.ReadUInt16();
            var sampleRate = reader.ReadUInt32();
            var byteRate = reader.ReadUInt32();
            var blockAlign = reader.ReadUInt16();
            var bitsPerSample = reader.ReadUInt16();
            _ = reader.ReadUInt32();
            var chunkSize = reader.ReadUInt32();
            ensureRemaining(reader, chunkSize);

            if (chunkSize == 0)
                continue;

            var include = sampleIds == null || sampleIds.Contains(index);
            if (encryptedWave)
            {
                // OMC wave encryption carries state across effect entries. Unreferenced entries
                // still need decoding to advance that state, but no WAV container is allocated.
                if (include)
                {
                    var encoded = reader.ReadBytes(checked((int)chunkSize));
                    var pcm = decodeWave(encoded, ref accumulatorKey, ref accumulatorCounter);
                    var wave = buildWave(audioFormat, channelCount, sampleRate, byteRate, blockAlign, bitsPerSample, pcm);
                    samples[index] = new OjmSample(index, name, ".wav", wave);
                }
                else
                    advanceWaveEncryption(reader, chunkSize, ref accumulatorKey, ref accumulatorCounter);
            }
            else if (include)
            {
                var pcm = reader.ReadBytes(checked((int)chunkSize));
                var wave = buildWave(audioFormat, channelCount, sampleRate, byteRate, blockAlign, bitsPerSample, pcm);
                samples[index] = new OjmSample(index, name, ".wav", wave);
            }
            else
                skip(reader, chunkSize);
        }

        seek(reader, backgroundOffset, reader.BaseStream.Length);
        for (var index = 0; index < backgroundCount; index++)
        {
            ensureRemaining(reader, 36);
            var name = decodeName(reader.ReadBytes(32));
            var size = reader.ReadUInt32();
            ensureRemaining(reader, size);
            var id = index + 1000;
            if (size > 0 && (sampleIds == null || sampleIds.Contains(id)))
            {
                var sample = reader.ReadBytes(checked((int)size));
                samples[id] = new OjmSample(id, name, ".ogg", sample);
            }
            else
                skip(reader, size);
        }

        return new OjmArchive(samples);
    }

    private static OjmArchive readOmcLazy(BinaryReader reader, string path, bool encryptedWave, IReadOnlySet<int>? sampleIds)
    {
        var effectCount = reader.ReadUInt16();
        var backgroundCount = reader.ReadUInt16();
        var effectOffset = reader.ReadUInt32();
        var backgroundOffset = reader.ReadUInt32();
        _ = reader.ReadUInt32();

        var samples = new Dictionary<int, OjmSample>();
        var accumulatorKey = 0xff;
        var accumulatorCounter = 0;

        seek(reader, effectOffset, reader.BaseStream.Length);
        for (var index = 0; index < effectCount; index++)
        {
            ensureRemaining(reader, 56);
            var name = decodeName(reader.ReadBytes(32));
            var audioFormat = reader.ReadUInt16();
            var channelCount = reader.ReadUInt16();
            var sampleRate = reader.ReadUInt32();
            var byteRate = reader.ReadUInt32();
            var blockAlign = reader.ReadUInt16();
            var bitsPerSample = reader.ReadUInt16();
            _ = reader.ReadUInt32();
            var chunkSize = reader.ReadUInt32();
            ensureRemaining(reader, chunkSize);

            var payloadOffset = reader.BaseStream.Position;
            var initialKey = accumulatorKey;
            var initialCounter = accumulatorCounter;
            if (chunkSize > 0 && (sampleIds == null || sampleIds.Contains(index)))
            {
                samples[index] = new OjmSample(index, name, ".wav", checked((long)chunkSize + 44), () =>
                {
                    var payload = readRange(path, payloadOffset, chunkSize);
                    if (encryptedWave)
                    {
                        var sampleKey = initialKey;
                        var sampleCounter = initialCounter;
                        payload = decodeWave(payload, ref sampleKey, ref sampleCounter);
                    }

                    return buildWave(audioFormat, channelCount, sampleRate, byteRate, blockAlign, bitsPerSample, payload);
                });
            }

            if (encryptedWave)
                advanceWaveEncryption(reader, chunkSize, ref accumulatorKey, ref accumulatorCounter);
            else
                skip(reader, chunkSize);
        }

        seek(reader, backgroundOffset, reader.BaseStream.Length);
        for (var index = 0; index < backgroundCount; index++)
        {
            ensureRemaining(reader, 36);
            var name = decodeName(reader.ReadBytes(32));
            var size = reader.ReadUInt32();
            ensureRemaining(reader, size);
            var id = index + 1000;
            var payloadOffset = reader.BaseStream.Position;
            if (size > 0 && (sampleIds == null || sampleIds.Contains(id)))
                samples[id] = new OjmSample(id, name, ".ogg", size, () => readRange(path, payloadOffset, size));
            skip(reader, size);
        }

        return new OjmArchive(samples);
    }

    private static OjmArchive readM30(BinaryReader reader, IReadOnlySet<int>? sampleIds)
    {
        _ = reader.ReadUInt32();
        var encodingCode = reader.ReadUInt32();
        var sampleCount = reader.ReadUInt32();
        var sampleOffset = reader.ReadUInt32();
        var payloadSize = reader.ReadUInt32();
        _ = reader.ReadUInt32();

        if (sampleCount > maximumSampleCount || sampleOffset < 28 || sampleOffset > reader.BaseStream.Length || payloadSize > int.MaxValue)
            throw new InvalidDataException("The M30 archive header is invalid.");

        seek(reader, sampleOffset, reader.BaseStream.Length);
        var samples = new Dictionary<int, OjmSample>();

        // Several converters under-report payloadSize and some archives over-report sampleCount.
        // EOF is therefore the trustworthy boundary, while a malformed final entry only ends the table.
        for (var index = 0u; index < sampleCount && reader.BaseStream.Position + 52 <= reader.BaseStream.Length; index++)
        {
            var name = decodeName(reader.ReadBytes(32));
            var size = reader.ReadUInt32();
            var codecCode = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt32();
            var reference = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt32();

            if (size > reader.BaseStream.Length - reader.BaseStream.Position)
                break;

            var id = reference + (codecCode == 0 ? 1000 : 0);
            if (size > 0 && codecCode is 0 or 5 && (sampleIds == null || sampleIds.Contains(id)))
            {
                var sample = reader.ReadBytes(checked((int)size));
                applyM30Encryption(sample, encodingCode);
                samples.TryAdd(id, new OjmSample(id, name, ".ogg", sample));
            }
            else
                skip(reader, size);
        }

        return new OjmArchive(samples);
    }

    private static OjmArchive readM30Lazy(BinaryReader reader, string path, IReadOnlySet<int>? sampleIds)
    {
        _ = reader.ReadUInt32();
        var encodingCode = reader.ReadUInt32();
        var sampleCount = reader.ReadUInt32();
        var sampleOffset = reader.ReadUInt32();
        var payloadSize = reader.ReadUInt32();
        _ = reader.ReadUInt32();

        if (sampleCount > maximumSampleCount || sampleOffset < 28 || sampleOffset > reader.BaseStream.Length || payloadSize > int.MaxValue)
            throw new InvalidDataException("The M30 archive header is invalid.");

        seek(reader, sampleOffset, reader.BaseStream.Length);
        var samples = new Dictionary<int, OjmSample>();

        for (var index = 0u; index < sampleCount && reader.BaseStream.Position + 52 <= reader.BaseStream.Length; index++)
        {
            var name = decodeName(reader.ReadBytes(32));
            var size = reader.ReadUInt32();
            var codecCode = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt32();
            var reference = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt32();

            if (size > reader.BaseStream.Length - reader.BaseStream.Position)
                break;

            var id = reference + (codecCode == 0 ? 1000 : 0);
            var payloadOffset = reader.BaseStream.Position;
            if (size > 0 && codecCode is 0 or 5 && (sampleIds == null || sampleIds.Contains(id)))
            {
                samples.TryAdd(id, new OjmSample(id, name, ".ogg", size, () =>
                {
                    var payload = readRange(path, payloadOffset, size);
                    applyM30Encryption(payload, encodingCode);
                    return payload;
                }));
            }

            skip(reader, size);
        }

        return new OjmArchive(samples);
    }

    private static OjmArchiveIndex readOmcIndex(BinaryReader reader)
    {
        var effectCount = reader.ReadUInt16();
        var backgroundCount = reader.ReadUInt16();
        var effectOffset = reader.ReadUInt32();
        var backgroundOffset = reader.ReadUInt32();
        _ = reader.ReadUInt32();
        var ids = new HashSet<int>();

        seek(reader, effectOffset, reader.BaseStream.Length);
        for (var index = 0; index < effectCount; index++)
        {
            ensureRemaining(reader, 56);
            _ = reader.ReadBytes(32);
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt32();
            var size = reader.ReadUInt32();
            ensureRemaining(reader, size);
            if (size > 0)
                ids.Add(index);
            reader.BaseStream.Position += size;
        }

        seek(reader, backgroundOffset, reader.BaseStream.Length);
        for (var index = 0; index < backgroundCount; index++)
        {
            ensureRemaining(reader, 36);
            _ = reader.ReadBytes(32);
            var size = reader.ReadUInt32();
            ensureRemaining(reader, size);
            if (size > 0)
                ids.Add(index + 1000);
            reader.BaseStream.Position += size;
        }

        return new OjmArchiveIndex(ids);
    }

    private static OjmArchiveIndex readM30Index(BinaryReader reader)
    {
        _ = reader.ReadUInt32();
        _ = reader.ReadUInt32();
        var sampleCount = reader.ReadUInt32();
        var sampleOffset = reader.ReadUInt32();
        var payloadSize = reader.ReadUInt32();
        _ = reader.ReadUInt32();

        if (sampleCount > maximumSampleCount || sampleOffset < 28 || sampleOffset > reader.BaseStream.Length || payloadSize > int.MaxValue)
            throw new InvalidDataException("The M30 archive header is invalid.");

        seek(reader, sampleOffset, reader.BaseStream.Length);
        var ids = new HashSet<int>();

        for (var index = 0u; index < sampleCount && reader.BaseStream.Position + 52 <= reader.BaseStream.Length; index++)
        {
            _ = reader.ReadBytes(32);
            var size = reader.ReadUInt32();
            var codecCode = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt32();
            var reference = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt32();

            if (size > reader.BaseStream.Length - reader.BaseStream.Position)
                break;
            if (size > 0 && codecCode is 0 or 5)
                ids.Add(reference + (codecCode == 0 ? 1000 : 0));
            reader.BaseStream.Position += size;
        }

        return new OjmArchiveIndex(ids);
    }

    private static void applyM30Encryption(byte[] sample, uint encodingCode)
    {
        byte[]? mask = encodingCode switch
        {
            16 => [(byte)'n', (byte)'a', (byte)'m', (byte)'i'],
            32 => [(byte)'0', (byte)'4', (byte)'1', (byte)'2'],
            _ => null,
        };

        if (mask == null)
            return;

        // O2Jam only encrypts complete four-byte groups; a trailing partial group is untouched.
        for (var byteIndex = 0; byteIndex + mask.Length <= sample.Length; byteIndex += mask.Length)
        {
            for (var keyIndex = 0; keyIndex < mask.Length; keyIndex++)
                sample[byteIndex + keyIndex] ^= mask[keyIndex];
        }
    }

    private static byte[] decodeWave(byte[] encoded, ref int accumulatorKey, ref int accumulatorCounter)
    {
        var rearranged = encoded.ToArray();
        var remainder = encoded.Length % 17;
        var tableOffset = remainder * 17;
        var blockSize = encoded.Length / 17;

        for (var block = 0; block < 17; block++)
        {
            var destinationBlock = waveRearrangeTable[tableOffset + block];
            Buffer.BlockCopy(encoded, blockSize * block, rearranged, blockSize * destinationBlock, blockSize);
        }

        for (var index = 0; index < rearranged.Length; index++)
        {
            var original = rearranged[index];
            if (((accumulatorKey << accumulatorCounter) & 0x80) != 0)
                rearranged[index] = (byte)~rearranged[index];

            accumulatorCounter++;
            if (accumulatorCounter > 7)
            {
                accumulatorCounter = 0;
                accumulatorKey = original;
            }
        }

        return rearranged;
    }

    private static void advanceWaveEncryption(BinaryReader reader, uint length, ref int accumulatorKey, ref int accumulatorCounter)
    {
        var payloadStart = reader.BaseStream.Position;
        var firstKeyIndex = 7 - accumulatorCounter;

        if (length > firstKeyIndex)
        {
            var lastKeyIndex = firstKeyIndex + ((long)length - 1 - firstKeyIndex) / 8 * 8;
            var blockSize = length / 17;
            long encodedIndex;

            if (blockSize == 0 || lastKeyIndex >= blockSize * 17)
                encodedIndex = lastKeyIndex;
            else
            {
                var remainder = length % 17;
                var tableOffset = checked((int)(remainder * 17));
                var destinationBlock = checked((int)(lastKeyIndex / blockSize));
                var offsetWithinBlock = lastKeyIndex % blockSize;
                var sourceBlock = 0;

                while (sourceBlock < 17 && waveRearrangeTable[tableOffset + sourceBlock] != destinationBlock)
                    sourceBlock++;

                if (sourceBlock == 17)
                    throw new InvalidDataException("The OMC wave rearrangement table is invalid.");

                encodedIndex = sourceBlock * blockSize + offsetWithinBlock;
            }

            reader.BaseStream.Position = payloadStart + encodedIndex;
            accumulatorKey = reader.ReadByte();
        }

        accumulatorCounter = checked((int)((accumulatorCounter + length % 8) % 8));
        reader.BaseStream.Position = payloadStart + length;
    }

    private static byte[] buildWave(ushort format, ushort channels, uint sampleRate, uint byteRate, ushort blockAlign, ushort bitsPerSample, byte[] pcm)
    {
        using var stream = new MemoryStream(44 + pcm.Length);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(checked(pcm.Length + 36));
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write(format);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(pcm.Length);
        writer.Write(pcm);
        return stream.ToArray();
    }

    private static byte[] readRange(string path, long offset, uint length)
    {
        var data = new byte[checked((int)length)];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.RandomAccess);
        stream.Position = offset;
        stream.ReadExactly(data);
        return data;
    }

    private static void seek(BinaryReader reader, uint offset, long totalLength)
    {
        if (offset > totalLength)
            throw new InvalidDataException("An OJM section offset is outside the file.");
        reader.BaseStream.Position = offset;
    }

    private static void ensureRemaining(BinaryReader reader, uint length)
    {
        if (length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("An OJM sample is truncated.");
    }

    private static void skip(BinaryReader reader, uint length) => reader.BaseStream.Position += length;

    private static string decodeName(byte[] bytes)
    {
        var length = Array.IndexOf(bytes, (byte)0);
        if (length < 0)
            length = bytes.Length;
        return Encoding.ASCII.GetString(bytes, 0, length).Trim();
    }
}
