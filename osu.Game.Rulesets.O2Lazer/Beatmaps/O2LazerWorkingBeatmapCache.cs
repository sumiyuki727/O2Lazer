using System;
using System.Collections.Generic;
using System.IO;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Rulesets.O2Lazer.IO.ResourceStore;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps;

internal sealed class O2LazerWorkingBeatmapCache
{
    private const int max_wrapper_cache = 96;

    private readonly IStorageResourceProvider resources;

    // Keep recently used wrappers alive so fast song-select dragging doesn't repeatedly
    // rebuild the wrapper, re-decode the OJN, and re-upload external backgrounds.
    private readonly Dictionary<Guid, O2LazerWorkingBeatmap> o2lazerWrapperCache = new();

    // Source directories are a small bounded set (SongA..SongZ), so strong caching avoids
    // re-uploading large cover textures every time a chart is selected again.
    private readonly Dictionary<string, LargeTextureStore> externalTextureStores = new();
    private readonly object externalTextureStoreLock = new();

    public O2LazerWorkingBeatmapCache(WorkingBeatmapCache inner)
    {
        resources = inner;
        inner.OnInvalidated += onInvalidated;
    }

    public WorkingBeatmap Wrap(WorkingBeatmap working)
    {
        lock (o2lazerWrapperCache)
        {
            if (o2lazerWrapperCache.TryGetValue(working.BeatmapInfo.ID, out var cached))
                return cached;

            if (o2lazerWrapperCache.Count >= max_wrapper_cache)
                evictOneWrapper();
        }


        // External-audio charts keep their images in Metadata.Source on disk rather than realm
        // storage; give the wrapper a store sandboxed to that directory. Normal imports have no
        // such directory and fall back to inner.GetBackground() (realm).
        TextureStore? externalStore = null;

        if (!string.IsNullOrWhiteSpace(working.Metadata.Source) && Directory.Exists(working.Metadata.Source))
            externalStore = getOrCreateExternalTextureStore(working.Metadata.Source);

        var wrapper = new O2LazerWorkingBeatmap(working, resources.AudioManager!, externalStore);

        lock (o2lazerWrapperCache)
            o2lazerWrapperCache[working.BeatmapInfo.ID] = wrapper;

        return wrapper;
    }

    private void evictOneWrapper()
    {
        foreach (var id in o2lazerWrapperCache.Keys)
        {
            if (o2lazerWrapperCache.Remove(id))
                return;
        }
    }

    private TextureStore getOrCreateExternalTextureStore(string basePath)
    {
        basePath = Path.GetFullPath(basePath);

        lock (externalTextureStoreLock)
        {
            if (externalTextureStores.TryGetValue(basePath, out var cached))
                return cached;

            var store = new LargeTextureStore(
                resources.Renderer,
                resources.CreateTextureLoaderStore(new O2LazerFileResourceStore(basePath)));

            externalTextureStores[basePath] = store;
            return store;
        }
    }

    private void onInvalidated(WorkingBeatmap working)
    {
        lock (o2lazerWrapperCache)
            o2lazerWrapperCache.Remove(working.BeatmapInfo.ID);
    }
}



