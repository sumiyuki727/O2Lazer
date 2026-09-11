using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.Objects;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps;

internal static class O2JamManiaScoreBeatmapAdapter
{
    public static void Apply(IBeatmap beatmap, float overallDifficulty, float drainRate)
    {
        if (beatmap is not O2JamBeatmap o2JamBeatmap)
            return;

        o2JamBeatmap.Difficulty.OverallDifficulty = overallDifficulty;
        o2JamBeatmap.Difficulty.DrainRate = drainRate;

        // Replacing the O2Jam subclasses before ApplyDefaults() lets mania own judgements,
        // hit windows and hold children while retaining the imported chart timing and samples.
        o2JamBeatmap.HitObjects = o2JamBeatmap.HitObjects.Select<ManiaHitObject, ManiaHitObject>(hitObject => hitObject switch
        {
            O2JamHoldNote hold => new HoldNote
            {
                StartTime = hold.StartTime,
                Duration = hold.Duration,
                Column = hold.Column,
                Samples = [.. hold.Samples],
                NodeSamples = [[.. hold.GetNodeSamples(0)], []],
                PlaySlidingSamples = hold.PlaySlidingSamples,
            },
            O2JamNote note => new Note
            {
                StartTime = note.StartTime,
                Column = note.Column,
                Samples = [.. note.Samples],
            },
            _ => hitObject,
        }).ToList();
    }
}
