using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets.O2Lazer.Difficulty;

namespace osu.Game.Rulesets.O2Lazer.Import;

internal sealed class O2JamRealmLibraryBackend : IO2JamLibraryBackend, IO2JamConcurrentDifficultyBackend
{
    private readonly O2JamLibraryWriter writer;
    private readonly O2JamLibraryWriter difficultyWriter;
    private O2JamLibraryDifficultyPipeline? difficultyWork;
    private readonly O2JamImportService importer;
    private readonly O2JamSourceFolderCollectionService collections;
    private readonly IWorkingBeatmapCache? workingBeatmaps;
    private readonly BeatmapDifficultyCache? difficultyCache;

    public O2JamRealmLibraryBackend(RealmAccess realm, Storage storage,
                                   IWorkingBeatmapCache? workingBeatmaps, BeatmapDifficultyCache? difficultyCache)
    {
        this.workingBeatmaps = workingBeatmaps;
        this.difficultyCache = difficultyCache;
        writer = new O2JamLibraryWriter(realm, storage);
        writer.BeatmapUpdated += invalidateWorkingBeatmap;
        writer.BeatmapUpdated += invalidateDifficulty;
        // Independent adapters keep mutable notification/recovery state on its owning worker.
        // Native RealmAccess supplies thread-local models and serialises the write transactions.
        difficultyWriter = new O2JamLibraryWriter(realm, storage);
        difficultyWriter.BeatmapUpdated += invalidateWorkingBeatmap;
        difficultyWriter.BeatmapUpdated += invalidateDifficulty;
        importer = new O2JamImportService(new O2JamImportPlanner(), writer);
        collections = new O2JamSourceFolderCollectionService(realm);
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public O2JamImportSummary Refresh(string path, Action<O2JamLibraryProgress>? progress, CancellationToken cancellationToken,
                                     O2JamLibraryRefreshMode mode = O2JamLibraryRefreshMode.Update)
    {
        new O2JamLibraryProgress(0, 0, O2JamLibraryStage.ScanningSources).Publish(progress);
        Logger.Log("O2Jam refresh: scanning source directory.");
        var paths = O2JamChartSourceScanner.Enumerate(path, cancellationToken,
            found => new O2JamLibraryProgress(found, 0, O2JamLibraryStage.ScanningSources).Publish(progress));
        Logger.Log($"O2Jam refresh: found {paths.Length} sources; reading imported metadata.");
        new O2JamLibraryProgress(0, 0, O2JamLibraryStage.ReadingImportedMetadata).Publish(progress);
        var importedSources = writer.GetImportedSources(cancellationToken, progress, mode);
        Logger.Log($"O2Jam refresh: indexed {importedSources.Count} imported sources; starting import.");
        return importer.Refresh(paths, importedSources,
            (processed, total) => progress?.Invoke(new O2JamLibraryProgress(processed, total)),
            (exception, sourcePath) => Logger.Error(exception, $"O2Jam refresh failed for '{sourcePath}'."),
            cancellationToken, progress, mode, requests =>
            {
                if (difficultyWork == null)
                    return;
                foreach (var source in writer.GetCommittedDifficultySources(requests))
                    difficultyWork.Offer(source);
            });
    }

    public void DeleteAll(Action<O2JamLibraryProgress>? progress = null, CancellationToken cancellationToken = default) => writer.DeleteAll(progress, cancellationToken);

    public IDisposable StartDifficultyProcessing(Action<O2JamLibraryProgress>? progress, CancellationToken cancellationToken)
    {
        if (difficultyWork != null)
            throw new InvalidOperationException("A difficulty worker is already active.");
        difficultyWork = new O2JamLibraryDifficultyPipeline(difficultyWriter, progress, cancellationToken);
        return new DifficultySession(this, difficultyWork);
    }

    public void UpdateCollections(string path, bool enabled)
    {
        if (enabled)
            collections.Synchronise(path);
        else
            collections.DeleteFeatureCollections();
    }

    public (int Failed, int PendingNotifications) CalculateDifficulties(string path, Action<O2JamLibraryProgress>? progress, CancellationToken cancellationToken)
    {
        difficultyWork?.Complete();
        difficultyWriter.RetryNotifications();
        var pending = writer.PendingNotifications;
        var failed = new O2JamLibraryDifficultyProcessor(difficultyWriter).Process(path, progress, cancellationToken,
            difficultyWork?.Completed ?? 0, difficultyWork?.FailedSources);
        return (failed, Math.Max(0, writer.PendingNotifications - pending) + difficultyWriter.PendingNotifications);
    }

    private void invalidateWorkingBeatmap(BeatmapInfo beatmap) => workingBeatmaps?.Invalidate(beatmap);
    private void invalidateDifficulty(BeatmapInfo beatmap) => difficultyCache?.Invalidate(beatmap, beatmap);

    public void Dispose()
    {
        difficultyWork?.Dispose();
        difficultyWork = null;
        writer.BeatmapUpdated -= invalidateWorkingBeatmap;
        writer.BeatmapUpdated -= invalidateDifficulty;
        difficultyWriter.BeatmapUpdated -= invalidateWorkingBeatmap;
        difficultyWriter.BeatmapUpdated -= invalidateDifficulty;
    }

    private sealed class DifficultySession(O2JamRealmLibraryBackend owner, O2JamLibraryDifficultyPipeline work) : IDisposable
    {
        public void Dispose()
        {
            try { work.Dispose(); }
            finally { owner.difficultyWork = null; }
        }
    }
}
