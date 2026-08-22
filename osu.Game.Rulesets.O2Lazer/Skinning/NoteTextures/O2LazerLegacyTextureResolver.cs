using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Configuration;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Skinning.NoteTextures;

/// <summary>
/// Centralises legacy mania/O2LAZER image-name fallback rules and texture resolution.
/// </summary>
/// <remarks>
/// Keeping these rules outside drawable classes prevents rendering code from also becoming
/// responsible for skin.ini semantics.
/// </remarks>
public static class O2LazerLegacyTextureResolver
{
    /// <summary>
    /// Mirrors mania's <c>ManiaLegacySkinTransformer</c> key-texture gate. A legacy skin only
    /// renders mania note textures when it actually provides a mania key texture; otherwise mania
    /// falls back to its procedural default note/body pieces. O2Jam must follow the same rule.
    /// </summary>
    public static bool HasManiaKeyTexture(ISkin skin)
    {
        var keyLookup = new O2LazerSkinComponentLookup(O2LazerSkinComponents.KeyArea, O2LazerLayoutVariant.O2Jam7K, 0);
        var keyImage = skin.GetConfig<O2LazerSkinConfigurationLookup, string>(
            new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.KeyImage, keyLookup))?.Value
            ?? "mania-key1";

        return skin.GetAnimation(keyImage, true, true) != null;
    }

    public static string FallbackColumnIndex(O2LazerSkinComponentLookup lookup)
    {
        if (lookup.IsScratch)
            return "S";

        // O2Jam's middle lane is mania's special column; mania uses the "S" suffix for it
        // (e.g. mania-noteS / mania-noteST), not the alternating 1/2 note index.
        if (lookup.LayoutVariant == O2LazerLayoutVariant.O2Jam7K
            && lookup.ManiaColumnIndex == lookup.ManiaKeyCount / 2)
            return "S";

        var maniaColumnsPerStage = lookup.LayoutVariant switch
        {
            O2LazerLayoutVariant.Mania5KDouble => 5,
            O2LazerLayoutVariant.Mania7KDouble => 7,
            O2LazerLayoutVariant.Mania9KDouble => 9,
            _ => lookup.ManiaKeyCount,
        };
        var columnInStage = Math.Clamp(lookup.ManiaColumnIndex ?? 0, 0, Math.Max(0, maniaColumnsPerStage - 1)) % maniaColumnsPerStage;
        var distanceToEdge = Math.Min(columnInStage, maniaColumnsPerStage - 1 - columnInStage);
        return distanceToEdge % 2 == 0 ? "1" : "2";
    }

    /// <summary>
    /// Resolves note textures by iterating the fallback chain for the given component
    /// and returning the first set of valid textures (or animation frames) found.
    /// </summary>
    /// <returns>An array of textures (frames for animated notes), or an empty array if no candidate resolved.</returns>
    public static Texture[] ResolveNoteTextures(ISkin skin, O2LazerSkinComponentLookup lookup)
    {
        var fallback = FallbackColumnIndex(lookup);

        foreach (var imageName in enumerateNoteCandidates(skin, lookup, fallback))
        {
            if (string.IsNullOrWhiteSpace(imageName))
                continue;

            var textures = skin.GetTextures(imageName, WrapMode.ClampToEdge, WrapMode.ClampToEdge, true, "-", null, out _)
                .Where(t => t.DisplayWidth > 0 && t.DisplayHeight > 0)
                .ToArray();

            if (textures.Length > 0)
                return textures;
        }

        return [];
    }

    /// <summary>
    /// Resolves the first valid note texture for the given component.
    /// Convenience wrapper around <see cref="ResolveNoteTextures"/> for callers that only
    /// need a single texture's dimensions rather than a full animation frame set.
    /// </summary>
    public static Texture? ResolveNoteTexture(ISkin skin, O2LazerSkinComponentLookup lookup)
        => ResolveNoteTextures(skin, lookup).FirstOrDefault();

    public static IEnumerable<string?> HoldBodyImageCandidates(ISkin skin, O2LazerSkinComponentLookup lookup)
    {
        var fallback = FallbackColumnIndex(lookup);
        var configuredBody = skin.GetConfig<O2LazerSkinConfigurationLookup, string>(new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HoldNoteBodyImage, lookup))?.Value;

        // O2Jam must match mania exactly: a configured body image is authoritative and the
        // fallback is always the dedicated mania long-body texture, never the short note texture.
        if (lookup.LayoutVariant == O2LazerLayoutVariant.O2Jam7K)
        {
            yield return configuredBody ?? $"mania-note{fallback}L";
            yield break;
        }

        var configuredNote = skin.GetConfig<O2LazerSkinConfigurationLookup, string>(new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value;
        var bodyIsShortNoteFallback = !string.IsNullOrWhiteSpace(configuredBody) && configuredBody == configuredNote;

        if (bodyIsShortNoteFallback)
        {
            yield return $"mania-note{fallback}L";
            yield return configuredNote;
        }
        else
        {
            yield return configuredBody ?? $"mania-note{fallback}L";
        }
    }

    private static IEnumerable<string?> enumerateNoteCandidates(ISkin? skin, O2LazerSkinComponentLookup lookup, string fallback)
    {
        switch (lookup.Component)
        {
            case O2LazerSkinComponents.Mine:
                yield return skin?.GetConfig<O2LazerSkinConfigurationLookup, string>(new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.Hit100, lookup))?.Value
                             ?? "mania-noteS";

                break;

            case O2LazerSkinComponents.HoldNoteHead:
                yield return skin?.GetConfig<O2LazerSkinConfigurationLookup, string>(new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HoldNoteHeadImage, lookup))?.Value
                             ?? $"mania-note{fallback}H";
                yield return skin?.GetConfig<O2LazerSkinConfigurationLookup, string>(new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value
                             ?? $"mania-note{fallback}";

                break;

            case O2LazerSkinComponents.HoldNoteTail:
                yield return skin?.GetConfig<O2LazerSkinConfigurationLookup, string>(new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HoldNoteTailImage, lookup))?.Value
                             ?? $"mania-note{fallback}T";
                yield return skin?.GetConfig<O2LazerSkinConfigurationLookup, string>(new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HoldNoteHeadImage, lookup))?.Value
                             ?? $"mania-note{fallback}H";
                yield return skin?.GetConfig<O2LazerSkinConfigurationLookup, string>(new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value
                             ?? $"mania-note{fallback}";

                break;

            default:
                yield return skin?.GetConfig<O2LazerSkinConfigurationLookup, string>(new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value
                             ?? $"mania-note{fallback}";

                break;
        }
    }
}
