using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace osu.Game.Rulesets.O2Lazer.Formats.Ojn;

/// <summary>
/// Supplies a conservative tie-breaker for short metadata that is valid in multiple code pages.
/// </summary>
internal sealed class OjnDirectoryEncoding
{
    private const int maximum_samples = 96;
    private const int maximum_directories = 128;

    internal static OjnDirectoryEncoding Shared { get; } = new();

    private readonly object cacheLock = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);

    internal OjnMetadataEncoding GetForFile(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        DateTime stamp;
        try
        {
            stamp = Directory.GetLastWriteTimeUtc(directory);
        }
        catch (IOException)
        {
            return OjnMetadataEncoding.Automatic;
        }
        catch (UnauthorizedAccessException)
        {
            return OjnMetadataEncoding.Automatic;
        }
        Entry entry;
        lock (cacheLock)
        {
            if (!entries.TryGetValue(directory, out entry!) || entry.Stamp != stamp)
            {
                if (entries.Count >= maximum_directories)
                    entries.Remove(entries.Keys.First());
                entries[directory] = entry = new Entry(stamp, new Lazy<OjnMetadataEncoding>(
                    () => inspectDirectory(directory), LazyThreadSafetyMode.ExecutionAndPublication));
            }
        }

        return entry.Encoding.Value;
    }

    internal void Clear()
    {
        lock (cacheLock)
            entries.Clear();
    }

    private static OjnMetadataEncoding inspectDirectory(string directory)
    {
        string[] paths;
        try
        {
            paths = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                             .Where(path => string.Equals(Path.GetExtension(path), ".ojn", StringComparison.OrdinalIgnoreCase))
                             .Order(StringComparer.OrdinalIgnoreCase)
                             .ToArray();
        }
        catch (IOException)
        {
            return OjnMetadataEncoding.Automatic;
        }
        catch (UnauthorizedAccessException)
        {
            return OjnMetadataEncoding.Automatic;
        }

        var korean = 0;
        var chinese = 0;
        var count = Math.Min(paths.Length, maximum_samples);
        var header = new byte[268];
        for (var index = 0; index < count; index++)
        {
            // Spread samples across the pack instead of letting the first few song IDs decide it.
            var path = paths[count <= 1 ? 0 : (int)((long)index * (paths.Length - 1) / (count - 1))];
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, header.Length, FileOptions.RandomAccess);
                var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
                switch (OjnReader.InspectHeaderEncoding(header.AsSpan(0, read)))
                {
                    case OjnMetadataEncoding.Cp949:
                        korean++;
                        break;

                    case OjnMetadataEncoding.Gbk:
                        chinese++;
                        break;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        // Abstain on mixed packs and tiny samples. A directory hint must not silently force every
        // string to the same code page, and never outranks evidence in the field being decoded.
        var total = korean + chinese;
        if (total < 4)
            return OjnMetadataEncoding.Automatic;
        if (korean >= total * 0.9)
            return OjnMetadataEncoding.Cp949;
        if (chinese >= total * 0.9)
            return OjnMetadataEncoding.Gbk;
        return OjnMetadataEncoding.Automatic;
    }

    private sealed record Entry(DateTime Stamp, Lazy<OjnMetadataEncoding> Encoding);
}
