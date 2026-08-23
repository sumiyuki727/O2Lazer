using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Database;
using osu.Game.Models;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.O2Jam;
using Realms;

namespace osu.Game.Rulesets.O2Lazer.IO.Import;

public partial class O2LazerFileImporter(RealmAccess realm, Storage storage, INotificationOverlay? notifications = null, BeatmapManager? beatmaps = null) : ICanAcceptFiles
{
    private const int max_parallel_imports = 8;
    private const int refresh_batch_size = 256;

    private static readonly Regex o2jam_id_pattern = new(
        @"^o2ma(\d+)\.(?:ojn|ojm|m30)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly EnumerationOptions library_enumeration_options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public IEnumerable<string> HandledExtensions => Constant.O2LAZER_EXTENSIONS;

    public Task Import(params string[] paths)
    {
        if (paths.Length == 0)
            return Task.CompletedTask;

        var notification = new ProgressNotification
        {
            Text = O2LazerStrings.ImportInitialising,
            State = ProgressNotificationState.Active,
            CancelRequested = () => true,
        };
        notifications?.Post(notification);

        return Import(paths, notification, false);
    }

    public Task Import(ImportTask[] tasks, ImportParameters parameters = default)
        => Import(tasks.Select(t => t.Path).ToArray());

    private Task Import(string[] paths, ProgressNotification notification, bool isRefresh) =>
        !checkRulesetAvailable(notification)
            ? Task.CompletedTask
            : Task.Run(() => runImportPipeline(notification, paths, isRefresh));

    /// <summary>
    /// Re-reads an O2Jam source folder, marks orphaned sets as pending deletion, and
    /// refreshes source-folder collections when the corresponding setting is enabled.
    /// Uses a single refresh notification so the user only sees scan progress and completion.
    /// </summary>
    public async Task Refresh(string path)
    {
        var notification = new ProgressNotification
        {
            Text = O2LazerStrings.Refreshing,
            State = ProgressNotificationState.Active,
        };
        notifications?.Post(notification);

        await Import([path], notification, true).ConfigureAwait(false);
        CleanupOrphanedSets(notification);

        if (O2LazerRulesetRuntime.SyncSourceFolderCollections)
            UpdateSourceFolderCollections();

        if (notification.State != ProgressNotificationState.Cancelled)
        {
            notification.CompletionText = O2LazerStrings.RefreshComplete;
            notification.State = ProgressNotificationState.Completed;
        }
    }

    /// <summary>
    ///     Scans all O2LAZER beatmap sets and marks those whose <c>Source</c> directory no longer exists
    ///     on disk as <see cref="BeatmapSetInfo.DeletePending" />.  These are "orphaned" sets — the
    ///     user deleted or moved the original O2LAZER pack directory, so audio resources are inaccessible.
    /// </summary>
    /// <remarks>
    ///     Only affects sets imported in external-audio mode (where <c>Metadata.Source</c> is set to
    ///     the original chart directory).  Sets imported before this feature (empty <c>Source</c>)
    ///     are skipped.
    /// </remarks>
    /// <returns>The number of sets marked as orphaned.</returns>
    public int CleanupOrphanedSets() => CleanupOrphanedSets(null);

    private int CleanupOrphanedSets(ProgressNotification? provided)
    {
        var notification = provided ?? new ProgressNotification
        {
            Text = O2LazerStrings.ScanningOrphans,
            State = ProgressNotificationState.Active,
        };

        if (provided == null)
            notifications?.Post(notification);

        try
        {
            var deleted = realm.Write(r =>
            {
                var orphans = r.All<BeatmapSetInfo>()
                    .AsEnumerable()
                    .Where(s => !s.DeletePending)
                    .Where(s => s.Beatmaps.Any(b => b.Ruleset.ShortName == Constant.SHORT_NAME))
                    .Where(s =>
                    {
                        var source = s.Beatmaps.FirstOrDefault(b => !string.IsNullOrEmpty(b.Metadata.Source))?.Metadata.Source;
                        if (string.IsNullOrEmpty(source) || !Directory.Exists(source))
                            return true;

                        var chartFile = s.Files.FirstOrDefault(f => Constant.IsChartFile(f.Filename))?.Filename;
                        return string.IsNullOrEmpty(chartFile) || !File.Exists(Path.Combine(source, chartFile));
                    })
                    .ToArray();

                foreach (var set in orphans)
                {
                    var title = set.Metadata.Title;
                    var source = set.Beatmaps.FirstOrDefault(b => !string.IsNullOrEmpty(b.Metadata.Source))?.Metadata.Source ?? "(unknown)";
                    O2LazerLogger.Log($"O2LAZER cleanup: marking orphan set \"{title}\" (source: {source})");
                    set.DeletePending = true;
                }

                return orphans.Length;
            });

            if (provided == null)
            {
                notification.CompletionText = deleted > 0
                    ? O2LazerStrings.CleanupComplete(deleted)
                    : O2LazerStrings.NoOrphansFound;
                notification.State = ProgressNotificationState.Completed;
            }
            else
                notification.Text = O2LazerStrings.Refreshing;

            return deleted;
        }
        catch (Exception e)
        {
            O2LazerLogger.Error(e, $"O2LAZER cleanup failed: {e.Message}");

            if (provided == null)
            {
                notification.CompletionText = O2LazerStrings.CleanupFailed;
                notification.State = ProgressNotificationState.Cancelled;
            }
            return 0;
        }
    }

    internal const string SOURCE_COLLECTION_PREFIX = "O2Lazer: ";

    /// <summary>
    /// Creates or refreshes one collection per O2Jam source folder, keeping only
    /// currently imported, non-deleted beatmaps inside them.
    /// </summary>
    public void UpdateSourceFolderCollections()
    {
        realm.Write(r =>
        {
            var activeBeatmaps = r.All<BeatmapInfo>()
                .Filter("Ruleset.ShortName == $0 && BeatmapSet.DeletePending == false", Constant.SHORT_NAME)
                .AsEnumerable()
                .Where(b => !string.IsNullOrWhiteSpace(b.Metadata.Source))
                .ToArray();

            var featureCollections = r.All<BeatmapCollection>()
                .Where(c => c.Name.StartsWith(SOURCE_COLLECTION_PREFIX, StringComparison.Ordinal))
                .ToList();

            var activeHashes = activeBeatmaps
                .Select(b => b.MD5Hash)
                .Where(hash => !string.IsNullOrWhiteSpace(hash))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var collection in featureCollections)
            {
                foreach (var hash in collection.BeatmapMD5Hashes.ToArray())
                {
                    if (!activeHashes.Contains(hash))
                        collection.BeatmapMD5Hashes.Remove(hash);
                }
            }

            foreach (var group in activeBeatmaps.GroupBy(b => sourceFolderName(b.Metadata.Source)))
            {
                var name = $"{SOURCE_COLLECTION_PREFIX}{group.Key}";
                var collection = featureCollections.FirstOrDefault(c => c.Name == name);

                if (collection == null)
                {
                    collection = new BeatmapCollection(name);
                    r.Add(collection);
                    featureCollections.Add(collection);
                }

                foreach (var hash in group.Select(b => b.MD5Hash).Where(hash => !string.IsNullOrWhiteSpace(hash)))
                {
                    if (!collection.BeatmapMD5Hashes.Contains(hash))
                        collection.BeatmapMD5Hashes.Add(hash);
                }
            }
        });
    }

