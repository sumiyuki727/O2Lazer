using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;

namespace osu.Game.Rulesets.O2Lazer.Import;

internal sealed class O2JamRealmLibraryBackend : IO2JamLibraryBackend
{
    private readonly O2JamLibraryWriter writer;
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
        writer.BeatmapUpdated += invalidate;
        importer = new O2JamImportService(new O2JamImportPlanner(), writer);
        collections = new O2JamSourceFolderCollectionService(realm);
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public O2JamImportSummary Refresh(string path, Action<O2JamLibraryProgress>? progress, CancellationToken cancellationToken)
    {
        var paths = O2JamChartSourceScanner.Enumerate(path, cancellationToken);
        return importer.Refresh(paths, writer.GetImportedSources(),
            (processed, total) => progress?.Invoke(new O2JamLibraryProgress(processed, total)),
            (exception, sourcePath) => Logger.Error(exception, $"O2Jam refresh failed for '{sourcePath}'."),
            cancellationToken);
    }

    public void DeleteAll() => writer.DeleteAll();

    public void UpdateCollections(string path, bool enabled)
    {
        if (enabled)
            collections.Synchronise(path);
        else
            collections.DeleteFeatureCollections();
    }

    private void invalidate(BeatmapInfo beatmap)
    {
        workingBeatmaps?.Invalidate(beatmap);
        difficultyCache?.Invalidate(beatmap, beatmap);
    }

    public void Dispose() => writer.BeatmapUpdated -= invalidate;
}
