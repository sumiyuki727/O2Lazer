namespace osu.Game.Rulesets.O2Lazer.IO.Import;

/// <summary>
/// Identifies the import-time derivation rules that are baked into realm metadata.
/// The marker is deliberately decoupled from the ruleset's own release version.
/// </summary>
internal static class O2LazerMetadataMarker
{
    /// <summary>
    /// Date-based revision for metadata rules that are not difficulty related.
    /// Bump when header decoding, string handling, backgrounds, statistics, or search
    /// metadata rules change. The date records when the rule set last changed.
    /// </summary>
    internal const string SCHEMA_REVISION = "2026.08.23";

    /// <summary>
    /// Mirrors <c>ManiaDifficultyCalculator.Version</c> (currently 20241007) in the pinned
    /// osu!lazer build. Mania star ratings are the only difficulty values that can change;
    /// O2Jam authored levels are stable. Bump this alongside an osu!lazer upgrade when the
    /// mania difficulty algorithm changes.
    /// </summary>
    internal const string MANIA_DIFFICULTY_VERSION = "20241007";

    internal static string CurrentMarker => $"o2lazer-meta:{SCHEMA_REVISION}:diff:{MANIA_DIFFICULTY_VERSION}";
}
