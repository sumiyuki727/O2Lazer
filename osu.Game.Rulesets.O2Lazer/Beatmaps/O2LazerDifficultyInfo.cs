using System;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Parsing;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps;

/// <summary>
/// Represents the O2LAZER-specific difficulty parameters parsed from chart headers
/// (#RANK, #TOTAL, #PLAYLEVEL, total columns). Provides mapping helpers to and from
/// osu! <see cref="BeatmapInfo.DifficultyName"/> and <see cref="BeatmapDifficulty"/>.
/// </summary>
public readonly struct O2LazerDifficultyInfo
{
    /// <summary>Pre-parsed difficulty name from chart title/filename (e.g. "SP Beginner"). Takes priority over PlayLevel/Rank.</summary>
    public string? ParsedName { get; init; }

    /// <summary>#PLAYLEVEL — star rating (1–20+). Null if absent.</summary>
    public float? PlayLevel { get; init; }

    /// <summary>#RANK — gauge difficulty: 0=VeryHard, 1=Hard, 2=Normal, 3=Easy, 4=VeryEasy.</summary>
    public int Rank { get; init; }

    /// <summary>
    /// #DEFEXRANK / #EXRANK — judgement difficulty as a percentage (100 = RANK 2 / NORMAL),
    /// or null when the chart only specifies <see cref="Rank"/>. When set, it overrides
    /// <see cref="Rank"/> for judgement-window scaling.
    /// </summary>
    public double? ExRank { get; init; }

    /// <summary>#TOTAL — gauge recovery coefficient. Zero means default formula.</summary>
    public double Total { get; init; }

    /// <summary>Playable column count (inferred from chart or CircleSize).</summary>
    public int KeyCount { get; init; }

    public O2LazerLongNoteMode LockedLongNoteMode { get; init; }

    /// <summary>
    /// Converts an O2Jam level to the star rating shown in song select. osu! floors the displayed
    /// value to two decimals, so naive double division (e.g. 41 / 10) lands just below the intended
    /// value and renders as 4.09. Multiplying by 0.1 (whose double is slightly above 1/10) keeps the
    /// result on or above the display boundary for integer levels.
    /// </summary>
    public static double ComputeStarRating(double level) => level * 0.1;

    /// <summary>
    /// Reads O2LAZER difficulty from a parse result.
    /// </summary>
    public static O2LazerDifficultyInfo FromParseResult(O2LazerParseResult result) => new()
    {
        PlayLevel = result.PlayLevel,
        Rank = result.Rank,
        ExRank = result.DefaultExRank,
        Total = result.Total,
        KeyCount = result.TotalColumns,
        LockedLongNoteMode = result.LockedLongNoteMode,
    };

    /// <summary>
    /// OverallDifficulty carries the judgement difficulty. The integer #RANK is stored
    /// directly as OD 0-4. When #DEFEXRANK/#EXRANK overrides RANK, the percentage is stored as
    /// <see cref="exrank_od_sentinel"/> + pct, so the two encodings never collide
    /// (EXRANK OD ≥ 100, RANK OD ≤ 4).
    /// </summary>
    private const double exrank_od_sentinel = 100;

    /// <summary>
    /// Encodes <see cref="Rank"/> / <see cref="ExRank"/> into an osu! OverallDifficulty value.
    /// </summary>
    public static float EncodeOverallDifficulty(int rank, double? exRank)
        => exRank is { } pct ? (float)(exrank_od_sentinel + pct) : rank;

    /// <summary>
    /// Decodes <see cref="Rank"/> and <see cref="ExRank"/> from an osu! OverallDifficulty value.
    /// When the OD carries an EXRANK percentage, <see cref="Rank"/> is normalised to 2 (NORMAL),
    /// since the chart's #RANK is overridden and not recoverable from OD alone.
    /// </summary>
    public static (int rank, double? exRank) DecodeFromOverallDifficulty(float od)
        => od >= exrank_od_sentinel ? (2, od - exrank_od_sentinel) : ((int)Math.Round(od), null);

    /// <summary>
    /// Converts the O2LAZER difficulty to a display name string suitable for
    /// <see cref="BeatmapInfo.DifficultyName"/>.
    /// Returns the <see cref="PlayLevel"/> value when set, or a name based on <see cref="Rank"/>.
    /// </summary>
    public string ToDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(ParsedName))
            return ParsedName;

        if (PlayLevel.HasValue)
            return PlayLevel.Value.ToString("F0");

        return Rank switch
        {
            0 => "Very Hard",
            1 => "Hard",
            2 => "Normal",
            3 => "Easy",
            4 => "Very Easy",
            _ => "Normal",
        };
    }

    /// <summary>
    /// Reconstructs a <see cref="O2LazerDifficultyInfo"/> from an osu! difficulty object.
    /// <see cref="Total"/> is stored in <see cref="IBeatmapDifficultyInfo.ApproachRate"/>.
    /// <see cref="PlayLevel"/> cannot be recovered from this source.
    /// </summary>
    public static O2LazerDifficultyInfo FromOsuDifficulty(IBeatmapDifficultyInfo difficulty)
    {
        var (rank, exRank) = DecodeFromOverallDifficulty(difficulty.OverallDifficulty);
        return new O2LazerDifficultyInfo
        {
            Rank = rank,
            ExRank = exRank,
            Total = Math.Max(0, difficulty.ApproachRate),
            KeyCount = GetKeyCount(difficulty),
            LockedLongNoteMode = (O2LazerLongNoteMode)Math.Clamp((int)Math.Round(difficulty.DrainRate), 0, 3),
        };
    }

    /// <summary>
    /// Writes O2LAZER difficulty parameters to an osu! <see cref="IBeatmap"/>.
    /// Sets <see cref="BeatmapInfo.DifficultyName"/> and the relevant
    /// <see cref="BeatmapDifficulty"/> fields.
    /// </summary>
    public void WriteToOsuDifficulty(IBeatmap beatmap)
    {
        beatmap.BeatmapInfo.DifficultyName = ToDisplayName();
        WriteToOsuDifficulty(beatmap.Difficulty);
        WriteToOsuDifficulty(beatmap.BeatmapInfo.Difficulty);
    }

    /// <summary>
    /// Writes O2LAZER difficulty parameters to an osu! <see cref="IBeatmap"/>.
    /// Sets <see cref="BeatmapInfo.DifficultyName"/> and the relevant
    /// <see cref="BeatmapDifficulty"/> fields.
    /// </summary>
    public void WriteToOsuDifficulty(BeatmapInfo beatmap)
    {
        beatmap.DifficultyName = ToDisplayName();
        WriteToOsuDifficulty(beatmap.Difficulty);
    }

    /// <summary>
    /// Writes O2LAZER difficulty parameters to an osu! <see cref="BeatmapDifficulty"/>.
    /// </summary>
    public void WriteToOsuDifficulty(BeatmapDifficulty difficulty)
    {
        var od = EncodeOverallDifficulty(Rank, ExRank);
        difficulty.OverallDifficulty = od;
        difficulty.CircleSize = KeyCount;
        difficulty.ApproachRate = (float)Total;
        difficulty.DrainRate = (float)LockedLongNoteMode;
    }

    /// <summary>
    /// Reads the total column count from an osu! difficulty object (<see cref="BeatmapDifficulty"/>
    /// or <see cref="IBeatmapDifficultyInfo"/>).
    /// </summary>
    public static int GetKeyCount(IBeatmapDifficultyInfo difficulty) =>
        Math.Max(1, (int)Math.Round(difficulty.CircleSize));

}
