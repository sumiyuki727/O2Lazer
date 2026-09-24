using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace osu.Game.Rulesets.O2Lazer.Formats.Ojm;

/// <summary>
/// Retains a small number of indexed OJM archives across WorkingBeatmap instances.
/// Sample payloads remain lazy even when song select starts indexing a newly selected archive early.
/// </summary>
internal sealed class OjmArchiveCache
{
    private const int default_max_entries = 12;
    private const long default_max_decoded_bytes = 384 * 1024 * 1024;

    internal static OjmArchiveCache Shared { get; } = new(default_max_entries, default_max_decoded_bytes, loadArchive);

    private readonly int maxEntries;
    private readonly long maxDecodedBytes;
    private readonly Func<string, IReadOnlySet<int>?, OjmArchive> loader;
    private readonly object cacheLock = new();
    private readonly Dictionary<CacheKey, Entry> entries = [];
    private long accessSequence;

    internal OjmArchiveCache(
        int maxEntries,
        long maxDecodedBytes,
        Func<string, IReadOnlySet<int>?, OjmArchive> loader)
    {
        if (maxEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        if (maxDecodedBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDecodedBytes));

        this.maxEntries = maxEntries;
        this.maxDecodedBytes = maxDecodedBytes;
        this.loader = loader;
    }

    internal OjmArchive Get(string sourcePath, string archivePath, IReadOnlySet<int> sampleIds)
        => get(sourcePath, archivePath, sampleIds);

    internal OjmArchive GetAll(string sourcePath, string archivePath)
        => get(sourcePath, archivePath, null);

    private OjmArchive get(string sourcePath, string archivePath, IReadOnlySet<int>? sampleIds)
    {
        var canonicalSource = Path.GetFullPath(sourcePath);
        var canonicalArchive = Path.GetFullPath(archivePath);
        var sourceSnapshot = FileSnapshot.From(canonicalSource);
        var archiveSnapshot = FileSnapshot.From(canonicalArchive);
        var requestedSamples = sampleIds?.ToHashSet();
        var key = new CacheKey(canonicalSource, createSampleKey(requestedSamples));
        Entry entry;

        lock (cacheLock)
        {
            if (!entries.TryGetValue(key, out entry!)
                || !entry.Matches(canonicalArchive, sourceSnapshot, archiveSnapshot, requestedSamples))
            {
                entry = new Entry(
                    key,
                    canonicalSource,
                    canonicalArchive,
                    sourceSnapshot,
                    archiveSnapshot,
                    requestedSamples,
                    loader);
                entries[key] = entry;
            }

            entry.LastAccess = ++accessSequence;
        }

        OjmArchive archive;
        try
        {
            archive = entry.Archive.Value;
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

        lock (cacheLock)
        {
            if (entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            {
                entry.DecodedByteLength = archive.DecodedByteLength;
                trim(entry);
            }
        }

        return archive;
    }

    private void trim(Entry retained)
    {
        while (entries.Count > maxEntries || entries.Values.Sum(entry => entry.DecodedByteLength) > maxDecodedBytes)
        {
            var victim = entries.Values
                                .Where(entry => !ReferenceEquals(entry, retained))
                                .MinBy(entry => entry.LastAccess);
            if (victim == null)
                return;

            entries.Remove(victim.Key);
        }
    }

    private static string createSampleKey(IReadOnlySet<int>? sampleIds) =>
        sampleIds == null ? "*" : string.Join(',', sampleIds.Order());

    private static OjmArchive loadArchive(string path, IReadOnlySet<int>? sampleIds)
        => new OjmReader().ReadLazy(path, sampleIds);

    private readonly record struct FileSnapshot(long Length, DateTime LastWriteTimeUtc)
    {
        public static FileSnapshot From(string path)
        {
            var info = new FileInfo(path);
            return new FileSnapshot(info.Length, info.LastWriteTimeUtc);
        }
    }

    private readonly record struct CacheKey(string SourcePath, string Samples);

    private sealed class Entry
    {
        public CacheKey Key { get; }
        public string SourcePath { get; }
        public string ArchivePath { get; }
        public FileSnapshot SourceSnapshot { get; }
        public FileSnapshot ArchiveSnapshot { get; }
        public IReadOnlySet<int>? SampleIds { get; }
        public Lazy<OjmArchive> Archive { get; }
        public long LastAccess { get; set; }
        public long DecodedByteLength { get; set; }

        public Entry(
            CacheKey key,
            string sourcePath,
            string archivePath,
            FileSnapshot sourceSnapshot,
            FileSnapshot archiveSnapshot,
            IReadOnlySet<int>? sampleIds,
            Func<string, IReadOnlySet<int>?, OjmArchive> loader)
        {
            Key = key;
            SourcePath = sourcePath;
            ArchivePath = archivePath;
            SourceSnapshot = sourceSnapshot;
            ArchiveSnapshot = archiveSnapshot;
            SampleIds = sampleIds;
            Archive = new Lazy<OjmArchive>(
                () => loader(ArchivePath, SampleIds),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public bool Matches(
            string archivePath,
            FileSnapshot sourceSnapshot,
            FileSnapshot archiveSnapshot,
            IReadOnlySet<int>? sampleIds) =>
            string.Equals(ArchivePath, archivePath, StringComparison.OrdinalIgnoreCase)
            && SourceSnapshot == sourceSnapshot
            && ArchiveSnapshot == archiveSnapshot
            && (SampleIds == null ? sampleIds == null : sampleIds != null && SampleIds.SetEquals(sampleIds));
    }
}
