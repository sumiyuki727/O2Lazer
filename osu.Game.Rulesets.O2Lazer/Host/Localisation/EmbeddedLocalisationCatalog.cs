using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace osu.Game.Rulesets.O2Lazer.Localisation;

/// <summary>
/// Embedded resources keep language additions data-only while retaining single-DLL deployment.
/// ResourceManager handles resource access; culture fallback is explicit because no satellite
/// assemblies are shipped alongside a custom ruleset.
/// </summary>
internal sealed class EmbeddedLocalisationCatalog
{
    private readonly ResourceManager english;
    private readonly Dictionary<string, ResourceManager> translations = new(StringComparer.OrdinalIgnoreCase);

    public EmbeddedLocalisationCatalog(Assembly assembly, string resourcePrefix)
    {
        english = new ResourceManager(resourcePrefix, assembly);
        var prefix = resourcePrefix + ".";
        const string suffix = ".resources";
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (name == resourcePrefix + suffix || !name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal))
                continue;
            var language = name[prefix.Length..^suffix.Length];
            if (language.Length == 0)
                continue;
            // Validate resource names on discovery so a mistyped locale cannot silently ship.
            var culture = CultureInfo.GetCultureInfo(language);
            translations.Add(culture.Name, new ResourceManager(name[..^suffix.Length], assembly));
        }
    }

    public IEnumerable<string> Languages => translations.Keys;

    public string GetEnglish(string key) =>
        english.GetString(key, CultureInfo.InvariantCulture)
        ?? throw new InvalidOperationException($"Missing English localisation resource: {key}");

    public string Get(string key, CultureInfo? culture)
    {
        for (var candidate = culture; candidate != null && candidate.Name.Length != 0; candidate = candidate.Parent)
        {
            if (translations.TryGetValue(candidate.Name, out var resources)
                && resources.GetString(key, CultureInfo.InvariantCulture) is { } translation)
                return translation;
        }
        return GetEnglish(key);
    }
}
