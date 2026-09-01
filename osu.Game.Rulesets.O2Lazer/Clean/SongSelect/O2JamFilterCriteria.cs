using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Filter;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

internal sealed class O2JamFilterCriteria : IRulesetFilterCriteria
{
    private static readonly Regex songIdentifiers = new("o2ma[0-9]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly List<PercentageFilter> percentageFilters = [];
    private readonly List<FilterCriteria.OptionalRange<double>> levelFilters = [];

    public bool Matches(BeatmapInfo beatmapInfo, FilterCriteria criteria)
    {
        if (beatmapInfo.Ruleset.ShortName != O2LazerIdentity.ShortName)
            return false;

        if (levelFilters.Count > 0)
        {
            var level = O2JamStarRatingMetadata.ResolveLevel(beatmapInfo);
            if (levelFilters.Any(range => !range.IsInRange(level)))
                return false;
        }

        if (percentageFilters.Count > 0)
        {
            var total = beatmapInfo.TotalObjectCount;
            var holds = beatmapInfo.EndTimeObjectCount;
            if (total <= 0 || holds < 0 || holds > total)
                return false;

            foreach (var filter in percentageFilters)
            {
                // Imported counts contain each LN once, irrespective of its duration or two judgements.
                var percentage = 100d * (filter.LongNotes ? holds : total - holds) / total;
                if (!filter.Range.IsInRange(percentage))
                    return false;
            }
        }

        // osu! already applies ordinary text matching. These checks only narrow its substring
        // results, so exact OJN identifiers need no global search patch or metadata rewrite.
        foreach (var filter in criteria.SearchTerms)
        {
            if (isSongIdentifier(filter.SearchTerm))
            {
                var matches = splitTags(beatmapInfo.Metadata.Tags).Contains(filter.SearchTerm, StringComparer.OrdinalIgnoreCase);
                if (matches == filter.ExcludeTerm)
                    return false;
            }
            else if (isNumeric(filter.SearchTerm) && !matchesNumber(beatmapInfo, filter))
                return false;
        }

        return true;
    }

    public bool TryParseCustomKeywordCriteria(string key, Operator op, string value)
    {
        if (string.Equals(key, "level", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "lv", StringComparison.OrdinalIgnoreCase))
        {
            var levelRange = new FilterCriteria.OptionalRange<double>();
            if (!FilterQueryParser.TryUpdateCriteriaRange(ref levelRange, op, value, tryParseLevelThreshold))
                return false;

            levelFilters.Add(levelRange);
            return true;
        }

        var longNotes = string.Equals(key, "ln", StringComparison.OrdinalIgnoreCase);
        if (!longNotes && !string.Equals(key, "note", StringComparison.OrdinalIgnoreCase))
            return false;

        var range = new FilterCriteria.OptionalRange<double>();
        if (!FilterQueryParser.TryUpdateCriteriaRange(ref range, op, value, tryParsePercentage))
            return false;

        // Keep clauses independent so exclusions and repeated bounds are intersected, not overwritten.
        percentageFilters.Add(new PercentageFilter(longNotes, range));
        return true;
    }

    public bool FilterMayChangeFromMods(FilterCriteria criteria, ValueChangedEvent<IReadOnlyList<Mod>> mods) => false;

    private static bool tryParseLevelThreshold(string value, out double level) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out level) && double.IsFinite(level);

    private static bool tryParsePercentage(string value, out double percentage)
    {
        if (value.EndsWith('%'))
            value = value[..^1];

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out percentage)
               && double.IsFinite(percentage) && percentage is >= 0 and <= 100;
    }

    private static bool isSongIdentifier(string value) =>
        value.StartsWith("o2ma", StringComparison.OrdinalIgnoreCase) && isNumeric(value[4..]);

    private static bool isNumeric(string value) => !string.IsNullOrEmpty(value) && value.All(char.IsAsciiDigit);

    private static string[] splitTags(string tags) => tags.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static bool matchesNumber(BeatmapInfo beatmap, FilterCriteria.OptionalTextFilter filter)
    {
        var exclude = filter.ExcludeTerm;
        filter.ExcludeTerm = false;
        var metadata = beatmap.Metadata;

        // Missing OJN titles fall back to filenames; source folders can also contain o2ma IDs.
        // Removing identifiers from every field prevents those paths from leaking numeric matches.
        var matches = matchesText(beatmap.DifficultyName)
                      || matchesText(metadata.Title)
                      || matchesText(metadata.TitleUnicode)
                      || matchesText(metadata.Artist)
                      || matchesText(metadata.ArtistUnicode)
                      || matchesText(metadata.Author.Username)
                      || matchesText(metadata.Source)
                      || splitTags(metadata.Tags).Any(tag =>
                          !tag.StartsWith("o2lazer-", StringComparison.OrdinalIgnoreCase) && matchesText(tag));

        return exclude ? !matches : matches;

        bool matchesText(string text) => filter.Matches(songIdentifiers.Replace(text, " "));
    }

    private readonly record struct PercentageFilter(bool LongNotes, FilterCriteria.OptionalRange<double> Range);
}
