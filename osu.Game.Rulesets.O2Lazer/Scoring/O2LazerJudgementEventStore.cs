using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

public static class O2LazerJudgementEventStore
{
    private static readonly ConditionalWeakTable<ScoreInfo, Holder> events = new();

    public static void Set(ScoreInfo score, IEnumerable<O2LazerJudgementEvent> judgementEvents)
    {
        var copy = judgementEvents.ToArray();
        set(score, copy);
    }

    internal static void SetView(ScoreInfo score, IReadOnlyList<O2LazerJudgementEvent> judgementEvents)
        => set(score, judgementEvents);

    private static void set(ScoreInfo score, IReadOnlyList<O2LazerJudgementEvent> judgementEvents)
    {
        lock (events)
        {
            if (events.TryGetValue(score, out var existing) && ReferenceEquals(existing.Events, judgementEvents))
                return;

            events.Remove(score);
            events.Add(score, new Holder(judgementEvents));
        }
    }

    public static bool TryGet(ScoreInfo score, out IReadOnlyList<O2LazerJudgementEvent> judgementEvents)
    {
        lock (events)
        {
            if (events.TryGetValue(score, out var holder))
            {
                judgementEvents = holder.Events;
                return true;
            }
        }

        judgementEvents = [];
        return false;
    }

    public static void Clear(ScoreInfo score)
    {
        lock (events)
            events.Remove(score);
    }

    private sealed class Holder(IReadOnlyList<O2LazerJudgementEvent> judgementEvents)
    {
        public IReadOnlyList<O2LazerJudgementEvent> Events { get; } = judgementEvents;
    }
}
