using System;
using System.Security.Cryptography;
using System.Text;

namespace osu.Game.Rulesets.O2Lazer.Core;

/// <summary>
/// Produces the persistent identity of one difficulty inside an external OJN file.
/// </summary>
public static class O2JamBeatmapIdentity
{
    public static string FromSource(string sourceHash, O2JamDifficulty difficulty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceHash);

        var identity = $"{sourceHash.Trim().ToLowerInvariant()}:{(int)difficulty}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }
}
