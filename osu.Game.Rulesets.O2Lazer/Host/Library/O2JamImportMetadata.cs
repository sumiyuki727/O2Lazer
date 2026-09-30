using System;
using System.Globalization;
using System.Linq;
using O2Jam.Core;
using O2Jam.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Import;

internal enum O2JamImportMetadataStatus
{
    Legacy,
    Valid,
    Invalid,
    Unsupported,
}

internal sealed record O2JamStoredImportMetadata(
    int ProjectionVersion,
    O2JamDifficulty Difficulty,
    ushort Level,
    OjnMetadataEncoding? EncodingFallback);

/// <summary>
/// Encodes the O2Jam projection carried by native beatmap metadata, without a database model.
/// These tokens are a native host adapter, not the storage-neutral import plan.
/// </summary>
internal static class O2JamImportMetadata
{
    public const int ProjectionVersion = 20261001;
    public const int IdentityVersion = 1;
    public const string ProjectionPrefix = "o2lazer-projection:";
    public const string IdentityPrefix = "o2lazer-identity:";
    public const string ChartPrefix = "o2lazer-chart:";
    public const string LevelPrefix = "o2lazer-level:";
    public const string EncodingContextPrefix = "o2lazer-encoding-context:";

    private static readonly string[] prefixes = [ProjectionPrefix, IdentityPrefix, ChartPrefix, LevelPrefix, EncodingContextPrefix];

    public static bool OwnsToken(string token) => prefixes.Any(prefix => token.StartsWith(prefix, StringComparison.Ordinal));

    public static string Create(O2JamImportPlan plan, O2JamImportChart chart) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{ProjectionPrefix}1:{ProjectionVersion} {IdentityPrefix}{IdentityVersion} {ChartPrefix}1:{(int)chart.Difficulty} {LevelPrefix}1:{chart.Level} {EncodingContextPrefix}1:{(plan.EncodingFallback is { } encoding ? ((int)encoding).ToString(CultureInfo.InvariantCulture) : "none")}");

    public static O2JamImportMetadataStatus Read(string tags, out O2JamStoredImportMetadata? metadata)
    {
        metadata = null;
        var values = new string?[prefixes.Length];
        foreach (var token in tags.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            for (var index = 0; index < prefixes.Length; index++)
            {
                if (!token.StartsWith(prefixes[index], StringComparison.Ordinal))
                    continue;
                if (values[index] != null)
                    return O2JamImportMetadataStatus.Invalid;
                values[index] = token[prefixes[index].Length..];
                break;
            }
        }
        if (values.All(value => value == null))
            return O2JamImportMetadataStatus.Legacy;
        if (values.Any(value => value == null))
            return O2JamImportMetadataStatus.Invalid;

        var projection = values[0]!.Split(':');
        var chart = values[2]!.Split(':');
        var level = values[3]!.Split(':');
        var context = values[4]!.Split(':');
        if (projection.Length != 2 || chart.Length != 2 || level.Length != 2 || context.Length != 2
            || !tryInteger(projection[0], out var projectionFormat)
            || !tryInteger(chart[0], out var chartFormat)
            || !tryInteger(level[0], out var levelFormat)
            || !tryInteger(context[0], out var contextFormat)
            || !tryInteger(values[1]!, out var identity)
            || !tryInteger(projection[1], out var version))
            return O2JamImportMetadataStatus.Invalid;

        if (projectionFormat > 1 || chartFormat > 1 || levelFormat > 1 || contextFormat > 1
            || identity > IdentityVersion || version > ProjectionVersion)
            return O2JamImportMetadataStatus.Unsupported;
        if (projectionFormat != 1 || chartFormat != 1 || levelFormat != 1 || contextFormat != 1
            || identity != IdentityVersion || version <= 0
            || !tryInteger(chart[1], out var difficulty) || difficulty is < 0 or > 2
            || !ushort.TryParse(level[1], NumberStyles.None, CultureInfo.InvariantCulture, out var rawLevel))
            return O2JamImportMetadataStatus.Invalid;

        OjnMetadataEncoding? encoding = null;
        if (context[1] != "none")
        {
            if (!tryInteger(context[1], out var code) || !Enum.IsDefined((OjnMetadataEncoding)code))
                return O2JamImportMetadataStatus.Invalid;
            encoding = (OjnMetadataEncoding)code;
        }

        metadata = new O2JamStoredImportMetadata(version, (O2JamDifficulty)difficulty, rawLevel, encoding);
        return O2JamImportMetadataStatus.Valid;
    }

    private static bool tryInteger(string value, out int number) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
}
