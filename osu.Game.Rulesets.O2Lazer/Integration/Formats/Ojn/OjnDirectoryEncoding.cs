using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using O2Jam.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;

/// <summary>
/// Shares a directory tie-breaker until native directory stamps or explicit refresh invalidate it.
/// </summary>
internal sealed class OjnDirectoryEncoding
{
    private const int maximum_samples = 96;
    private const int maximum_directories = 128;
    private const int maximum_parallel_headers = 4;
    internal static OjnDirectoryEncoding Shared { get; } = new();
    private readonly object cacheLock = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, Stream> openHeader;
    private readonly Func<string, IEnumerable<string>> enumerateFiles;
    private long generation;

    internal OjnDirectoryEncoding(Func<string, Stream>? openHeader = null,
        Func<string, IEnumerable<string>>? enumerateFiles = null)
    {
        this.openHeader = openHeader ?? (path => new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 268, FileOptions.RandomAccess));
        this.enumerateFiles = enumerateFiles ?? (directory => Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly));
    }

    internal OjnMetadataEncoding GetForFile(string path) => GetContext(path).Encoding;

    internal OjnEncodingContext GetContext(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            long stamp;
            try { stamp = getDirectoryStamp(directory); }
            catch (Exception exception) when (isFileFailure(exception)) { return unknown(directory); }

            Entry entry;
            lock (cacheLock)
            {
                if (!entries.TryGetValue(directory, out entry!) || entry.Stamp != stamp)
                {
                    if (entry != null)
                        retire(entry);
                    entry = new Entry(directory, stamp, generation);
                    entry.Result = new Lazy<OjnEncodingContext>(() => inspect(entry), LazyThreadSafetyMode.ExecutionAndPublication);
                    entries[directory] = entry;
                    while (entries.Count > maximum_directories)
                    {
                        var victim = entries.Keys.First();
                        retire(entries[victim]);
                        entries.Remove(victim);
                    }
                }
            }

            OjnEncodingContext context;
            try { context = entry.Result.Value; }
            catch (Exception exception) when (isFileFailure(exception))
            {
                remove(directory, entry);
                continue;
            }

            // Completed work must not republish a context retired by refresh or eviction.
            lock (cacheLock)
            {
                if (entry.Generation != generation || !entries.TryGetValue(directory, out var current)
                    || !ReferenceEquals(current, entry))
                    continue;
                if (!context.Reusable)
                    entries.Remove(directory);
                return context;
            }
        }
        return unknown(directory);
    }

    internal bool IsSameGeneration(OjnEncodingContext context) =>
        context.Active && context.Generation == Volatile.Read(ref generation);

    internal bool IsCurrent(OjnEncodingContext context)
    {
        if (!context.Reusable || !IsSameGeneration(context))
            return false;
        try
        {
            if (getDirectoryStamp(context.Directory) == context.DirectoryWriteTicks)
                return IsSameGeneration(context);
        }
        catch (Exception exception) when (isFileFailure(exception)) { }

        // A reuse query only rejects stale work. Header inspection belongs to a new preparation.
        lock (cacheLock)
        {
            context.Retire();
            if (entries.TryGetValue(context.Directory, out var entry) && entry.Result.IsValueCreated
                && ReferenceEquals(entry.Result.Value, context))
                entries.Remove(context.Directory);
        }
        return false;
    }

    internal void Clear()
    {
        lock (cacheLock)
        {
            generation++;
            foreach (var entry in entries.Values)
                retire(entry);
            entries.Clear();
        }
    }

    private OjnEncodingContext inspect(Entry entry)
    {
        var paths = enumerateFiles(entry.Directory)
            .Where(path => string.Equals(Path.GetExtension(path), ".ojn", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var count = Math.Min(paths.Length, maximum_samples);
        var samples = new string[count];
        var stamps = new SourceStamp?[count];
        var complete = true;
        for (var index = 0; index < count; index++)
        {
            // Preserve the existing spread across the entire pack, including unselected names.
            samples[index] = paths[count <= 1 ? 0 : (int)((long)index * (paths.Length - 1) / (count - 1))];
            try { stamps[index] = getSourceStamp(samples[index]); }
            catch (Exception exception) when (isFileFailure(exception)) { complete = false; }
        }

        var votes = new OjnMetadataEncoding[count];
        var readable = new bool[count];
        // The measured first-use cost is repeated file opening. Bound overlap without changing
        // which samples vote, and wait for every owned stream before validating or publishing.
        Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = maximum_parallel_headers },
            () => new byte[268], (index, _, header) =>
            {
                try
                {
                    using (var stream = openHeader(samples[index]))
                    {
                        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
                        votes[index] = OjnReader.InspectHeaderEncoding(header.AsSpan(0, read));
                    }
                    readable[index] = true;
                }
                catch (Exception exception) when (isFileFailure(exception)) { }
                return header;
            }, _ => { });
        complete &= readable.All(value => value);

        // Validate the initial votes together; ordinary edits during inspection must not cache a mixed result.
        for (var index = 0; index < count; index++)
            if (stamps[index] is { } stamp && getSourceStamp(samples[index]) != stamp)
                throw new IOException("The OJN sample changed while resolving its directory encoding.");
        if (getDirectoryStamp(entry.Directory) != entry.Stamp)
            throw new IOException("The OJN directory changed while resolving its encoding.");

        // The directory vote only breaks field ties; it never outranks stronger field evidence.
        var korean = votes.Count(value => value == OjnMetadataEncoding.Cp949);
        var chinese = votes.Count(value => value == OjnMetadataEncoding.Gbk);
        var total = korean + chinese;
        var encoding = total < 4 ? OjnMetadataEncoding.Automatic
            : korean >= total * 0.9 ? OjnMetadataEncoding.Cp949
            : chinese >= total * 0.9 ? OjnMetadataEncoding.Gbk : OjnMetadataEncoding.Automatic;
        return new OjnEncodingContext(entry.Directory, entry.Stamp, encoding, entry.Generation, complete);
    }

    private static long getDirectoryStamp(string directory)
    {
        var info = new DirectoryInfo(directory);
        if (!info.Exists)
            throw new DirectoryNotFoundException("The OJN source directory is unavailable.");
        return info.LastWriteTimeUtc.Ticks;
    }

    private static SourceStamp getSourceStamp(string path)
    {
        var info = new FileInfo(path);
        return new SourceStamp(info.Length, info.LastWriteTimeUtc.Ticks);
    }

    private OjnEncodingContext unknown(string directory) =>
        new(directory, 0, OjnMetadataEncoding.Automatic, Volatile.Read(ref generation), false);

    private void remove(string directory, Entry entry)
    {
        lock (cacheLock)
        {
            if (entries.TryGetValue(directory, out var current) && ReferenceEquals(current, entry))
            {
                retire(entry);
                entries.Remove(directory);
            }
        }
    }

    private static void retire(Entry entry)
    {
        if (entry.Result.IsValueCreated && entry.Result.Value.Reusable)
            entry.Result.Value.Retire();
    }

    private static bool isFileFailure(Exception exception) => exception is IOException or UnauthorizedAccessException;
    private readonly record struct SourceStamp(long Length, long LastWriteTicks);
    private sealed class Entry(string directory, long stamp, long generation)
    {
        internal string Directory { get; } = directory;
        internal long Stamp { get; } = stamp;
        internal long Generation { get; } = generation;
        internal Lazy<OjnEncodingContext> Result { get; set; } = null!;
    }
}
