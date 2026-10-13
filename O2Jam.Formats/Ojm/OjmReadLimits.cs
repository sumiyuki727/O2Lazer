using System;
using System.Buffers;
using System.IO;

namespace O2Jam.Formats.Ojm;

internal sealed record OjmReadLimits(int BufferedSourceBytes, int SampleBytes, long EagerBytes, long LazyBytes = 512L * 1024 * 1024)
{
    internal static OjmReadLimits Default { get; } = new(256 * 1024 * 1024, 128 * 1024 * 1024, 512L * 1024 * 1024);

    internal void CheckSampleLength(long length)
    {
        if (length > SampleBytes)
            throw new InvalidDataException("The OJM sample exceeds the supported output byte budget.");
    }

    internal MemoryStream BufferSource(Stream source)
    {
        var memory = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (true)
            {
                // Probe at most one excess byte to distinguish an exact-limit EOF before writing.
                var available = (int)Math.Min(buffer.Length, (long)BufferedSourceBytes - memory.Length + 1);
                var read = source.Read(buffer.AsSpan(0, available));
                if (read == 0)
                    return memory;

                if (memory.Length + read > BufferedSourceBytes)
                    throw new InvalidDataException("The OJM exceeds the supported non-seek source byte budget.");

                memory.Write(buffer.AsSpan(0, read));
            }
        }
        catch
        {
            memory.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

internal sealed class OjmReadBudget(OjmReadLimits limits)
{
    private long bytes;

    internal void AddSample(long length)
    {
        limits.CheckSampleLength(length);
        // Count owned outputs before payload reads and WAV wrapping, including their container bytes.
        if (length > limits.EagerBytes - bytes)
            throw new InvalidDataException("The OJM exceeds the supported eager output byte budget.");

        bytes += length;
    }
}

internal sealed class OjmLazyReadBudget(OjmReadLimits limits)
{
    private readonly object gate = new();
    private long bytes;

    internal byte[] Load(long length, Func<byte[]> loader)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        limits.CheckSampleLength(length);
        lock (gate)
        {
            // Reserve before IO: concurrent sounds must not independently spend the same allowance.
            if (length > limits.LazyBytes - bytes)
                throw new InvalidDataException("The OJM exceeds the supported lazy output byte budget.");
            bytes += length;
        }

        try
        {
            var data = loader();
            if (data.LongLength != length)
                throw new InvalidDataException("The OJM sample output does not match its indexed byte length.");

            // Successful Lazy values retain their arrays; closing a stream cannot return this allowance.
            return data;
        }
        catch
        {
            lock (gate)
                bytes -= length;
            throw;
        }
    }
}
