using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

public static class O2LazerBeatmapSearchPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.BeatmapSearch";

    private static readonly Regex o2ma_query_pattern = new(
        @"^o2ma(\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly object install_lock = new();

    public static bool IsInstalled { get; private set; }

    public static void InstallOnce()
    {
        lock (install_lock)
        {
            if (IsInstalled)
                return;

            var target = AccessTools.Method(
                typeof(BeatmapInfoExtensions),
                nameof(BeatmapInfoExtensions.Match),
                [typeof(IBeatmapInfo), typeof(FilterCriteria.OptionalTextFilter[])]);
            var prefixMethod = AccessTools.Method(typeof(O2LazerBeatmapSearchPatcher), nameof(prefix));

            var missing = new[]
            {
                (name: "BeatmapInfoExtensions.Match", member: (MemberInfo?)target),
                (name: "O2LazerBeatmapSearchPatcher.prefix", member: prefixMethod),
            }.Where(m => m.member == null).Select(m => m.name).ToArray();

            if (missing.Length > 0)
            {
                O2LazerLogger.Log("O2LAZER BeatmapSearchPatcher: Cannot install Harmony patch. Missing: " + string.Join(", ", missing), level: LogLevel.Error);
                return;
            }

            new Harmony(harmony_id).Patch(target, prefix: new HarmonyMethod(prefixMethod));
            IsInstalled = true;
        }
    }

    // ReSharper disable once InconsistentNaming
    private static bool prefix(IBeatmapInfo beatmapInfo, FilterCriteria.OptionalTextFilter[] filters, ref bool __result)
    {
        if (beatmapInfo.Ruleset.ShortName != Constant.SHORT_NAME)
            return true;

        var hasO2MaQuery = filters.Any(isO2MaQuery);
        var hasNumericQuery = filters.Any(isNumericQuery);
        if (!hasO2MaQuery && !hasNumericQuery)
            return true;

        foreach (var filter in filters)
        {
            if (isO2MaQuery(filter))
            {
                if (filter.Matches(beatmapInfo.DifficultyName)
                    || matchesNonTagMetadata(beatmapInfo.Metadata, filter)
                    || matchesExactO2MaTag(beatmapInfo.Metadata.Tags, filter))
                    continue;
            }
            else if (hasNumericQuery && isNumericQuery(filter))
            {
                // Bare numbers must not hit the o2ma search tags; keep normal metadata matching.
                if (filter.Matches(beatmapInfo.DifficultyName) || matchesNonTagMetadata(beatmapInfo.Metadata, filter))
                    continue;
            }
            else if (filter.Matches(beatmapInfo.DifficultyName) || BeatmapMetadataInfoExtensions.Match(beatmapInfo.Metadata, filter))
                continue;

            __result = false;
            return false;
        }

        __result = true;
        return false;
    }
    // ReSharper restore InconsistentNaming

    private static bool isO2MaQuery(FilterCriteria.OptionalTextFilter filter) =>
        filter.MatchMode == FilterCriteria.MatchMode.Substring
        && o2ma_query_pattern.IsMatch(filter.SearchTerm);

    private static bool isNumericQuery(FilterCriteria.OptionalTextFilter filter) =>
        filter.MatchMode == FilterCriteria.MatchMode.Substring
        && filter.SearchTerm.Length > 0
        && filter.SearchTerm.All(char.IsDigit);

    private static bool matchesNonTagMetadata(IBeatmapMetadataInfo metadata, FilterCriteria.OptionalTextFilter filter) =>
        filter.Matches(metadata.Author.Username)
        || filter.Matches(metadata.Artist)
        || filter.Matches(metadata.ArtistUnicode)
        || filter.Matches(metadata.Title)
        || filter.Matches(metadata.TitleUnicode)
        || filter.Matches(metadata.Source);

    private static bool matchesExactO2MaTag(string tags, FilterCriteria.OptionalTextFilter filter)
    {
        var containsExact = !string.IsNullOrEmpty(tags)
            && Regex.IsMatch(tags, $@"(^|\s){Regex.Escape(filter.SearchTerm)}(\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return filter.ExcludeTerm ? !containsExact : containsExact;
    }
}
