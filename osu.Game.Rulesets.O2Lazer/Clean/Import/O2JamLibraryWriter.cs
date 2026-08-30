using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Models;
using osu.Game.Rulesets;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Localisation;
using Realms;

namespace osu.Game.Rulesets.O2Lazer.Import;

public enum O2JamLibraryWriteResult
{
    Imported,
    Updated,
    AlreadyPresent,
    RulesetUnavailable,
}

public sealed record O2JamImportedSource(
    Guid SetId,
    DateTimeOffset? LastLocalUpdate,
    long? SourceLength,
    bool HasCurrentMetadata,
    bool HasCurrentEncoding);

public sealed record O2JamLibraryWriteRequest(
    O2JamImportPlan Plan,
    Guid? KnownSourceSetId = null,
    bool SourceIndexWasLoaded = false);

/// <summary>
/// The only import component allowed to know about Realm and osu!'s permanent file store.
/// </summary>
public sealed class O2JamLibraryWriter
{
    internal const string MetadataMarker = "o2lazer-clean:2";
    private const string encoding_marker_prefix = "o2lazer-encoding:";
    internal const string EncodingMarker = encoding_marker_prefix + "2";
    private const string source_length_prefix = "o2lazer-source-size:";

    private readonly RealmAccess realm;
    private readonly RealmFileStore files;

    public O2JamLibraryWriter(RealmAccess realm, Storage storage)
    {
        this.realm = realm;
        files = new RealmFileStore(realm, storage);
    }

    public O2JamLibraryWriteResult Write(O2JamImportPlan plan) =>
        WriteBatch([new O2JamLibraryWriteRequest(plan)])[0];

    public IReadOnlyList<O2JamLibraryWriteResult> WriteBatch(IReadOnlyList<O2JamLibraryWriteRequest> requests)
    {
        if (requests.Count == 0)
            return [];

        var results = Enumerable.Repeat(O2JamLibraryWriteResult.RulesetUnavailable, requests.Count).ToArray();

        realm.Write(database =>
        {
            var ruleset = database.Find<RulesetInfo>(O2LazerIdentity.ShortName);
            if (ruleset?.Available != true)
                return;

            for (var index = 0; index < requests.Count; index++)
                results[index] = write(database, ruleset, requests[index]);
        });

        return results;
    }

    public IReadOnlyDictionary<string, O2JamImportedSource> GetImportedSources() => realm.Run(database =>
    {
        var sources = new Dictionary<string, O2JamImportedSource>(StringComparer.OrdinalIgnoreCase);

        foreach (var set in database.All<BeatmapSetInfo>().Where(set => !set.DeletePending).AsEnumerable())
        {
            if (!isOwnedByO2Lazer(set) || !tryGetSourcePath(set, out var sourcePath))
                continue;

            var beatmap = set.Beatmaps.First(candidate => string.Equals(
                candidate.Ruleset.ShortName,
                O2LazerIdentity.ShortName,
                StringComparison.Ordinal));
            var tags = beatmap.Metadata.Tags.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var sourceLength = parseSourceLength(tags);
            sources[sourcePath] = new O2JamImportedSource(
                set.ID,
                beatmap.LastLocalUpdate,
                sourceLength,
                tags.Contains(MetadataMarker, StringComparer.Ordinal),
                tags.Contains(EncodingMarker, StringComparer.Ordinal));
        }

        return sources;
    });

    public int MarkDeleted(IEnumerable<Guid> setIds)
    {
        var ids = setIds.Distinct().ToArray();
        return realm.Write(database =>
        {
            var count = 0;
            foreach (var id in ids)
            {
                var set = database.Find<BeatmapSetInfo>(id);
                if (set is not { DeletePending: false } || !isOwnedByO2Lazer(set))
                    continue;

                set.DeletePending = true;
                count++;
            }

            return count;
        });
    }

    public int DeleteAll() => realm.Write(database =>
    {
        var sets = database.All<BeatmapSetInfo>()
                           .Where(set => !set.DeletePending)
                           .AsEnumerable()
                           .Where(isOwnedByO2Lazer)
                           .ToArray();

        foreach (var set in sets)
            set.DeletePending = true;

        return sets.Length;
    });