    /// <summary>
    /// Removes all collections previously created by the source-folder sync feature.
    /// </summary>
    public void DeleteSourceFolderCollections()
    {
        realm.Write(r =>
        {
            var collections = r.All<BeatmapCollection>()
                .Where(c => c.Name.StartsWith(SOURCE_COLLECTION_PREFIX, StringComparison.Ordinal))
                .ToList();

            foreach (var collection in collections)
                r.Remove(collection);
        });
    }

    private static string sourceFolderName(string source)
    {
        try
        {
            var trimmed = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.GetFileName(trimmed);
        }
        catch (Exception)
        {
            return source;
        }
    }

    /// <summary>
    /// Backfills the <c>o2ma{id}</c> search tag for charts imported before tags were stored.
    /// The song-select carousel only exposes metadata, so the id must live in <see cref="BeatmapMetadata.Tags"/>.
    /// </summary>
    public void UpdateSearchTags()
    {
        realm.Write(r =>
        {
            var beatmaps = r.All<BeatmapInfo>()
                .Filter("Ruleset.ShortName == $0 && BeatmapSet.DeletePending == false", Constant.SHORT_NAME)
                .ToList();

            foreach (var beatmap in beatmaps)
            {
                var idTerm = getO2JamIdTerm(beatmap);
                if (idTerm == null || beatmap.Metadata.Tags.Contains(idTerm, StringComparison.OrdinalIgnoreCase))
                    continue;

                beatmap.Metadata.Tags = string.IsNullOrWhiteSpace(beatmap.Metadata.Tags)
                    ? idTerm
                    : $"{beatmap.Metadata.Tags} {idTerm}";
            }
        });
    }

    private static string? getO2JamIdTerm(BeatmapInfo beatmap)
    {
        var match = o2jam_id_pattern.Match(beatmap.Path ?? string.Empty);
        if (match.Success)
            return $"o2ma{match.Groups[1].Value}";

        foreach (var file in beatmap.BeatmapSet?.Files ?? [])
        {
            match = o2jam_id_pattern.Match(file.Filename);
            if (match.Success)
                return $"o2ma{match.Groups[1].Value}";
        }

        return null;
    }

    public void DeleteAllO2LazerFilesAsync()
    {
        if (beatmaps == null)
        {
            O2LazerLogger.Log("O2LAZER delete: BeatmapManager is unavailable; cannot delete O2LAZER beatmaps.");
            return;
        }

        var notification = new ProgressNotification
        {
            Text = O2LazerStrings.DeletingBeatmaps,
            State = ProgressNotificationState.Active,
        };
        notification.CancelRequested = () => true;
        notifications?.Post(notification);

        var cnt = 0;

        Task.Run(() =>
        {
            try
            {
                realm.Run(r =>
                {
                    foreach (var set in r.All<BeatmapSetInfo>().Where(x => !x.DeletePending))
                    {
                        notification.CancellationToken.ThrowIfCancellationRequested();

                        if (set.Beatmaps.Any(x => x.Ruleset.ShortName == Constant.SHORT_NAME))
                        {
                            r.Write(() => beatmaps.Delete(set));
                            cnt += 1;
                            notification.Text = O2LazerStrings.DeletedSets(cnt);
                        }
                    }
                });

                notification.CompletionText = O2LazerStrings.DeletedSets(cnt);
                notification.State = ProgressNotificationState.Completed;
            }
            catch (OperationCanceledException)
            {
                O2LazerLogger.Log($"O2LAZER delete: cancelled after {cnt} sets");
                notification.CompletionText = cnt > 0
                    ? O2LazerStrings.DeleteCancelled(cnt)
                    : O2LazerStrings.DeleteWasCancelled;
                notification.State = ProgressNotificationState.Cancelled;
            }
            catch (Exception e)
            {
                O2LazerLogger.Error(e, $"O2LAZER delete failed: {e.Message}");

                notification.CompletionText = O2LazerStrings.DeleteFailed;
                notification.State = ProgressNotificationState.Cancelled;
            }
        });
    }

