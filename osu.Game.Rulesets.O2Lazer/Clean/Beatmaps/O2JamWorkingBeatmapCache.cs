using System;
using System.Collections.Generic;
using osu.Framework.Audio;
using osu.Game.Beatmaps;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps;

/// <summary>
/// Keeps recently selected external beatmaps alive across osu!'s weak WorkingBeatmap cache.
/// </summary>
internal sealed class O2JamWorkingBeatmapCache
{
    // Preloaded native decoders are considerably heavier than the lazy OJM index. Six entries keep
    // two complete three-difficulty songs warm without retaining every chart crossed during a drag.
    private const int max_wrappers = 6;

    private readonly AudioManager audioManager;
    private readonly Dictionary<Guid, CacheEntry> entries = [];
    private readonly LinkedList<Guid> accessOrder = [];

    internal O2JamWorkingBeatmapCache(WorkingBeatmapCache inner, AudioManager audioManager)
    {
        this.audioManager = audioManager;
        inner.OnInvalidated += onInvalidated;
    }

    internal O2JamWorkingBeatmap Wrap(WorkingBeatmap working, string chartPath)
    {
        // Empty IDs are common in tests and temporary beatmaps but cannot identify a cache entry.
        if (working.BeatmapInfo.ID == Guid.Empty)
            return new O2JamWorkingBeatmap(working, audioManager, chartPath);

        lock (entries)
        {
            if (entries.TryGetValue(working.BeatmapInfo.ID, out var cached)
                && string.Equals(cached.ChartPath, chartPath, StringComparison.OrdinalIgnoreCase))
            {
                accessOrder.Remove(cached.AccessNode);
                accessOrder.AddLast(cached.AccessNode);
                return cached.Wrapper;
            }

            if (cached != null)
                remove(cached);

            while (entries.Count >= max_wrappers)
            {
                var oldest = accessOrder.First;
                if (oldest == null)
                    break;

                accessOrder.RemoveFirst();
                if (entries.Remove(oldest.Value, out var evicted))
                    evicted.Wrapper.DisposeCachedResources();
            }

            var wrapper = new O2JamWorkingBeatmap(working, audioManager, chartPath);
            var node = accessOrder.AddLast(working.BeatmapInfo.ID);
            entries[working.BeatmapInfo.ID] = new CacheEntry(chartPath, wrapper, node);
            return wrapper;
        }
    }

    private void onInvalidated(WorkingBeatmap working)
    {
        lock (entries)
        {
            if (entries.TryGetValue(working.BeatmapInfo.ID, out var entry))
                remove(entry);
        }
    }

    private void remove(CacheEntry entry)
    {
        accessOrder.Remove(entry.AccessNode);
        entries.Remove(entry.AccessNode.Value);
        entry.Wrapper.DisposeCachedResources();
    }

    private sealed record CacheEntry(
        string ChartPath,
        O2JamWorkingBeatmap Wrapper,
        LinkedListNode<Guid> AccessNode);
}
