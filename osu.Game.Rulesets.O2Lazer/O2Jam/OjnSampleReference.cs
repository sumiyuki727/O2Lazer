using System;
using System.IO;
using System.Text;
using osu.Game.Rulesets.O2Lazer.IO.ResourceStore;

namespace osu.Game.Rulesets.O2Lazer.O2Jam;

internal static class OjnSampleReference
{
    private const string relative_prefix = "@o2jam/";
    private const string resolved_prefix = "@o2jam-absolute/";

    internal static string Create(string ojmFileName, ushort sampleId) =>
        $"{relative_prefix}{encode(ojmFileName)}/{sampleId}";

    internal static bool IsReference(string? value) =>
        tryParse(value, relative_prefix, out _, out _) || tryParse(value, resolved_prefix, out _, out _);

    internal static bool TryResolve(string? value, string basePath, out string identity)
    {
        identity = null!;
        if (!tryParse(value, relative_prefix, out var encodedPath, out var sampleId))
            return false;

        var relativePath = decode(encodedPath);
        string fullPath;

        try
        {
            var baseFullPath = Path.GetFullPath(basePath);
            fullPath = Path.GetFullPath(Path.Combine(baseFullPath, relativePath));
            if (!O2LazerFileResourceStore.IsPathInsideDirectory(fullPath, baseFullPath) || !File.Exists(fullPath))
                return false;
        }
        catch (Exception)
        {
            return false;
        }

        identity = $"{resolved_prefix}{encode(fullPath)}/{sampleId}";
        return true;
    }

    internal static bool TryParseResolved(string? value, out string path, out ushort sampleId)
    {
        path = null!;
        if (!tryParse(value, resolved_prefix, out var encodedPath, out sampleId))
            return false;

        path = decode(encodedPath);
        return true;
    }

    private static bool tryParse(string? value, string prefix, out string encodedPath, out ushort sampleId)
    {
        encodedPath = null!;
        sampleId = 0;

        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var separator = value.LastIndexOf('/');
        if (separator <= prefix.Length || !ushort.TryParse(value[(separator + 1)..], out sampleId))
            return false;

        encodedPath = value[prefix.Length..separator];
        return encodedPath.Length > 0;
    }

    private static string encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static string decode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }
}
