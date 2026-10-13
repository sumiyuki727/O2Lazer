using System;
using System.Buffers;
using System.IO;

namespace O2Jam.Formats.Ojn;

internal sealed record OjnReadLimits(int SourceBytes, int MaterialisedEvents)
{
    internal static OjnReadLimits Default { get; } = new(64 * 1024 * 1024, 1_000_000);

    internal void CheckSourceLength(long length)
    {
        if (length > SourceBytes)
            throw new InvalidDataException("The OJN exceeds the supported source byte budget.");
    }

    internal byte[] ReadSource(Stream source)
    {
        using var memory = BufferSource(source);
        return memory.ToArray();
    }

    internal MemoryStream BufferSource(Stream source)
    {
        if (source.CanSeek)
            CheckSourceLength(source.Length);

        var memory = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (true)
            {
                // A one-byte probe distinguishes an exact-limit EOF without buffering excess data.
                var available = (int)Math.Min(buffer.Length, (long)SourceBytes - memory.Length + 1);
                var read = source.Read(buffer.AsSpan(0, available));
                if (read == 0)
                    return memory;

                CheckSourceLength(memory.Length + read);
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

internal sealed class OjnReadBudget(OjnReadLimits limits)
{
    private int events;

    internal void AddEvent()
    {
        // Count before normalisation, which can merge LN endpoints or discard orphan releases.
        if (events >= limits.MaterialisedEvents)
            throw new InvalidDataException("The OJN exceeds the supported materialised event budget.");

        events++;
    }
}
