using System;
using System.Collections.Generic;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Embedded;

/// <summary>
/// Static registry that maps known osu! built-in skin types to the
/// O2LazerEmbeddedSkinKind whose asset set best matches their visual style.
/// </summary>
/// <remarks>
/// Used by O2LazerEmbeddedSkinSource.GetEmbeddedSkinKind to decide which
/// embedded texture pack to activate during gameplay.
/// Lookups are exact-type matches (<c>GetType() == typeof(T)</c>); subclasses are
/// not considered, so an unrecognised user skin falls through to the default.
/// </remarks>
public static class O2LazerEmbeddedSkinDefinition
{
    /// <summary>
    /// Skins that use the O2LazerEmbeddedSkinKind.LegacyOld asset set —
    /// classic osu!stable-style visuals.
    /// </summary>
    public static readonly IReadOnlyDictionary<Type, O2LazerEmbeddedSkinKind> LEGACY_SKINS = new Dictionary<Type, O2LazerEmbeddedSkinKind>
    {
        [typeof(DefaultLegacySkin)] = O2LazerEmbeddedSkinKind.LegacyOld,
        [typeof(RetroSkin)] = O2LazerEmbeddedSkinKind.LegacyOld,
    };

    /// <summary>
    /// Skins that use the O2LazerEmbeddedSkinKind.LegacyModern asset set —
    /// modern Argon-compatible visuals.
    /// </summary>
    public static readonly IReadOnlyDictionary<Type, O2LazerEmbeddedSkinKind> MODERN_SKINS = new Dictionary<Type, O2LazerEmbeddedSkinKind>
    {
        [typeof(ArgonSkin)] = O2LazerEmbeddedSkinKind.LegacyModern,
        [typeof(ArgonProSkin)] = O2LazerEmbeddedSkinKind.LegacyModern,
        [typeof(TrianglesSkin)] = O2LazerEmbeddedSkinKind.LegacyModern,
    };

    /// <summary>
    /// Attempts to resolve the O2LazerEmbeddedSkinKind for a given skin.
    /// Modern skins are checked first; legacy skins are checked as a fallback.
    /// </summary>
    /// <param name="skin">The skin to look up.</param>
    /// <param name="kind">
    /// When this method returns <c>true</c>, the matched O2LazerEmbeddedSkinKind.
    /// </param>
    /// <returns>
    /// <c>true</c> if the skin's exact runtime type is in MODERN_SKINS
    /// or LEGACY_SKINS; otherwise <c>false</c>.
    /// </returns>
    public static bool TryGetKind(ISkin skin, out O2LazerEmbeddedSkinKind kind)
    {
        var type = skin.GetType();
        return MODERN_SKINS.TryGetValue(type, out kind) || LEGACY_SKINS.TryGetValue(type, out kind);
    }
}
