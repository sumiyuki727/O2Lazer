using System.Collections.Generic;
using osu.Game.Rulesets.O2Lazer.Core;

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
    IReadOnlyList<O2JamImportChart> Charts);

public sealed record O2JamImportChart(
    O2JamDifficulty Difficulty,
    ushort Level,
    string Md5Hash,
    double Length,
    int TotalObjectCount,
    int HoldObjectCount);
