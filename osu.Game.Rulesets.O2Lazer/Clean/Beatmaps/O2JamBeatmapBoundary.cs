using osu.Game.Beatmaps;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps;

internal static class O2JamBeatmapBoundary
{
    internal static bool Crosses(IBeatmapInfo source, IRulesetInfo target)
    {
        var sourceIsO2Jam = source.Ruleset.ShortName == O2LazerIdentity.ShortName;
        var targetIsO2Jam = target.ShortName == O2LazerIdentity.ShortName;
        return sourceIsO2Jam != targetIsO2Jam;
    }
}