    public int MarkMissingSources()
    {
        var sources = realm.Run(database => database.All<BeatmapSetInfo>()
                                                    .Where(set => !set.DeletePending)
                                                    .AsEnumerable()
                                                    .Where(isOwnedByO2Lazer)
                                                    .Select(set => tryGetSourcePath(set, out var sourcePath)
                                                        ? (set.ID, SourcePath: sourcePath)
                                                        : (Guid.Empty, SourcePath: string.Empty))
                                                    .Where(source => source.Item1 != Guid.Empty)
                                                    .ToArray());
        var missing = sources.Where(source => !File.Exists(source.SourcePath))
                             .Select(source => source.Item1);
        return MarkDeleted(missing);
    }

    private O2JamLibraryWriteResult write(Realm database, RulesetInfo ruleset, O2JamLibraryWriteRequest request)
    {
        var plan = request.Plan;
        BeatmapSetInfo? sourceSet = null;

        if (request.KnownSourceSetId != null)
        {
            var knownSet = database.Find<BeatmapSetInfo>(request.KnownSourceSetId.Value);
            if (knownSet is { DeletePending: false } && isOwnedByO2Lazer(knownSet))
                sourceSet = knownSet;
        }
        else if (!request.SourceIndexWasLoaded)
        {
            sourceSet = database.All<BeatmapSetInfo>()
                                .AsEnumerable()
                                .FirstOrDefault(set => !set.DeletePending && containsSourceChart(set, plan.SourcePath));
        }

        // Older releases can produce a different set hash when an OJN difficulty contains
        // blocks but no playable notes. The unchanged source file is the stronger identity;
        // migrate its metadata in place so Beatmap IDs and attached scores remain intact.
        if (sourceSet != null && containsSourceContent(sourceSet, plan))
        {
            return refreshMetadata(sourceSet, plan)
                ? O2JamLibraryWriteResult.Updated
                : O2JamLibraryWriteResult.AlreadyPresent;
        }

        var matchingSet = database.All<BeatmapSetInfo>()
                                  .Where(set => !set.DeletePending && set.Hash == plan.SetHash)
                                  .AsEnumerable()
                                  .FirstOrDefault(isOwnedByO2Lazer);
        if (matchingSet != null)
        {
            var result = containsSourceChart(matchingSet, plan.SourcePath) && refreshMetadata(matchingSet, plan)
                ? O2JamLibraryWriteResult.Updated
                : O2JamLibraryWriteResult.AlreadyPresent;

            if (sourceSet != null && sourceSet != matchingSet)
                sourceSet.DeletePending = true;

            return result;
        }

        var replacedSet = sourceSet;

        using var sourceStream = new MemoryStream(plan.SourceData, writable: false);
        var sourceFile = files.Add(sourceStream, database, preferHardLinks: false);
        var beatmapSet = new BeatmapSetInfo
        {
            OnlineID = -1,
            DateAdded = DateTimeOffset.UtcNow,
            Hash = plan.SetHash,
        };

        beatmapSet.Files.Add(new RealmNamedFileUsage(sourceFile, plan.FileName));

        var backgroundFileName = string.Empty;
        if (plan.Background.Length > 0)
        {
            using var backgroundStream = new MemoryStream(plan.Background, writable: false);
            var backgroundFile = files.Add(backgroundStream, database, preferHardLinks: false);
            backgroundFileName = $"o2jam-background{detectImageExtension(plan.Background)}";
            beatmapSet.Files.Add(new RealmNamedFileUsage(backgroundFile, backgroundFileName));
        }

        foreach (var chart in plan.Charts)
        {
            var beatmapInfo = new BeatmapInfo
            {
                DifficultyName = O2LazerStrings.DifficultyName(chart.Difficulty, chart.Level).ToString(),
                Ruleset = ruleset,
                Difficulty = new BeatmapDifficulty
                {
                    CircleSize = Beatmaps.O2JamBeatmap.ColumnCount,
                    OverallDifficulty = Math.Clamp((int)chart.Level, 0, 10),
                },
                Metadata = new BeatmapMetadata
                {
                    Title = plan.Title,
                    Artist = plan.Artist,
                    Author = new RealmUser
                    {
                        Username = string.IsNullOrWhiteSpace(plan.Author) ? "O2Jam" : plan.Author,
                    },
                    Source = plan.SourceDirectory,
                    Tags = $"o2jam o2ma{plan.SongId} {MetadataMarker} {EncodingMarker} {source_length_prefix}{plan.SourceData.LongLength}",
                    BackgroundFile = backgroundFileName,
                    // A real set file gives osu! a stable audio identity across difficulties even though
                    // the OJM event stream itself remains external to the managed beatmap store.
                    AudioFile = plan.FileName,
                    PreviewTime = 0,
                },
                // Each OJN contains three independently-scored difficulties. Sharing the
                // source-file hash makes osu! attach one difficulty's grades to all three.
                Hash = O2JamBeatmapIdentity.FromSource(plan.SourceHash, chart.Difficulty),
                MD5Hash = chart.Md5Hash,
                StarRating = O2JamDifficultyRating.FromLevel(chart.Level),
                BPM = plan.InitialBpm,
                Length = chart.Length,
                TotalObjectCount = chart.TotalObjectCount,
                EndTimeObjectCount = chart.HoldObjectCount,
                LastLocalUpdate = getSourceTimestamp(plan.SourcePath),
            };

            beatmapSet.Beatmaps.Add(beatmapInfo);
            beatmapInfo.BeatmapSet = beatmapSet;
        }

        database.Add(beatmapSet);
        if (replacedSet != null)
            replacedSet.DeletePending = true;

        return O2JamLibraryWriteResult.Imported;
    }

