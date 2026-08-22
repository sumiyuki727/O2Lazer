using System;
using System.Collections.Generic;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.Filter;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

public class O2LazerFilterCriteria : IRulesetFilterCriteria
{
    private FilterCriteria.OptionalRange<float> longNotePercentage;
    private FilterCriteria.OptionalTextFilter source;

    public O2LazerFilterCriteria(O2LazerRulesetConfigManager? _)
    {
    }

    public bool Matches(BeatmapInfo beatmapInfo, FilterCriteria criteria)
    {
        // O2Jam only consumes its own imported charts; converted std/mania charts must not
        // appear even when the global "show converted beatmaps" option is enabled.
        if (beatmapInfo.Ruleset.ShortName != Constant.SHORT_NAME)
            return false;

        if (GetVariant(beatmapInfo) != O2LazerLayoutVariant.O2Jam7K)
            return false;

        if (source.HasFilter && !source.Matches(beatmapInfo.Metadata.Source))
            return false;

        if (longNotePercentage.HasFilter
            && !longNotePercentage.IsInRange(beatmapInfo.EndTimeObjectCount / (float)Math.Max(1, beatmapInfo.TotalObjectCount) * 100))
            return false;

        return true;
    }

    internal static O2LazerLayoutVariant GetVariant(BeatmapInfo beatmapInfo) => O2LazerLayoutVariant.O2Jam7K;

    public bool TryParseCustomKeywordCriteria(string key, Operator op, string strValues)
    {
        switch (key)
        {
            case "src":
            case "source":
                return FilterQueryParser.TryUpdateCriteriaText(ref source, op, strValues);

            case "ln":
            case "lns":
                return FilterQueryParser.TryUpdateCriteriaRange(ref longNotePercentage, op, strValues);
        }

        return false;
    }

    public bool FilterMayChangeFromMods(FilterCriteria criteria, ValueChangedEvent<IReadOnlyList<Mod>> mods) => false;
}
