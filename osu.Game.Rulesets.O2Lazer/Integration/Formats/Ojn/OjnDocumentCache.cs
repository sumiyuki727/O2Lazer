using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using O2Jam.Core;
using O2Jam.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;

/// <summary>
/// Reuses a selected difficulty only while its source and used directory context remain valid.
/// </summary>
internal sealed class OjnDocumentCache
{
    private const int max_documents = 128;
    internal static OjnDocumentCache Shared { get; } = new();
    private readonly object cacheLock = new();
    private readonly Dictionary<CacheKey, CacheEntry> entries = [];
    private readonly OjnDirectoryEncoding directoryEncoding;
    private readonly Func<string, Stream> openSource;
    private long accessSequence;

    internal OjnDocumentCache(OjnDirectoryEncoding? directoryEncoding = null, Func<string, Stream>? openSource = null)
    {
        this.directoryEncoding = directoryEncoding ?? OjnDirectoryEncoding.Shared;
        this.openSource = openSource ?? File.OpenRead;
    }

    internal OjnDocument Get(string path, O2JamDifficulty difficulty)
    {
        var canonicalPath = Path.GetFullPath(path);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var key = getKey(canonicalPath, difficulty);
            CacheEntry entry;
            lock (cacheLock)
            {
                if (!entries.TryGetValue(key, out entry!))
                {
                    removePathEntries(canonicalPath, difficulty);
                    entry = new CacheEntry(new Lazy<ParsedDocument>(() => read(canonicalPath, difficulty), LazyThreadSafetyMode.ExecutionAndPublication));
                    entries[key] = entry;
                }
                entry.LastAccess = ++accessSequence;
                trim(key);
            }

            ParsedDocument parsed;
            bool valid;
            try
            {
                parsed = entry.Document.Value;
                valid = getKey(canonicalPath, difficulty) == key && contextIsCurrent(parsed);
            }
            catch
            {
                remove(key, entry);
                throw;
            }
            if (!valid)
            {
                remove(key, entry);
                continue;
            }
            lock (cacheLock)
            {
                if (!entries.TryGetValue(key, out var current) || !ReferenceEquals(current, entry)
                    || (parsed.Context != null && !directoryEncoding.IsSameGeneration(parsed.Context)))
                    continue;
                // An unavailable/partial context preserves normal Reader fallback for this read,
                // but cannot permanently cache a temporary directory failure.
                if (parsed.Context is { Reusable: false })
                    entries.Remove(key);
                return parsed.Document;
            }
        }
        throw new IOException("The OJN source or encoding context changed repeatedly while loading.");
    }

    internal bool IsCurrent(string path, O2JamDifficulty difficulty, OjnDocument document)
    {
        try
        {
            var canonicalPath = Path.GetFullPath(path);
            var key = getKey(canonicalPath, difficulty);
            CacheEntry? entry;
            lock (cacheLock)
            {
                if (!entries.TryGetValue(key, out entry) || !entry.Document.IsValueCreated)
                    return false;
            }
            var parsed = entry.Document.Value;
            if (!ReferenceEquals(parsed.Document, document) || parsed.Context is { Reusable: false }
                || !contextIsCurrent(parsed) || getKey(canonicalPath, difficulty) != key)
                return false;
            lock (cacheLock)
                return entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry)
                    && (parsed.Context == null || directoryEncoding.IsSameGeneration(parsed.Context));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    internal void Invalidate(string path)
    {
        var canonicalPath = Path.GetFullPath(path);
        lock (cacheLock)
            removePathEntries(canonicalPath, null);
    }

    private bool contextIsCurrent(ParsedDocument parsed) => parsed.Context == null
        || (parsed.Context.Reusable ? directoryEncoding.IsCurrent(parsed.Context) : directoryEncoding.IsSameGeneration(parsed.Context));

    private ParsedDocument read(string path, O2JamDifficulty difficulty)
    {
        using var stream = openSource(path);
        OjnEncodingContext? context = null;
        var document = new OjnReader(OjnMetadataEncoding.Automatic, () =>
        {
            context = directoryEncoding.GetContext(path);
            return context.Encoding;
        }).ReadChart(stream, difficulty.ToFormat());
        return new ParsedDocument(document, context);
    }

    private static CacheKey getKey(string path, O2JamDifficulty difficulty)
    {
        var info = new FileInfo(path);
        return new CacheKey(path, info.Length, info.LastWriteTimeUtc.Ticks, difficulty);
    }

    private void remove(CacheKey key, CacheEntry entry)
    {
        lock (cacheLock)
        {
            if (entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                entries.Remove(key);
        }
    }

    private void removePathEntries(string path, O2JamDifficulty? difficulty)
    {
        var staleKeys = new List<CacheKey>();
        foreach (var key in entries.Keys)
            if ((difficulty == null || key.Difficulty == difficulty) && string.Equals(key.Path, path, StringComparison.OrdinalIgnoreCase))
                staleKeys.Add(key);
        foreach (var key in staleKeys)
            entries.Remove(key);
    }

    private void trim(CacheKey key)
    {
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

    private readonly record struct CacheKey(string Path, long Length, long LastWriteTicks, O2JamDifficulty Difficulty);
    private sealed record ParsedDocument(OjnDocument Document, OjnEncodingContext? Context);
    private sealed class CacheEntry(Lazy<ParsedDocument> document)
    {
        internal Lazy<ParsedDocument> Document { get; } = document;
        internal long LastAccess { get; set; }
    }
}