    /// <summary>
    /// Fast discovery pass: walk the input paths and treat each OJN as one three-difficulty set.
    /// Performs no file reading or hashing, so it returns quickly and yields an accurate total count.
    /// </summary>
    private static ImportGroup[] discoverChartGroups(IEnumerable<string> paths)
    {
        var chartPaths = paths.AsParallel()
            .SelectMany(expandPathToCharts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return chartPaths
            .Where(path => !string.IsNullOrWhiteSpace(Path.GetDirectoryName(path)))
            .Select(path => new ImportGroup(Path.GetDirectoryName(path)!, [path]))
            .ToArray();
    }

    private static IEnumerable<string> expandPathToCharts(string path)
    {
        if (Directory.Exists(path))
            return Directory.EnumerateFiles(path, "*", library_enumeration_options).Where(Constant.IsChartFile);

        return File.Exists(path) && Constant.IsChartFile(path) ? [path] : [];
    }

    /// <summary>Compute a deterministic set hash from sorted unique beatmap MD5 hashes.</summary>
    private static string calculateSetHash(IEnumerable<string> md5Hashes) => string.Concat(md5Hashes
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)).ToLowerInvariant();

    private static string calculateSetHash(BeatmapSetInfo beatmapSetInfo) =>
        calculateSetHash(beatmapSetInfo.Beatmaps.Select(b => b.MD5Hash));

