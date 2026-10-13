using System;
using System.IO;
using System.Security.Cryptography;
using O2Jam.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Import;

internal sealed record O2JamSourceSnapshot(byte[] Data, string Hash, DateTimeOffset Timestamp)
{
    public static string ReadHash(string path) => ReadHash(path, OjnReadLimits.Default);

    internal static string ReadHash(string path, OjnReadLimits limits)
    {
        var timestamp = O2JamSourceTimestamp.Read(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        limits.CheckSourceLength(stream.Length);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (O2JamSourceTimestamp.Read(path) != timestamp)
            throw new IOException("The OJN changed while scanning its content.");
        return hash;
    }

    public static O2JamSourceSnapshot Read(string path) => Read(path, OjnReadLimits.Default);

    internal static O2JamSourceSnapshot Read(string path, OjnReadLimits limits)
    {
        var timestamp = O2JamSourceTimestamp.Read(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var data = limits.ReadSource(stream);
        if (O2JamSourceTimestamp.Read(path) != timestamp)
            throw new IOException("The OJN changed while preparing its import snapshot.");
        return new O2JamSourceSnapshot(data, Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(), timestamp);
    }

    public static FileStream AcquireGuard(O2JamImportPlan plan) => AcquireGuard(plan, OjnReadLimits.Default);

    internal static FileStream AcquireGuard(O2JamImportPlan plan, OjnReadLimits limits)
    {
        limits.CheckSourceLength(plan.SourceData.LongLength);
        if (plan.SourceTimestamp == null
            || !string.Equals(Convert.ToHexString(SHA256.HashData(plan.SourceData)), plan.SourceHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An OJN import requires a verified source snapshot.");
        var stream = new FileStream(plan.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            limits.CheckSourceLength(stream.Length);
            // Retain the read handle through commit so Windows cannot replace the source
            // between this content check and publishing its captured timestamp.
            if (stream.Length != plan.SourceData.LongLength || O2JamSourceTimestamp.Read(plan.SourcePath) != plan.SourceTimestamp
                || !string.Equals(Convert.ToHexString(SHA256.HashData(stream)), plan.SourceHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The OJN changed after its import snapshot was prepared.");
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
