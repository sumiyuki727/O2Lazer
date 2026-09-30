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
    public O2JamImportPlan Create(string sourcePath)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        var sourceData = File.ReadAllBytes(fullPath);
        var document = new OjnReader(OjnMetadataEncoding.Automatic, () => OjnDirectoryEncoding.Shared.GetForFile(fullPath)).Read(sourceData);

        var charts = document.Charts
                             .Where(chart => chart.Notes.Any(note => note.IsPlayable))
                             .Select(chart =>
                             {
                                 var timingMap = new O2JamTimingMap(document.Metadata.InitialBpm, chart.BpmEvents.Select(change => change.ToGameplay()));
                                 var finalPosition = chart.Notes.Select(note => note.EndPosition ?? note.Position).DefaultIfEmpty(0).Max();
                                 var objectLength = timingMap.TimeAt(finalPosition) + 5000;
                                 var declaredLength = document.Metadata.Durations[(int)chart.Difficulty] * 1000d;
                                 var playable = chart.Notes.Where(note => note.IsPlayable).ToArray();
                                 var beatmap = new OjnBeatmapFactory().Create(document, chart.Difficulty);
                                 var maniaAttributes = O2JamManiaStarRating.CalculateAttributes(beatmap, [], false);

                                 return new O2JamImportChart(
                                     chart.Difficulty.ToGameplay(),
                                     chart.Level,
                                     calculateDifficultyMd5(sourceData, chart.Difficulty.ToGameplay()),
                                     Math.Max(objectLength, declaredLength),
                                     playable.Length,
                                     playable.Count(note => note.EndPosition != null),
                                     maniaAttributes.StarRating,
                                     maniaAttributes.MaxCombo);
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
            Convert.ToHexString(SHA256.HashData(sourceData)).ToLowerInvariant(),
            setHash,
            document.Metadata.SongId,
            title,
            document.Metadata.Artist,
            document.Metadata.NoteArranger,
            document.Metadata.InitialBpm,
            background,
            charts);
    }

    private static string calculateDifficultyMd5(byte[] source, O2JamDifficulty difficulty)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        hash.AppendData(source);
        hash.AppendData([(byte)difficulty]);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
