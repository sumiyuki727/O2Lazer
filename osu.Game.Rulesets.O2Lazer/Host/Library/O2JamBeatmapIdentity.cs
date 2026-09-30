using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using O2Jam.Core;

namespace osu.Game.Rulesets.O2Lazer.Host.Library;

/// <summary>
/// Produces persistent set and difficulty identities for an external OJN file.
/// </summary>
public static class O2JamBeatmapIdentity
{
    public static string FromSource(string sourceHash, O2JamDifficulty difficulty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceHash);

        var identity = $"{sourceHash.Trim().ToLowerInvariant()}:{(int)difficulty}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    public static string Md5FromSource(ReadOnlySpan<byte> source, O2JamDifficulty difficulty)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        hash.AppendData(source);
        hash.AppendData([(byte)difficulty]);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static string SetFromMd5Hashes(IEnumerable<string> difficultyMd5Hashes)
    {
        var difficulties = string.Concat(difficultyMd5Hashes.OrderBy(hash => hash, StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{O2LazerIdentity.ShortName}:{difficulties}"))).ToLowerInvariant();
    }
}