    private ConcurrentDictionary<string, byte> createImportedBeatmapMd5Lookup()
    {
        var hashes = realm.Run(r =>
        {
            var result = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

            foreach (var beatmap in r.All<BeatmapInfo>().Filter("Ruleset.ShortName == $0 && BeatmapSet.DeletePending == false", Constant.SHORT_NAME))
            {
                if (!string.IsNullOrWhiteSpace(beatmap.MD5Hash))
                    result.TryAdd(beatmap.MD5Hash, 0);
            }

            return result;
        });

        return new ConcurrentDictionary<string, byte>(hashes, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads one OJN and prepares its EX, NX and HX charts.
    /// </summary>
    private static PreparedDirectory? readPreparedDirectory(
        ImportGroup group,
        RealmAccess realmAccess,
        RealmFileStore fileStore,
        ConcurrentDictionary<string, byte> importedBeatmapMd5Hashes,
        IReadOnlySet<string> existingSetHashes,
        IReadOnlyDictionary<string, ExistingSet> sourcePathToSetHash)
    {
        var path = group.ChartPaths.Single();
        var pathKey = getSourcePathKey(group.Directory, Path.GetFileName(path));
        var fingerprint = computeFileFingerprint(path);

        if (sourcePathToSetHash.TryGetValue(pathKey, out var previousSet)
            && previousSet.Marker == O2LazerMetadataMarker.CurrentMarker
            && previousSet.Fingerprint != null
            && previousSet.Fingerprint == fingerprint)
        {
            // Marker and fingerprint both match: the source is unchanged since the last
            // processed import, so skip the file without reading or decoding it.
            return null;
        }

        var content = File.ReadAllBytes(path);
        var header = OjnDecoder.DecodeHeader(content);

        var candidates = Enum.GetValues<OjnDifficulty>()
            .Where(difficulty => header.BlockCounts[(int)difficulty] > 0 || header.NoteCounts[(int)difficulty] > 0)
            .Select(difficulty => new
            {
                Difficulty = difficulty,
                Md5 = calculateDifficultyHash(content, difficulty),
            })
            .ToArray();
        if (candidates.Length == 0)
            return null;

        var setHash = calculateSetHash(candidates.Select(candidate => candidate.Md5));

        string? replacedSetHash = null;

        if (sourcePathToSetHash.TryGetValue(pathKey, out previousSet) && previousSet.Hash != setHash)
        {
            // The file at this path changed: retire the previously imported set so the new
            // content replaces it instead of leaving a duplicate behind.
            foreach (var md5 in previousSet.Md5Hashes)
                importedBeatmapMd5Hashes.TryRemove(md5, out _);

            replacedSetHash = previousSet.Hash;
        }

        if (existingSetHashes.Contains(setHash))
        {
            return new PreparedDirectory(group.Directory, [], null, null,
                Marker: O2LazerMetadataMarker.CurrentMarker,
                Fingerprint: fingerprint!,
                Refresh: new MetadataRefresh(setHash, replacedSetHash, header, path));
        }

        var decodedCharts = OjnDecoder.DecodeAll(content, path);
        if (decodedCharts.Count == 0)
            return null;

        var decodedModel = new O2LazerDecodedBeatmap();
        decodedModel.CopyFrom(decodedCharts[0].ParseResult);
        var backgroundPath = O2LazerWorkingBeatmap.resolveExternalBackgroundPaths(group.Directory, decodedModel, false).FirstOrDefault();
        var panelBackgroundPath = O2LazerWorkingBeatmap.resolveExternalBackgroundPaths(group.Directory, decodedModel, true).FirstOrDefault();
        var externalBackgrounds = backgroundPath != null || panelBackgroundPath != null
            ? new ExternalBackgroundImport(backgroundPath ?? panelBackgroundPath!, panelBackgroundPath ?? backgroundPath!)
            : null;

        var uniqueCharts = candidates
            .Where(candidate => importedBeatmapMd5Hashes.TryAdd(candidate.Md5, 0))
            .ToArray();
        if (uniqueCharts.Length == 0)
            return null;

        var decodedByDifficulty = decodedCharts.ToDictionary(decoded => decoded.Difficulty);
        var parsedCharts = uniqueCharts.Select(candidate =>
        {
            var decoded = decodedByDifficulty[candidate.Difficulty];
            var level = decoded.Header.Levels[(int)decoded.Difficulty];
            var title = string.IsNullOrWhiteSpace(decoded.Header.Title)
                ? Path.GetFileNameWithoutExtension(path)
                : decoded.Header.Title;
            var hitObjects = decoded.ParseResult.HitObjects;

            return (
                Md5: candidate.Md5,
                O2JamId: decoded.Header.Id,
                Title: title,
                Artist: decoded.Header.Artist,
                DifficultyName: OjnDecoder.DifficultyDisplayName(decoded.Difficulty, level),
                Level: level,
                Noter: decoded.Header.Noter,
                // O2Jam's authored Lv is the native difficulty scale. Expose it through
                // lazer's star-rating field so song select can sort and display it directly.
                StarRating: O2LazerDifficultyInfo.ComputeStarRating(level),
                Bpm: (double)decoded.Header.Bpm,
                decoded.Length,
                TotalObjectCount: hitObjects.Count,
                EndTimeObjectCount: hitObjects.Count(hit => hit.IsLongNote)
            );
        }).ToArray();

        var chartFileHash = string.Empty;
        ResourceImport? background = null;

        lock (fileStore)
        {
            realmAccess.Run(r =>
            {
                using var stream = new MemoryBackedFileStream(path, content);
                chartFileHash = fileStore.Add(stream, r, addToRealm: false, preferHardLinks: true).Hash;

                var image = decodedCharts[0].Header.CoverArt.Length > 0
                    ? decodedCharts[0].Header.CoverArt
                    : decodedCharts[0].Header.Thumbnail;

                if (image.Length > 0)
                {
                    using var imageStream = new MemoryStream(image, writable: false);
                    var imageHash = fileStore.Add(imageStream, r, addToRealm: false).Hash;
                    background = new ResourceImport(imageHash, $"o2jam-background{detectImageExtension(image)}");
                }
            });
        }

        var charts = parsedCharts.Select(parsed => new ChartImport(
            path, parsed.Md5, chartFileHash,
            parsed.O2JamId,
            parsed.Title, parsed.Artist, parsed.DifficultyName, parsed.Level,
            parsed.Noter, parsed.StarRating, parsed.Bpm, parsed.Length,
            parsed.TotalObjectCount, parsed.EndTimeObjectCount, 0)).ToArray();

        return new PreparedDirectory(group.Directory, charts, background, externalBackgrounds,
            Marker: O2LazerMetadataMarker.CurrentMarker,
            Fingerprint: fingerprint!,
            ReplacesSetHash: replacedSetHash);
    }

    /// <summary>
    /// Applies a queued metadata refresh inside the consumer's write transaction, so no
    /// realm writes happen from producer worker threads. Also retires the replaced set
    /// when the same path now resolves to different content.
    /// </summary>
    private static void applyMetadataRefresh(Realm r, MetadataRefresh refresh, string marker, string fingerprint)
    {
        if (refresh.ReplacesSetHash is { } replacedHash)
        {
            var replaced = r.All<BeatmapSetInfo>().Filter("Hash == $0", replacedHash).FirstOrDefault();
            if (replaced != null && !replaced.DeletePending)
            {
                replaced.DeletePending = true;
                O2LazerLogger.Log($"O2LAZER refresh: retiring replaced set \"{replaced.Metadata.Title}\"");
            }
        }

        var beatmapSet = r.All<BeatmapSetInfo>()
            .Filter("Hash == $0", refresh.SetHash)
            .FirstOrDefault();
        if (beatmapSet == null)
            return;

        var title = string.IsNullOrWhiteSpace(refresh.Header.Title)
            ? Path.GetFileNameWithoutExtension(refresh.ChartPath)
            : refresh.Header.Title;
        var artist = refresh.Header.Artist;
        var author = string.IsNullOrWhiteSpace(refresh.Header.Noter)
            ? Constant.AUTHOR
            : refresh.Header.Noter;

        foreach (var beatmap in beatmapSet.Beatmaps.Where(b => b.Ruleset.ShortName == Constant.SHORT_NAME))
        {
            beatmap.Metadata.Title = title;
            beatmap.Metadata.Artist = artist;
            beatmap.Metadata.Author.Username = author;

            var tags = beatmap.Metadata.Tags
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(token => !token.StartsWith("o2lazer-", StringComparison.Ordinal))
                .Append(marker)
                .Append(fingerprint);
            beatmap.Metadata.Tags = string.Join(' ', tags);
        }
    }

    /// <summary>
    /// Snapshots the currently imported O2Lazer library once per pipeline so per-chart
    /// existence and replacement decisions avoid a realm query for every file.
    /// </summary>
    private (HashSet<string> SetHashes, Dictionary<string, ExistingSet> SourcePathToSetHash) createExistingLibrarySnapshot()
    {
        // All data is copied into plain records inside the realm action; realm objects must
        // never be returned, as the temporary realm is closed before the caller resumes.
        return realm.Run(r =>
        {
            var setHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sourcePathToSetHash = new Dictionary<string, ExistingSet>(StringComparer.OrdinalIgnoreCase);

            var sets = r.All<BeatmapSetInfo>()
                .AsEnumerable()
                .Where(s => !s.DeletePending && s.Beatmaps.Any(b => b.Ruleset.ShortName == Constant.SHORT_NAME))
                .ToArray();

            foreach (var set in sets)
            {
                setHashes.Add(set.Hash);

                var source = set.Beatmaps.FirstOrDefault(b => !string.IsNullOrEmpty(b.Metadata.Source))?.Metadata.Source;
                var chartFile = set.Files.FirstOrDefault(f => Constant.IsChartFile(f.Filename))?.Filename;
                if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(chartFile))
                {
                    var firstBeatmap = set.Beatmaps.FirstOrDefault(b => b.Ruleset.ShortName == Constant.SHORT_NAME);
                    var (marker, fingerprint) = readMarkerTokens(firstBeatmap?.Metadata.Tags);
                    sourcePathToSetHash[getSourcePathKey(source, chartFile)] = new ExistingSet(
                        set.Hash,
                        set.Beatmaps.Select(b => b.MD5Hash).Where(hash => !string.IsNullOrWhiteSpace(hash)).ToArray()!,
                        marker,
                        fingerprint);
                }
            }

            return (setHashes, sourcePathToSetHash);
        });
    }

    private static string getSourcePathKey(string directory, string chartFile) =>
        $"{Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)}|{chartFile}";

    private static string? computeFileFingerprint(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? $"o2lazer-src:{info.Length}:{info.LastWriteTimeUtc.Ticks}" : null;
        }
        catch
        {
            return null;
        }
    }

