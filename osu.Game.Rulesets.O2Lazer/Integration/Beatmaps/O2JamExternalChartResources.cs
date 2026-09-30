using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using O2Jam.Core;
using O2Jam.Formats.Ojm;
using O2Jam.Formats.Ojn;
using osu.Framework.Logging;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojm;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps;

/// <summary>
/// Owns the source snapshot behind one external working beatmap, independently of native audio objects.
/// </summary>
internal sealed class O2JamExternalChartResources
{
    private readonly string chartPath;
    private readonly O2JamDifficulty difficulty;
    private readonly Lazy<OjnDocument> document;
    private readonly Lazy<Task<OjmArchive>> archive;
    private ArchiveSourceStamp? loadedArchiveStamp;

    internal OjnDocument Document => document.Value;
    internal Task<OjmArchive> Archive => archive.Value;

    internal O2JamExternalChartResources(string chartPath, O2JamDifficulty difficulty)
    {
        this.chartPath = chartPath;
        this.difficulty = difficulty;
        document = new Lazy<OjnDocument>(readDocument, true);
        // Switching rulesets creates many carousel wrappers; only the selected chart should
        // start indexing OJM so speculative panels cannot stall its UI transition.
        archive = new Lazy<Task<OjmArchive>>(() => Task.Run(readArchive), true);
    }

    internal bool CanReuse
    {
        get
        {
            if (!archive.IsValueCreated)
                return true;

            var task = archive.Value;
            if (task.IsFaulted || task.IsCanceled)
                return false;

            if (!task.IsCompletedSuccessfully)
                return true;

            var loaded = Volatile.Read(ref loadedArchiveStamp);
            if (loaded == null)
                return false;

            try
            {
                if (loaded != captureArchiveStamp())
                    return false;

                // Manual refresh can invalidate an archive whose bytes changed without changing
                // its file stamp. The old working beatmap must not retain its sample skin then.
                return loaded.Path == null || OjmArchiveCache.Shared.IsCurrentArchive(chartPath, task.Result);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    private OjnDocument readDocument() => OjnDocumentCache.Shared.Get(chartPath, difficulty);

    private OjmArchive readArchive()
    {
        var started = Stopwatch.GetTimestamp();
        var stamp = captureArchiveStamp();
        Volatile.Write(ref loadedArchiveStamp, stamp);
        if (stamp.Path == null)
        {
            Logger.Log($"O2Lazer found no OJM archive for {Path.GetFileName(chartPath)}.", level: LogLevel.Verbose);
            return new OjmArchive(new Dictionary<int, OjmSample>());
        }

        // Indexing the full archive is cheap because payloads remain lazy. Keeping every sample
        // available lets matching BGM schedules transfer the live track between difficulties.
        var result = OjmArchiveCache.Shared.GetAll(chartPath, stamp.Path);
        Logger.Log(
            $"O2Lazer indexed OJM {Path.GetFileName(stamp.Path)} for {Path.GetFileName(chartPath)} in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:N1} ms ({result.Samples.Count} samples).",
            level: LogLevel.Verbose);
        return result;
    }

    private ArchiveSourceStamp captureArchiveStamp()
    {
        var resourceName = document.Value.Metadata.OjmFileName;
        if (string.IsNullOrWhiteSpace(resourceName))
            resourceName = Path.ChangeExtension(Path.GetFileName(chartPath), ".ojm");

        if (!O2JamExternalChart.TryResolveResource(chartPath, resourceName, out var ojmPath))
        {
            var fallback = Path.ChangeExtension(Path.GetFileName(chartPath), ".ojm");
            if (!O2JamExternalChart.TryResolveResource(chartPath, fallback, out ojmPath))
                return new ArchiveSourceStamp(null, 0, 0);
        }

        var file = new FileInfo(ojmPath);
        return new ArchiveSourceStamp(ojmPath, file.Length, file.LastWriteTimeUtc.Ticks);
    }

    private sealed record ArchiveSourceStamp(string? Path, long Length, long LastWriteTicks);
}
