using System;
using System.Collections.Generic;
using O2Jam.Core;
using O2Jam.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Import;

public sealed record O2JamImportPlan(
    string SourcePath,
    string SourceDirectory,
    string FileName,
    byte[] SourceData,
    string SourceHash,
    string SetHash,
    uint SongId,
    string Title,
    string Artist,
    string Author,
    double InitialBpm,
    byte[] Background,
    IReadOnlyList<O2JamImportChart> Charts)
{
    public DateTimeOffset? SourceTimestamp { get; init; }
    public OjnMetadataEncoding? EncodingFallback { get; init; }
    public IReadOnlyList<O2JamImportSlot> Slots { get; init; } = [];
}

public sealed record O2JamImportSlot(O2JamDifficulty Difficulty, string Md5Hash, bool IsPlayable);

public sealed record O2JamImportChart(
    O2JamDifficulty Difficulty,
    ushort Level,
    string Md5Hash,
    double Length,
    int TotalObjectCount,
    int HoldObjectCount,
    double ManiaStarRating,
    int ManiaMaxCombo)
{
    public bool HasManiaDifficulty => double.IsFinite(ManiaStarRating) && ManiaStarRating >= 0 && ManiaMaxCombo >= 0;
}
