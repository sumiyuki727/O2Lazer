using osu.Game.Skinning;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Configuration;

/// <summary>
/// Lookup key used to query O2LAZER-specific skin configuration values from
/// O2LazerLegacySkinTransformer.GetConfig{TLookup,TValue}.
/// </summary>
/// <remarks>
/// O2LAZER skin config overlaps heavily with osu!mania legacy skin config — keys such as
/// <c>NoteImage</c>, <c>KeyImage</c>, <c>ColumnWidth</c>, and <c>HitPosition</c> are
/// shared — so this lookup reuses LegacyManiaSkinConfigurationLookups
/// rather than duplicating the enum.
/// <para>
/// Resolution order in O2LazerLegacySkinTransformer:
/// <list type="number">
///   <item><description>
///     O2LAZER-section configurations parsed from <c>[O2LAZER]</c> sections of <c>skin.ini</c>,
///     matched by <c>O2LazerLayoutVariant</c>.
///   </description></item>
///   <item><description>
///     Mania-section configurations from <c>[Mania]</c> sections, matched by key count
///     (with special-style fallback for 5K/7K scratch variants).
///   </description></item>
///   <item><description>
///     The wrapped skin's native LegacyManiaSkinConfigurationLookup API,
///     using O2LazerLegacySkinTransformer's <c>maniaKeyCount</c>.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// Column resolution:
/// When ComponentLookup is provided (the lookup originates from a specific
/// playfield component), column index is derived from it — using the O2LAZER column index for
/// <c>[O2LAZER]</c> sections and the mania column index for <c>[Mania]</c> sections.
/// When only ColumnIndex is set (lookups with no per-column context, such as
/// <c>HitPosition</c>), it is used directly for both section types.
/// </para>
/// <para>
/// O2LazerBuiltInSkinTransformer always returns <c>null</c> for this lookup
/// type — built-in skins carry no <c>skin.ini</c> O2LAZER or mania configuration.
/// </para>
/// </remarks>
public class O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups lookup, O2LazerSkinComponentLookup? componentLookup = null, int? columnIndex = null)
{
    /// <summary>The mania configuration key to look up.</summary>
    public readonly LegacyManiaSkinConfigurationLookups Lookup = lookup;

    /// <summary>
    /// The component context this lookup originates from, or <c>null</c> for
    /// stage-wide lookups that have no per-column context (e.g. <c>HitPosition</c>).
    /// Carries both the O2LAZER column index and the mapped mania column index.
    /// </summary>
    public readonly O2LazerSkinComponentLookup? ComponentLookup = componentLookup;

    /// <summary>
    /// Raw column index override used when ComponentLookup is <c>null</c>.
    /// </summary>
    public readonly int? ColumnIndex = columnIndex;
}
