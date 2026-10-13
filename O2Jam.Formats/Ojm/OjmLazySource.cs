using System;
using System.IO;
using System.Threading;

namespace O2Jam.Formats.Ojm;

internal sealed class OjmLazySource
{
    private readonly string path;
    private readonly (long Length, DateTime LastWrite) indexed;
    private int invalidated;
    internal bool IsInvalidated => Volatile.Read(ref invalidated) != 0;
    internal OjmLazySource(string path, FileStream stream)
    {
        this.path = path;
        indexed = (stream.Length, File.GetLastWriteTimeUtc(stream.SafeFileHandle));
    }
    internal void Validate(FileStream stream)
    {
        if (IsInvalidated || indexed != (stream.Length, File.GetLastWriteTimeUtc(stream.SafeFileHandle)))
        {
            Interlocked.Exchange(ref invalidated, 1);
            throw new IOException("The indexed OJM source is no longer current.");
        }
    }
    internal byte[] ReadRange(long offset, uint length)
    {
        if (IsInvalidated)
            throw new IOException("The indexed OJM source is no longer current.");
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.RandomAccess);
            Validate(stream);
            var data = new byte[checked((int)length)];
            stream.Position = offset;
            stream.ReadExactly(data);
            Validate(stream);
            return data;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Interlocked.Exchange(ref invalidated, 1);
            throw;
        }
    }
}
