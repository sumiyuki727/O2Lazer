using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.O2Lazer.Replays;

/// <summary>
/// Generates native O2Jam replay frames for future autoplay-style mods.
/// </summary>
internal sealed class O2JamAutoGenerator(O2JamBeatmap beatmap) : AutoGenerator<O2JamReplayFrame>(beatmap)
{
    internal const double ReleaseDelay = 20;

    private new O2JamBeatmap Beatmap => (O2JamBeatmap)base.Beatmap;

    protected override void GenerateFrames()
    {
        if (Beatmap.HitObjects.Count == 0)
            return;

        var actions = new List<ManiaAction>();
        var groups = createActionPoints().GroupBy(point => point.Time).OrderBy(group => group.Key);

        foreach (var group in groups)
        {
            foreach (var point in group)
            {
                var action = ManiaAction.Key1 + point.Column;

                if (point.Pressed)
                    actions.Add(action);
                else
                    actions.Remove(action);
            }

            Frames.Add(new O2JamReplayFrame(group.Key, actions.ToArray()));
        }
    }

    private IEnumerable<ActionPoint> createActionPoints()
    {
        for (var index = 0; index < Beatmap.HitObjects.Count; index++)
        {
            var current = Beatmap.HitObjects[index];
            var nextInColumn = GetNextObject(index);

            yield return new ActionPoint(current.StartTime, current.Column, true);
            yield return new ActionPoint(releaseTimeFor(current, nextInColumn), current.Column, false);
        }
    }

    private static double releaseTimeFor(HitObject current, HitObject? next)
    {
        var endTime = current.GetEndTime();
        var delay = ReleaseDelay;

        if (current is HoldNote hold)
        {
            if (hold.Duration > 0)
                return endTime;

            delay = 1;
        }

        if (next == null || next.StartTime > endTime + delay)
            return endTime + delay;

        // Releasing just before the following note prevents overlapping states in one column.
        return endTime + (next.StartTime - endTime) * 0.9;
    }

    protected override HitObject? GetNextObject(int currentIndex)
    {
        var column = Beatmap.HitObjects[currentIndex].Column;

        for (var index = currentIndex + 1; index < Beatmap.HitObjects.Count; index++)
        {
            if (Beatmap.HitObjects[index].Column == column)
                return Beatmap.HitObjects[index];
        }

        return null;
    }

    private readonly record struct ActionPoint(double Time, int Column, bool Pressed);
}
