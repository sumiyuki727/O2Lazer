using System;
using osu.Framework.Localisation;

namespace osu.Game.Rulesets.O2Lazer.Localisation;

/// <summary>
/// Resolves at formatting time so existing UI bindings follow lazer's language changes.
/// Nested localisable arguments and number formatting remain owned by the framework.
/// </summary>
internal sealed class ResourceLocalisableString(EmbeddedLocalisationCatalog catalog, string key, object[] args)
    : LocalisableFormattableString(catalog.GetEnglish(key), args)
{
    private readonly EmbeddedLocalisationCatalog catalog = catalog;
    private readonly string key = key;

    protected override string FormatString(string format, object?[] formatArgs, LocalisationParameters parameters) =>
        base.FormatString(catalog.Get(key, parameters.Store?.EffectiveCulture), formatArgs, parameters);

    public override bool Equals(ILocalisableStringData? other) =>
        other is ResourceLocalisableString resource && ReferenceEquals(catalog, resource.catalog)
        && key == resource.key && base.Equals((LocalisableFormattableString)resource);

    public override bool Equals(object? obj) => obj is ILocalisableStringData localisable && Equals(localisable);

    public override int GetHashCode() => HashCode.Combine(catalog, key, base.GetHashCode());
}
