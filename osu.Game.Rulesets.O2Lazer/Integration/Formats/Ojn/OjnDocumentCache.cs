using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using O2Jam.Core;
using O2Jam.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;

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
                removePathEntries(canonicalPath, difficulty);

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

    internal void Invalidate(string path)
    {
        var canonicalPath = Path.GetFullPath(path);
        lock (cacheLock)
            removePathEntries(canonicalPath, null);
    }

    private void removePathEntries(string canonicalPath, O2JamDifficulty? difficulty)
    {
        var staleKeys = new List<CacheKey>();
        foreach (var key in entries.Keys)
        {
            if ((difficulty == null || key.Difficulty == difficulty)
                && string.Equals(key.Path, canonicalPath, StringComparison.OrdinalIgnoreCase))
                staleKeys.Add(key);
        }

        foreach (var key in staleKeys)
            entries.Remove(key);
    }

    private static OjnDocument read(string path, O2JamDifficulty difficulty)
    {
        using var stream = File.OpenRead(path);
        return new OjnReader(OjnMetadataEncoding.Automatic, () => OjnDirectoryEncoding.Shared.GetForFile(path)).ReadChart(stream, difficulty.ToFormat());
    }

    private readonly record struct CacheKey(string Path, long Length, long LastWriteTicks, O2JamDifficulty Difficulty);

    private sealed class CacheEntry(Lazy<OjnDocument> document)
    {
        public Lazy<OjnDocument> Document { get; } = document;
        public long LastAccess { get; set; }
    }
}
