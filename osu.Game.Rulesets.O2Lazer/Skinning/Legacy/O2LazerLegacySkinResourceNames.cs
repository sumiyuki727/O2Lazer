using System;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.NoteTextures;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Legacy;

internal sealed class O2LazerLegacySkinResourceNames(
    Func<LegacyManiaSkinConfigurationLookups, O2LazerSkinComponentLookup?, int?, string?> getStringConfig)
{
    public string GetKeyImageName(O2LazerSkinComponentLookup lookup, bool down) =>
        getStringConfig(down ? LegacyManiaSkinConfigurationLookups.KeyImageDown : LegacyManiaSkinConfigurationLookups.KeyImage, lookup, null)
        ?? $"mania-key{O2LazerLegacyTextureResolver.FallbackColumnIndex(lookup)}{(down ? "D" : string.Empty)}";

    public string GetHitExplosionImageName(O2LazerSkinComponentLookup lookup) =>
        lookup.IsLongNote
            ? getStringConfig(LegacyManiaSkinConfigurationLookups.HoldNoteLightImage, lookup, null) ?? "lightingL"
            : getStringConfig(LegacyManiaSkinConfigurationLookups.ExplosionImage, lookup, null) ?? "lightingN";

    public string GetHitTargetImageName() =>
        getStringConfig(LegacyManiaSkinConfigurationLookups.HitTargetImage, null, null) ?? "mania-stage-hint";

    public string[] GetStageBackgroundImageNames() =>
    [
        getStringConfig(LegacyManiaSkinConfigurationLookups.LeftStageImage, null, null) ?? "mania-stage-left",
        getStringConfig(LegacyManiaSkinConfigurationLookups.RightStageImage, null, null) ?? "mania-stage-right",
    ];

    public string GetStageForegroundImageName() =>
        getStringConfig(LegacyManiaSkinConfigurationLookups.BottomStageImage, null, null) ?? "mania-stage-bottom";
}
