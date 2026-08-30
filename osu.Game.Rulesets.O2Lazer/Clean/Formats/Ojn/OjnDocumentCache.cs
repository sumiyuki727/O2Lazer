using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using osu.Game.Rulesets.O2Lazer.Core;

namespace osu.Game.Rulesets.O2Lazer.Formats.Ojn;

/// <summary>
/// Avoids parsing the same OJN difficulty again when song select revisits a recent entry.
/// </summary>
internal sealed class OjnDocumentCache
{
    private const int max_documents = 128;

    internal static OjnDocumentCache Shared { get; } = new();

    private readonly object cacheLock = new();
    private readonly Dictionary<CacheKey, CacheEntry> entries = [];
    private long accessSequence;

    internal OjnDocument Get(string path, O2JamDifficulty difficulty)
    {
        var canonicalPath = Path.GetFullPath(path);
        var info = new FileInfo(canonicalPath);
        var key = new CacheKey(canonicalPath, info.Length, info.LastWriteTimeUtc.Ticks, difficulty);
        CacheEntry entry;

        lock (cacheLock)
        {
            if (!entries.TryGetValue(key, out entry!))
            {
                // A changed source must not leave stale versions occupying the bounded cache.
                var staleKeys = new List<CacheKey>();
                foreach (var stale in entries.Keys)
                {
                    if (stale.Difficulty == difficulty
                        && string.Equals(stale.Path, canonicalPath, StringComparison.OrdinalIgnoreCase))
                        staleKeys.Add(stale);
                }

                foreach (var stale in staleKeys)
                    entries.Remove(stale);

                entry = new CacheEntry(
                    new Lazy<OjnDocument>(() => read(canonicalPath, difficulty), LazyThreadSafetyMode.ExecutionAndPublication));
                entries[key] = entry;
            }

            entry.LastAccess = ++accessSequence;

            while (entries.Count > max_documents)
            {
                CacheKey? victim = null;
                var oldest = long.MaxValue;

                foreach (var candidate in entries)
                {
                    if (candidate.Value.LastAccess >= oldest || candidate.Key == key)
                        continue;

                    victim = candidate.Key;
                    oldest = candidate.Value.LastAccess;
                }

                if (victim == null)
                    break;

                entries.Remove(victim.Value);
            }
        }

        try
        {
            return entry.Document.Value;
        }
        catch
        {
            lock (cacheLock)
            {
                if (entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                    entries.Remove(key);
            }

            throw;
        }
    }

    private static OjnDocument read(string path, O2JamDifficulty difficulty)
    {
        using var stream = File.OpenRead(path);
        return new OjnReader(OjnMetadataEncoding.Automatic, () => OjnDirectoryEncoding.Shared.GetForFile(path)).ReadChart(stream, difficulty);
    }

    private readonly record struct CacheKey(string Path, long Length, long LastWriteTicks, O2JamDifficulty Difficulty);

    private sealed class CacheEntry(Lazy<OjnDocument> document)
    {
        public Lazy<OjnDocument> Document { get; } = document;
        public long LastAccess { get; set; }
    }
}
