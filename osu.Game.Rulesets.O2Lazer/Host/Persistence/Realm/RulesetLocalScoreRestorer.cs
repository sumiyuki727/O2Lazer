using System;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets;
using osu.Game.Scoring;
using Realms;

namespace osu.Game.Rulesets.O2Lazer.Import;

internal sealed class RulesetLocalScoreRestorer(RealmAccess access, string rulesetShortName)
{
    private const int hash_batch_size = 128;

    public int Restore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hashes = access.Run<string[]>(database =>
        {
            var ruleset = database.Find<RulesetInfo>(rulesetShortName);
            if (ruleset == null)
                return [];
            return database.All<ScoreInfo>()
                           .Filter("Ruleset == $0 AND DeletePending == false AND (BeatmapInfo == NULL OR BeatmapInfo.BeatmapSet == NULL OR BeatmapInfo.BeatmapSet.DeletePending == true)", ruleset)
                           .AsEnumerable().Select(score => score.BeatmapHash)
                           .Where(hash => !string.IsNullOrEmpty(hash)).Distinct(StringComparer.Ordinal).ToArray();
        });
        var restored = 0;
        for (var offset = 0; offset < hashes.Length; offset += hash_batch_size)
        {
            cancellationToken.ThrowIfCancellationRequested();
            restored += access.Write(database =>
            {
                var ruleset = database.Find<RulesetInfo>(rulesetShortName);
                if (ruleset == null)
                    return 0;
                var count = 0;
                foreach (var hash in hashes.Skip(offset).Take(hash_batch_size))
                {
                    var targets = database.All<BeatmapInfo>()
                                          .Filter("Ruleset == $0 AND Hash == $1 AND BeatmapSet != NULL AND BeatmapSet.DeletePending == false", ruleset, hash)
                                          .AsEnumerable().Take(2).ToArray();
                    if (targets.Length != 1)
                        continue;
                    var target = targets[0];
                    var matches = database.All<ScoreInfo>().Where(score => score.BeatmapHash == hash);
                    var pending = matches.Filter("Ruleset == $0 AND DeletePending == false AND (BeatmapInfo == NULL OR BeatmapInfo.BeatmapSet == NULL OR BeatmapInfo.BeatmapSet.DeletePending == true)", ruleset).ToArray();
                    if (pending.Length == 0)
                        continue;

                    // Native reattachment has no ruleset/deletion filter. Keep its common path,
                    // but never move another ruleset's or a deleted score when hashes coincide.
                    if (!matches.Filter("Ruleset != $0 OR DeletePending == true", ruleset).Any())
                        target.UpdateLocalScores(database);
                    else
                        foreach (var score in pending)
                            score.BeatmapInfo = target;
                    count += pending.Length;
                }
                return count;
            });
        }
        return restored;
    }
}
