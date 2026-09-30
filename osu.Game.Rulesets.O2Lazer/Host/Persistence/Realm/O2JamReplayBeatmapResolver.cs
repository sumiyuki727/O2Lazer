using System;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using Realms;

namespace osu.Game.Rulesets.O2Lazer.Replays;

internal static class O2JamReplayBeatmapResolver
{
    public static BeatmapInfo? Resolve(Realm database, string hash, string md5)
    {
        if (string.IsNullOrWhiteSpace(hash) && string.IsNullOrWhiteSpace(md5))
            return null;

        var candidates = database.All<BeatmapInfo>()
                                 .Filter($"{nameof(BeatmapInfo.Ruleset)}.{nameof(RulesetInfo.ShortName)} == $0", O2LazerIdentity.ShortName)
                                 .Filter($"{nameof(BeatmapInfo.BeatmapSet)}.{nameof(BeatmapSetInfo.DeletePending)} == false")
                                 .AsEnumerable()
                                 .Where(candidate => !string.IsNullOrWhiteSpace(md5)
                                     ? string.Equals(candidate.MD5Hash, md5, StringComparison.OrdinalIgnoreCase)
                                     : string.Equals(candidate.Hash, hash, StringComparison.OrdinalIgnoreCase))
                                 .Take(2).ToArray();
        if (candidates.Length != 1)
            return null;

        var beatmap = candidates[0];
        if (string.IsNullOrWhiteSpace(hash) || string.Equals(beatmap.Hash, hash, StringComparison.OrdinalIgnoreCase))
            return beatmap;

        // An independent difficulty MD5 can disambiguate a known old source SHA-256.
        // An arbitrary mismatching hash must never fall back to an otherwise valid MD5.
        var sourceHashes = beatmap.BeatmapSet!.Files
                                  .Where(file => file.Filename.EndsWith(".ojn", StringComparison.OrdinalIgnoreCase))
                                  .Select(file => file.File.Hash).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return sourceHashes.Length == 1 && string.Equals(sourceHashes[0], hash, StringComparison.OrdinalIgnoreCase) ? beatmap : null;
    }
}