    internal static bool containsSourceChart(BeatmapSetInfo set, string sourcePath)
    {
        string expected;

        try
        {
            expected = Path.GetFullPath(sourcePath);
        }
        catch (Exception)
        {
            return false;
        }

        foreach (var beatmap in set.Beatmaps)
        {
            var sourceFileName = O2JamExternalChart.SourceFileName(beatmap);
            if (!string.Equals(beatmap.Ruleset.ShortName, O2LazerIdentity.ShortName, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(beatmap.Metadata.Source)
                || string.IsNullOrWhiteSpace(sourceFileName))
                continue;

            try
            {
                var candidate = Path.GetFullPath(Path.Combine(beatmap.Metadata.Source, sourceFileName));
                if (string.Equals(candidate, expected, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch (Exception)
            {
                // A malformed legacy source path must not prevent unrelated charts from importing.
            }
        }

        return false;
    }

    internal static bool refreshMetadata(BeatmapSetInfo set, O2JamImportPlan plan)
    {
        var author = string.IsNullOrWhiteSpace(plan.Author) ? "O2Jam" : plan.Author;
        var sourceTimestamp = getSourceTimestamp(plan.SourcePath);
        var changed = false;

        // BeatmapSetInfo.Hash is global across the Realm library. Namespacing it prevents another
        // keysound ruleset importing the same chart MD5s from becoming the owner of this set.
        if (!string.Equals(set.Hash, plan.SetHash, StringComparison.OrdinalIgnoreCase))
        {
            set.Hash = plan.SetHash;
            changed = true;
        }

        foreach (var beatmap in set.Beatmaps)
        {
            if (!string.Equals(beatmap.Ruleset.ShortName, O2LazerIdentity.ShortName, StringComparison.Ordinal))
                continue;

            var chart = plan.Charts.FirstOrDefault(candidate =>
                beatmap.DifficultyName.StartsWith(candidate.Difficulty.ToString(), StringComparison.OrdinalIgnoreCase));

            if (chart != null)
            {
                var starRating = O2JamDifficultyRating.FromLevel(chart.Level);
                if (Math.Abs(beatmap.StarRating - starRating) > 0.000001)
                {
                    beatmap.StarRating = starRating;
                    changed = true;
                }

                var beatmapHash = O2JamBeatmapIdentity.FromSource(plan.SourceHash, chart.Difficulty);
                if (!string.Equals(beatmap.Hash, beatmapHash, StringComparison.OrdinalIgnoreCase))
                {
                    // Scores already linked to a legacy shared hash retain their difficulty
                    // association while the three beatmaps are separated in place.
                    var attachedScores = beatmap.IsManaged ? beatmap.Scores.ToArray() : [];
                    beatmap.Hash = beatmapHash;

                    foreach (var score in attachedScores)
                    {
                        score.BeatmapHash = beatmapHash;
                        score.BeatmapInfo = beatmap;
                    }

                    changed = true;
                }
            }

            if (!string.Equals(beatmap.Metadata.Title, plan.Title, StringComparison.Ordinal))
            {
                beatmap.Metadata.Title = plan.Title;
                changed = true;
            }

            if (!string.Equals(beatmap.Metadata.Artist, plan.Artist, StringComparison.Ordinal))
            {
                beatmap.Metadata.Artist = plan.Artist;
                changed = true;
            }

            if (!string.Equals(beatmap.Metadata.Author.Username, author, StringComparison.Ordinal))
            {
                beatmap.Metadata.Author.Username = author;
                changed = true;
            }

            if (!string.Equals(beatmap.Metadata.AudioFile, plan.FileName, StringComparison.Ordinal))
            {
                beatmap.Metadata.AudioFile = plan.FileName;
                changed = true;
            }

            var tags = beatmap.Metadata.Tags.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                              .Where(tag => !tag.StartsWith(source_length_prefix, StringComparison.Ordinal)
                                            && (!tag.StartsWith(encoding_marker_prefix, StringComparison.Ordinal) || tag == EncodingMarker))
                              .ToList();
            if (!tags.Contains(MetadataMarker, StringComparer.Ordinal))
            {
                tags.Add(MetadataMarker);
                changed = true;
            }

            if (!tags.Contains(EncodingMarker, StringComparer.Ordinal))
            {
                tags.Add(EncodingMarker);
                changed = true;
            }

            tags.Add($"{source_length_prefix}{plan.SourceData.LongLength}");
            var updatedTags = string.Join(' ', tags);
            if (!string.Equals(beatmap.Metadata.Tags, updatedTags, StringComparison.Ordinal))
            {
                beatmap.Metadata.Tags = updatedTags;
                changed = true;
            }

            if (beatmap.LastLocalUpdate != sourceTimestamp)
            {
                beatmap.LastLocalUpdate = sourceTimestamp;
                changed = true;
            }
        }

        return changed;
    }

    internal static bool isOwnedByO2Lazer(BeatmapSetInfo set) =>
        set.Beatmaps.Any(beatmap => string.Equals(
            beatmap.Ruleset.ShortName,
            O2LazerIdentity.ShortName,
            StringComparison.Ordinal));

    internal static bool containsSourceContent(BeatmapSetInfo set, O2JamImportPlan plan)
    {
        if (!containsSourceChart(set, plan.SourcePath))
            return false;

        return set.Files.Any(file => string.Equals(file.Filename, plan.FileName, StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(file.File.Hash, plan.SourceHash, StringComparison.OrdinalIgnoreCase))
               || set.Beatmaps.Any(beatmap => string.Equals(beatmap.Hash, plan.SourceHash, StringComparison.OrdinalIgnoreCase));
    }

    private static bool tryGetSourcePath(BeatmapSetInfo set, out string sourcePath)
    {
        foreach (var beatmap in set.Beatmaps)
        {
            if (!string.Equals(beatmap.Ruleset.ShortName, O2LazerIdentity.ShortName, StringComparison.Ordinal))
                continue;

            var sourceFileName = O2JamExternalChart.SourceFileName(beatmap);
            if (string.IsNullOrWhiteSpace(beatmap.Metadata.Source) || string.IsNullOrWhiteSpace(sourceFileName))
                continue;

            try
            {
                sourcePath = Path.GetFullPath(Path.Combine(beatmap.Metadata.Source, sourceFileName));
                return true;
            }
            catch (Exception)
            {
                // Malformed paths remain untouched because their intended source cannot be proven.
            }
        }

        sourcePath = string.Empty;
        return false;
    }

    internal static DateTimeOffset getSourceTimestamp(string path)
    {
        var milliseconds = new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeMilliseconds();
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }

    private static long? parseSourceLength(IEnumerable<string> tags)
    {
        foreach (var tag in tags)
        {
            if (tag.StartsWith(source_length_prefix, StringComparison.Ordinal)
                && long.TryParse(tag.AsSpan(source_length_prefix.Length), out var length))
                return length;
        }

        return null;
    }

    private static string detectImageExtension(IReadOnlyList<byte> image)
    {
        if (image.Count >= 8
            && image[0] == 0x89 && image[1] == (byte)'P' && image[2] == (byte)'N' && image[3] == (byte)'G')
            return ".png";
        if (image.Count >= 3 && image[0] == 0xff && image[1] == 0xd8 && image[2] == 0xff)
            return ".jpg";
        if (image.Count >= 2 && image[0] == (byte)'B' && image[1] == (byte)'M')
            return ".bmp";

        return ".jpg";
    }
}
