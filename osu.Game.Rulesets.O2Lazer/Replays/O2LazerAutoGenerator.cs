using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.IO.Input;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.O2Lazer.Replays;

public class O2LazerAutoGenerator(O2LazerBeatmap beatmap) : AutoGenerator<O2LazerReplayFrame>(beatmap)
{
    public const double RELEASE_DELAY = 10;

    // ReSharper disable once InconsistentNaming
    private new O2LazerBeatmap Beatmap => (O2LazerBeatmap)base.Beatmap;

    private readonly record struct ActionPoint(double Time, O2LazerAction Action, bool Press);

    protected override void GenerateFrames()
    {
        var active = new List<O2LazerAction>();

        foreach (var group in generateActionPoints().GroupBy(p => p.Time).OrderBy(g => g.Key))
        {
            foreach (var point in group.OrderBy(p => p.Press))
            {
                if (point.Press)
                {
                    if (!active.Contains(point.Action))
                        active.Add(point.Action);
                }
                else
                    active.Remove(point.Action);
            }

            Frames.Add(new O2LazerReplayFrame(group.Key, active.ToArray()));
        }
    }

    protected override HitObject? GetNextObject(int currentIndex)
    {
        var column = Beatmap.HitObjects[currentIndex].Column;

        for (var i = currentIndex + 1; i < Beatmap.HitObjects.Count; i++)
        {
            if (Beatmap.HitObjects[i].Column == column)
                return Beatmap.HitObjects[i];
        }

        return null;
    }

    private static double calculateReleaseTime(O2LazerHitObject current, HitObject? nextObject)
    {
        var endTime = current.GetEndTime();

        if (current is O2LazerLongNote)
        {
            return Math.Max(endTime, current.StartTime + RELEASE_DELAY);
        }

        return nextObject == null || nextObject.StartTime > endTime + RELEASE_DELAY
            ? endTime + RELEASE_DELAY
            : Math.Min(endTime + (nextObject.StartTime - endTime) * 0.9, endTime + RELEASE_DELAY);
    }

    private IEnumerable<ActionPoint> generateActionPoints()
    {
        for (var i = 0; i < Beatmap.HitObjects.Count; i++)
        {
            var current = Beatmap.HitObjects[i];

            if (O2LazerKeyBindingConfiguration.ActionForColumn(Beatmap.LayoutVariant, current.Column) is not { } action)
                continue;

            yield return new ActionPoint(current.StartTime, action, true);
            yield return new ActionPoint(calculateReleaseTime(current, GetNextObject(i)), action, false);
        }
    }
}
