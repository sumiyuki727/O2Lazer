using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Skinning.Configuration;
using osu.Game.Rulesets.O2Lazer.Skinning.Embedded;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Legacy;

/// <summary>
/// get the config in skin.ini for current layout
/// </summary>
internal sealed class O2LazerLegacySkinConfigurationProvider
{

    public bool HasConfigurations => layoutVariant == O2LazerLayoutVariant.O2Jam7K
        ? skinConfigurations.Value.Any(c =>
            (c.Section == O2LazerSkinConfigurationSection.O2Lazer && c.Layout == O2LazerLayoutVariant.O2Jam7K)
            || (c.Section == O2LazerSkinConfigurationSection.Mania && c.Keys == maniaKeyCount))
        : skinConfigurations.Value.Count > 0;

    private readonly ISkin skin;
    private readonly O2LazerLayoutVariant layoutVariant;
    private readonly int maniaKeyCount;
    private readonly Lazy<IReadOnlyList<O2LazerSkinConfiguration>> skinConfigurations;

    public O2LazerLegacySkinConfigurationProvider(ISkin skin, O2LazerLayoutVariant layoutVariant)
    {
        this.skin = skin;
        this.layoutVariant = layoutVariant;
        maniaKeyCount = O2LazerLayout.GetManiaKeyCount(layoutVariant);
        skinConfigurations = new Lazy<IReadOnlyList<O2LazerSkinConfiguration>>(() =>
            skin is O2LazerEmbeddedSkin embedded
                ? O2LazerSkinConfigurationDecoder.Decode(embedded.Resources)
                : O2LazerSkinConfigurationDecoder.Decode(skin));
    }

    public IBindable<TValue>? GetConfig<TValue>(O2LazerSkinConfigurationLookup lookup)
        where TValue : notnull
    {
        foreach (var configuration in getConfigurations())
        {
            var column = getConfigurationColumn(configuration, lookup);

            if (configuration.TryGet<TValue>(lookup.Lookup, column, out var value))
                return value;
        }

        return skin.GetConfig<LegacyManiaSkinConfigurationLookup, TValue>(new LegacyManiaSkinConfigurationLookup(maniaKeyCount, lookup.Lookup,
            lookup.ComponentLookup?.ManiaColumnIndex ?? lookup.ColumnIndex));
    }

    private IEnumerable<O2LazerSkinConfiguration> getConfigurations()
    {
        if (layoutVariant == O2LazerLayoutVariant.O2Jam7K)
        {
            foreach (var configuration in skinConfigurations.Value.Where(c => c.Section == O2LazerSkinConfigurationSection.O2Lazer && c.Layout == O2LazerLayoutVariant.O2Jam7K))
                yield return configuration;

            foreach (var configuration in getManiaFallbackConfigurations())
                yield return configuration;

            yield break;
        }

        foreach (var configuration in skinConfigurations.Value.Where(c => c.Section == O2LazerSkinConfigurationSection.O2Lazer && c.Layout == layoutVariant))
            yield return configuration;

        var layout1P = layoutVariant switch
        {
            O2LazerLayoutVariant.Mania5K2P => O2LazerLayoutVariant.Mania5K,
            O2LazerLayoutVariant.Mania7K2P => O2LazerLayoutVariant.Mania7K,
            _ => (O2LazerLayoutVariant?)null,
        };

        if (layout1P != null)
        {
            foreach (var configuration in skinConfigurations.Value.Where(c => c.Section == O2LazerSkinConfigurationSection.O2Lazer && c.Layout == layout1P))
                yield return configuration;
        }

        foreach (var configuration in getManiaFallbackConfigurations())
            yield return configuration;
    }

    private IEnumerable<O2LazerSkinConfiguration> getManiaFallbackConfigurations()
    {
        var configurations = skinConfigurations.Value.Where(c => c.Section == O2LazerSkinConfigurationSection.Mania).ToArray();

        foreach (var keys in getSpecialStyleManiaFallbackKeys())
        {
            foreach (var configuration in configurations.Where(c => c.Keys == keys && c.SpecialStyle == 1))
                yield return configuration;
        }

        foreach (var keys in getScratchInclusiveManiaFallbackKeys())
        {
            foreach (var configuration in configurations.Where(c => c.Keys == keys && c.SpecialStyle != 1))
                yield return configuration;
        }

        foreach (var configuration in configurations.Where(c => c.Keys == maniaKeyCount))
            yield return configuration;
    }

    private IEnumerable<int> getSpecialStyleManiaFallbackKeys()
    {
        foreach (var keys in getScratchInclusiveManiaFallbackKeys())
            yield return keys;
    }

    private IEnumerable<int> getScratchInclusiveManiaFallbackKeys()
    {
        var totalColumns = O2LazerLayout.GetTotalColumns(layoutVariant);

        if (totalColumns != maniaKeyCount)
            yield return totalColumns;
    }

    private int? getConfigurationColumn(O2LazerSkinConfiguration configuration, O2LazerSkinConfigurationLookup lookup)
    {
        if (configuration.Section == O2LazerSkinConfigurationSection.O2Lazer || configuration.Keys != maniaKeyCount)
            return lookup.ComponentLookup?.ColumnIndex ?? lookup.ColumnIndex;

        return lookup.ComponentLookup?.ManiaColumnIndex ?? lookup.ColumnIndex;
    }
}