    private static (string? Marker, string? Fingerprint) readMarkerTokens(string? tags)
    {
        string? marker = null;
        string? fingerprint = null;

        if (tags != null)
        {
            foreach (var token in tags.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith("o2lazer-meta:", StringComparison.Ordinal))
                    marker = token;
                else if (token.StartsWith("o2lazer-src:", StringComparison.Ordinal))
                    fingerprint = token;
            }
        }

        return (marker, fingerprint);
    }

    private static string detectImageExtension(byte[] image)
    {
        if (image.Length >= 8
            && image[0] == 0x89 && image[1] == (byte)'P' && image[2] == (byte)'N' && image[3] == (byte)'G'
            && image[4] == 0x0d && image[5] == 0x0a && image[6] == 0x1a && image[7] == 0x0a)
            return ".png";
        if (image.Length >= 3 && image[0] == 0xff && image[1] == 0xd8 && image[2] == 0xff)
            return ".jpg";
        if (image.Length >= 6 && (image.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || image.AsSpan(0, 6).SequenceEqual("GIF89a"u8)))
            return ".gif";
        if (image.Length >= 2 && image[0] == (byte)'B' && image[1] == (byte)'M')
            return ".bmp";

        return ".jpg";
    }

    private static string calculateDifficultyHash(byte[] content, OjnDifficulty difficulty)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        hash.AppendData(content);
        hash.AppendData([(byte)difficulty]);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void applyCompletionState(ProgressNotification notification, ImportResult result)
    {
        if (!result.RulesetAvailable)
        {
            notification.CompletionText = O2LazerStrings.ImportFailed;
            notification.State = ProgressNotificationState.Cancelled;
            return;
        }

        if (result.Imported == 0 && result.Processed > 0)
        {
            notification.CompletionText = result.Processed == 1
                ? O2LazerStrings.SetAlreadyImported
                : O2LazerStrings.AllSetsAlreadyImported(result.Processed);
            notification.State = ProgressNotificationState.Completed;
            return;
        }

        if (result.Imported == 0)
        {
            notification.CompletionText = O2LazerStrings.ImportFailed;
            notification.State = ProgressNotificationState.Cancelled;
            return;
        }

        notification.CompletionText = result.Failed > 0
            ? O2LazerStrings.ImportCompletedWithFailures(result.Imported, result.TotalSets, result.Failed)
            : result.Imported == result.TotalSets
            ? O2LazerStrings.ImportedSets(result.Imported)
            : O2LazerStrings.ImportedSetsProgress(result.Imported, result.TotalSets);
        notification.State = ProgressNotificationState.Completed;
    }

    /// <summary>Validate that the O2LAZER ruleset is available; set notification state if not.</summary>
    private bool checkRulesetAvailable(ProgressNotification notification) => realm.Run(r =>
    {
        if (r.Find<RulesetInfo>(Constant.SHORT_NAME)?.Available == true) return true;

        notification.CompletionText = O2LazerStrings.RulesetUnavailable;
        notification.State = ProgressNotificationState.Cancelled;
        return false;
    });

    /// <summary>
    /// Full import pipeline (runs on a thread-pool thread). Orchestrates discovery,
    /// producer launch, and consumer drain. All cancellation and error states are
    /// written back to <paramref name="notification"/>.
    /// </summary>
    private void runImportPipeline(ProgressNotification notification, string[] paths, bool isRefresh = false)
    {
        try
        {
            var fileStore = new RealmFileStore(realm, storage);

            if (!isRefresh)
                notification.Text = O2LazerStrings.ScanningFiles;
            var groups = discoverChartGroups(paths);

            if (groups.Length == 0)
            {
                notification.CompletionText = O2LazerStrings.NoChartsFound;
                notification.State = ProgressNotificationState.Cancelled;
                return;
            }

            if (!isRefresh)
                notification.Text = O2LazerStrings.PreparingImport;

            using var pool = new BlockingCollection<PreparedDirectory?>(1024);
            var importedBeatmapMd5Hashes = createImportedBeatmapMd5Lookup();
            var (existingSetHashes, sourcePathToSetHash) = createExistingLibrarySnapshot();
            var failedPaths = new ConcurrentQueue<string>();

            var producer = launchProducer(notification, groups, fileStore, pool, importedBeatmapMd5Hashes, existingSetHashes, sourcePathToSetHash, failedPaths);
            var (imported, processed, cancelled) = drainConsumer(notification, groups, pool, producer);

            if (cancelled) return; // drainConsumer already set the notification state

            if (O2LazerRulesetRuntime.SyncSourceFolderCollections)
                UpdateSourceFolderCollections();

            UpdateSearchTags();

            applyCompletionState(notification,
                new ImportResult(RulesetAvailable: true, TotalSets: groups.Length, Imported: imported, Processed: processed, Failed: failedPaths.Count));
        }
        catch (OperationCanceledException)
        {
            O2LazerLogger.Log("O2LAZER import: cancelled");
            notification.CompletionText = O2LazerStrings.ImportWasCancelled;
            notification.State = ProgressNotificationState.Cancelled;
        }
        catch (Exception e)
        {
            O2LazerLogger.Log($"O2LAZER import: scan failed: {e.Message}");
            O2LazerLogger.Log(e.ToString());
            notification.CompletionText = O2LazerStrings.ImportFailed;
            notification.State = ProgressNotificationState.Cancelled;
        }
    }

    /// <summary>
    /// Launch producer tasks that read, parse, and write chart/resource files to disk
    /// in parallel, one directory per worker.
    /// </summary>
    private Task launchProducer(
        ProgressNotification notification,
        ImportGroup[] groups,
        RealmFileStore fileStore,
        BlockingCollection<PreparedDirectory?> pool,
        ConcurrentDictionary<string, byte> importedBeatmapMd5Hashes,
        IReadOnlySet<string> existingSetHashes,
        IReadOnlyDictionary<string, ExistingSet> sourcePathToSetHash,
        ConcurrentQueue<string> failedPaths)
    {
        return Task.Run(() =>
        {
            try
            {
                Parallel.ForEach(groups, new ParallelOptions
                {
                    CancellationToken = notification.CancellationToken,
                    MaxDegreeOfParallelism = max_parallel_imports,
                }, group =>
                {
                    PreparedDirectory? prepared = null;

                    try
                    {
                        prepared = readPreparedDirectory(group, realm, fileStore, importedBeatmapMd5Hashes, existingSetHashes, sourcePathToSetHash);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        var path = group.ChartPaths.Single();
                        failedPaths.Enqueue(path);
                        O2LazerLogger.Error(exception, $"O2Jam import: failed to read '{path}'; continuing with the remaining library.");
                    }

                    pool.Add(prepared, notification.CancellationToken);
                });
            }
            catch (OperationCanceledException e)
            {
                // Producer cancelled — items already queued will still be consumed.
                O2LazerLogger.Log($"O2LAZER import: producer error: {e.Message}");
            }
            finally
            {
                pool.CompleteAdding();
            }
        });
    }

    /// <summary>
    /// Drain the prepared-directory queue, performing per-set dedup and realm writes.
    /// Returns <c>cancelled = true</c> when the user requested cancellation; the caller
    /// should not overwrite the notification state.
    /// </summary>
    private (int imported, int processed, bool cancelled) drainConsumer(
        ProgressNotification notification,
        ImportGroup[] groups,
        BlockingCollection<PreparedDirectory?> pool,
        Task producer)
    {
        var imported = 0;
        var processed = 0;
        var pendingRefreshes = new List<(PreparedDirectory Prepared, MetadataRefresh Refresh)>();

        try
        {
            foreach (var prepared in pool.GetConsumingEnumerable())
            {
                realm.Run(r =>
                {
                    var rulesetInfo = r.Find<RulesetInfo>(Constant.SHORT_NAME)!;
                    notification.CancellationToken.ThrowIfCancellationRequested();

                    // ── Fast path: skip if the set hash already exists (prepared == null) ──
                    if (prepared == null)
                    {
                        processed++;
                        reportProgress(notification, groups.Length, processed);
                        return;
                    }

                    if (prepared.Refresh is { } refresh)
                    {
                        pendingRefreshes.Add((prepared, refresh));
                        processed++;
                        reportProgress(notification, groups.Length, processed);

                        if (pendingRefreshes.Count >= refresh_batch_size)
                            flushPendingRefreshes(r, pendingRefreshes);

                        return;
                    }

                    flushPendingRefreshes(r, pendingRefreshes);

                    var ok = importPreparedDirectory(r, prepared, rulesetInfo);

                    if (ok)
                        imported++;

                    processed++;
                    reportProgress(notification, groups.Length, processed);
                });
            }

            if (pendingRefreshes.Count > 0)
                realm.Run(r => flushPendingRefreshes(r, pendingRefreshes));

            producer.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            O2LazerLogger.Log($"O2LAZER import: cancelled after {imported} of {groups.Length} sets");
            notification.CompletionText = imported > 0
                ? O2LazerStrings.ImportCancelledProgress(imported, groups.Length)
                : O2LazerStrings.ImportWasCancelled;
            notification.State = ProgressNotificationState.Cancelled;
            return (imported, processed, true);
        }

        return (imported, processed, false);

        static void reportProgress(ProgressNotification n, int total, int proc)
        {
            n.Text = O2LazerStrings.ProcessedSetsProgress(proc, total);
            n.Progress = (float)proc / total;
        }
    }

    private static void flushPendingRefreshes(
        Realm r,
        List<(PreparedDirectory Prepared, MetadataRefresh Refresh)> pending)
    {
        if (pending.Count == 0)
            return;

        using var transaction = r.BeginWrite();
        foreach (var (prepared, refresh) in pending)
            applyMetadataRefresh(r, refresh, prepared.Marker, prepared.Fingerprint);
        transaction.Commit();
        pending.Clear();
    }

    /// <summary>
    /// Creates <see cref="RealmFile"/> + <see cref="BeatmapSetInfo"/> objects for all charts in a
    /// prepared directory, in one transaction. Files were already written to disk by the producer,
    /// so this transaction only touches realm metadata — no disk I/O.
    /// Star rating is pre-computed during the producer phase and written directly.
    /// </summary>
    private bool importPreparedDirectory(
        Realm r,
        PreparedDirectory prepared,
        RulesetInfo rulesetInfo)
    {
        try
        {
            var chartImports = prepared.Charts;

            // Collect all unique file hashes for batch RealmFile creation.
            var allHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in chartImports) allHashes.Add(c.FileHash);
            if (prepared.Background != null) allHashes.Add(prepared.Background.FileHash);

            string? externalBackgroundMarkerPath = null;
            string? externalBackgroundMarkerHash = null;
            if (prepared.ExternalBackgrounds is { } external)
            {
                externalBackgroundMarkerPath = external.PanelBackgroundPath ?? external.BackgroundPath;
                externalBackgroundMarkerHash = $"{external.BackgroundPath}|{external.PanelBackgroundPath}";
                allHashes.Add(externalBackgroundMarkerHash);
            }

            using var transaction = r.BeginWrite();

            if (prepared.ReplacesSetHash is { } replacedHash)
            {
                var replaced = r.All<BeatmapSetInfo>().Filter("Hash == $0", replacedHash).FirstOrDefault();
                if (replaced != null && !replaced.DeletePending)
                    replaced.DeletePending = true;
            }

            // Find-or-create RealmFile objects. Files already exist on disk (written by producers);
            // we only need the realm metadata to point at them.
            var realmFileByHash = new Dictionary<string, RealmFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var hash in allHashes)
            {
                var existing = r.Find<RealmFile>(hash);
                if (existing != null)
                    realmFileByHash[hash] = existing;
                else
                {
                    var rf = new RealmFile { Hash = hash };
                    r.Add(rf);
                    realmFileByHash[hash] = rf;
                }
            }

            var externalBackgroundRealmFile = externalBackgroundMarkerHash != null
                ? realmFileByHash[externalBackgroundMarkerHash]
                : null;

            // ── Build the BeatmapSet ──

            var beatmapSetInfo = new BeatmapSetInfo
            {
                OnlineID = -1,
                DateAdded = DateTimeOffset.UtcNow,
            };

            // Attach file usages (dedupe by filename).
            var seenFilenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var chart in chartImports)
            {
                var fileName = Path.GetFileName(chart.Path);
                if (seenFilenames.Add(fileName))
                    beatmapSetInfo.Files.Add(new RealmNamedFileUsage(realmFileByHash[chart.FileHash], fileName));
            }

            if (prepared.Background != null && seenFilenames.Add(prepared.Background.FileName))
                beatmapSetInfo.Files.Add(new RealmNamedFileUsage(realmFileByHash[prepared.Background.FileHash], prepared.Background.FileName));

            if (externalBackgroundMarkerPath != null && externalBackgroundRealmFile != null && seenFilenames.Add(externalBackgroundMarkerPath))
                beatmapSetInfo.Files.Add(new RealmNamedFileUsage(externalBackgroundRealmFile, externalBackgroundMarkerPath));

            var setTitle = chartImports[0].Title;

            // Create beatmap infos.
            foreach (var chart in chartImports)
            {
                var beatmapInfo = new BeatmapInfo
                {
                    DifficultyName = chart.DifficultyName,
                    Ruleset = rulesetInfo,
                        Metadata = new BeatmapMetadata
                        {
                            Title = setTitle,
                            Artist = chart.Artist,
                            Author = new RealmUser
                            {
                                Username = string.IsNullOrWhiteSpace(chart.Noter) ? Constant.AUTHOR : chart.Noter,
                            },
                            Tags = $"o2jam o2ma{chart.O2JamId} {prepared.Marker} {prepared.Fingerprint}",
                            Source = prepared.Directory,
                            BackgroundFile = externalBackgroundMarkerPath ?? prepared.Background?.FileName ?? string.Empty,
                            PreviewTime = 0,
                    },
                    Difficulty = new BeatmapDifficulty(),
                    Hash = chart.FileHash,
                    MD5Hash = chart.Md5Hash,
                    StarRating = chart.StarRating,
                    BPM = chart.Bpm,
                    Length = chart.Length,
                    TotalObjectCount = chart.TotalObjectCount,
                    EndTimeObjectCount = chart.EndTimeObjectCount,
                };
                var diff = new O2LazerDifficultyInfo
                {
                    ParsedName = chart.DifficultyName,
                    PlayLevel = chart.Level,
                    Rank = 2,
                    Total = chart.Level,
                    KeyCount = O2LazerLayout.O2JAM_KEY_COLUMNS,
                };
                diff.WriteToOsuDifficulty(beatmapInfo);

                beatmapSetInfo.Beatmaps.Add(beatmapInfo);
                beatmapInfo.BeatmapSet = beatmapSetInfo;
            }

            beatmapSetInfo.Hash = calculateSetHash(beatmapSetInfo);
            r.Add(beatmapSetInfo);
            transaction.Commit();

            return true;
        }
        catch (Exception e)
        {
            O2LazerLogger.Error(e, $"O2LAZER import: failed to import {prepared.Directory}: {e.Message}");
            return false;
        }

    }

    // ── Private records (data flowing through the pipeline) ──

    private sealed record ImportResult(bool RulesetAvailable, int TotalSets, int Imported, int Processed, int Failed);

    private sealed record ImportGroup(string Directory, string[] ChartPaths);

    private sealed record ChartImport(
        string Path,
        string Md5Hash,
        string FileHash, // SHA-256 for RealmFile (computed during file write)
        int O2JamId,
        string Title,
        string Artist,
        string DifficultyName,
        int Level,
        string Noter,
        double StarRating,
        double Bpm,
        double Length,
        int TotalObjectCount,
        int EndTimeObjectCount,
        int ScratchObjectCount);

    private sealed record ResourceImport(string FileHash, string FileName);

    private sealed record PreparedDirectory(
        string Directory,
        ChartImport[] Charts,
        ResourceImport? Background,
        ExternalBackgroundImport? ExternalBackgrounds,
        string Marker,
        string Fingerprint,
        MetadataRefresh? Refresh = null,
        string? ReplacesSetHash = null);

    private sealed record MetadataRefresh(
        string SetHash,
        string? ReplacesSetHash,
        OjnHeader Header,
        string ChartPath);

    private sealed record ExistingSet(
        string Hash,
        IReadOnlyList<string> Md5Hashes,
        string? Marker,
        string? Fingerprint);

    private sealed record ExternalBackgroundImport(string BackgroundPath, string PanelBackgroundPath);

    /// <summary>
    /// A <see cref="FileStream"/> whose contents are served from an in-memory buffer rather than disk,
    /// while still reporting the real file path through <see cref="FileStream.Name"/>.
    /// <para>
    /// This lets <see cref="RealmFileStore.Add"/> take its hard-link fast path (which requires the stream
    /// to be a <see cref="FileStream"/> and reads <c>Name</c>) without re-reading the file from disk for
    /// hashing — the bytes were already read in parallel before the write transaction.
    /// </para>
    /// </summary>
    private sealed class MemoryBackedFileStream(string path, byte[] content)
        : FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1)
    {

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => content.Length;

        public override long Position
        {
            get => position;
            set => position = (int)value;
        }

        private int position;

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = content.Length - position;
            if (remaining <= 0)
                return 0;

            var toCopy = Math.Min(remaining, count);
            Array.Copy(content, position, buffer, offset, toCopy);
            position += toCopy;
            return toCopy;
        }

        public override int Read(Span<byte> buffer)
        {
            var remaining = content.Length - position;
            if (remaining <= 0)
                return 0;

            var toCopy = Math.Min(remaining, buffer.Length);
            content.AsSpan(position, toCopy).CopyTo(buffer);
            position += toCopy;
            return toCopy;
        }

        public override int ReadByte()
            => position < content.Length ? content[position++] : -1;

        public override long Seek(long offset, SeekOrigin origin)
        {
            position = origin switch
            {
                SeekOrigin.Begin => (int)offset,
                SeekOrigin.Current => position + (int)offset,
                SeekOrigin.End => content.Length + (int)offset,
                _ => position,
            };
            return position;
        }

        public override void Flush()
        {
        }
    }
}


