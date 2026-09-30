using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using O2Jam.Core;
using O2Jam.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Import;

/// <summary>
/// Produces database-independent import metadata from one OJN file.
/// </summary>
public sealed class O2JamImportPlanner
{
    public O2JamImportPlan Create(string sourcePath, O2JamImportedSource? importedSource = null)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        var snapshot = O2JamSourceSnapshot.Read(fullPath);
        var sourceData = snapshot.Data;
        var sourceHash = snapshot.Hash;
        OjnMetadataEncoding? encodingFallback = null;
        var document = new OjnReader(OjnMetadataEncoding.Automatic, () =>
        {
            var encoding = OjnDirectoryEncoding.Shared.GetForFile(fullPath);
            encodingFallback = encoding;
            return encoding;
        }).Read(sourceData);

        var charts = document.Charts
                             .Where(chart => chart.Notes.Any(note => note.IsPlayable))
                             .Select(chart =>
                             {
                                 var timingMap = new O2JamTimingMap(document.Metadata.InitialBpm, chart.BpmEvents.Select(change => change.ToGameplay()));
                                 var finalPosition = chart.Notes.Select(note => note.EndPosition ?? note.Position).DefaultIfEmpty(0).Max();
                                 var objectLength = timingMap.TimeAt(finalPosition) + 5000;
                                 var declaredLength = document.Metadata.Durations[(int)chart.Difficulty] * 1000d;
                                 var playable = chart.Notes.Where(note => note.IsPlayable).ToArray();
                                 // Text context and import projection revisions do not change the
                                 // native no-mod attributes of identical source bytes.
                                 var cache = string.Equals(importedSource?.SourceHash, sourceHash, StringComparison.OrdinalIgnoreCase)
                                     ? importedSource?.ManiaCache?.SingleOrDefault(candidate => candidate.Difficulty == chart.Difficulty.ToGameplay())
                                     : null;
                                 if (cache != null && (cache.Version != O2JamManiaStarRating.CacheVersion
                                                       || !double.IsFinite(cache.StarRating) || cache.StarRating < 0 || cache.MaxCombo < 0))
                                     cache = null;
                                 if (cache == null)
                                 {
                                     var beatmap = new OjnBeatmapFactory().Create(document, chart.Difficulty);
                                     var attributes = O2JamManiaStarRating.CalculateAttributes(beatmap, [], false);
                                     cache = new O2JamImportDifficultyCache(chart.Difficulty.ToGameplay(), attributes.StarRating, attributes.MaxCombo, O2JamManiaStarRating.CacheVersion);
                                 }

                                 return new O2JamImportChart(
                                     chart.Difficulty.ToGameplay(),
                                     chart.Level,
                                     calculateDifficultyMd5(sourceData, chart.Difficulty.ToGameplay()),
                                     Math.Max(objectLength, declaredLength),
                                     playable.Length,
                                     playable.Count(note => note.EndPosition != null),
                                     cache.StarRating,
                                     cache.MaxCombo);
                             })
                             .ToArray();

        if (charts.Length == 0)
            throw new InvalidDataException("The OJN contains no playable charts.");

        var title = string.IsNullOrWhiteSpace(document.Metadata.Title)
            ? Path.GetFileNameWithoutExtension(fullPath)
            : document.Metadata.Title;
        var cover = document.Metadata.Cover;
        var background = cover.Length > 0 ? cover : document.Metadata.Thumbnail;
        var genericSetIdentity = string.Concat(charts.Select(chart => chart.Md5Hash).OrderBy(hash => hash, StringComparer.Ordinal));
        var setHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{O2LazerIdentity.ShortName}:{genericSetIdentity}"))).ToLowerInvariant();

        return new O2JamImportPlan(
            fullPath,
            Path.GetDirectoryName(fullPath)!,
            Path.GetFileName(fullPath),
            sourceData,
            sourceHash,
            setHash,
            document.Metadata.SongId,
            title,
            document.Metadata.Artist,
            document.Metadata.NoteArranger,
            document.Metadata.InitialBpm,
            background,
            charts)
        {
            SourceTimestamp = snapshot.Timestamp,
            EncodingFallback = encodingFallback,
        };
    }

    private static string calculateDifficultyMd5(byte[] source, O2JamDifficulty difficulty)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        hash.AppendData(source);
        hash.AppendData([(byte)difficulty]);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
