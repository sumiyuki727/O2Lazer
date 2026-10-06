using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace osu.Game.Rulesets.O2Lazer.Import;

internal static class O2JamChartSourceScanner
{
    public static string[] Enumerate(string path, CancellationToken cancellationToken, Action<int>? progress = null)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
            BufferSize = 64 * 1024,
        };
        var charts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(path, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(Path.GetExtension(file), ".ojn", StringComparison.OrdinalIgnoreCase))
            {
                if (charts.Add(file))
                    progress?.Invoke(charts.Count);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return charts.Order(StringComparer.OrdinalIgnoreCase).ThenBy(path => path, StringComparer.Ordinal).ToArray();
    }

}
