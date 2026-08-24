using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Media.Audio.Playback;
using osu.Game.Rulesets.O2Lazer.Media.Audio.Preview;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.IO.ResourceStore;
using osu.Game.Rulesets.O2Lazer.O2Jam;
using osu.Game.Skinning;
using osu.Game.Storyboards;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps;

/// <summary>
///     A <see cref="WorkingBeatmap" /> that wraps a normally-created working beatmap but
///     overrides resource handling required by O2LAZER charts, including preview playback and
///     disabling osu! beatmap skins. Other members are delegated to the inner working beatmap.
/// </summary>
public class O2LazerWorkingBeatmap(WorkingBeatmap inner, AudioManager audioManager, TextureStore? externalTextureStore = null)
    : WorkingBeatmap(createWrapperBeatmapInfo(inner), audioManager)
{

    internal static O2LazerPreviewTrack? ActivePreviewTrack { get; private set; }

    private readonly AudioManager audioManager = audioManager;

    private readonly object externalBackgroundResolutionLock = new();
    private volatile bool externalBackgroundResolved;
    private List<string> resolvedBackgroundPaths = [];
    private List<string> resolvedPanelBackgroundPaths = [];

    public override bool TryTransferTrack(WorkingBeatmap target)
    {
        if (!TrackLoaded || target is not O2LazerWorkingBeatmap || BeatmapInfo.ID != target.BeatmapInfo.ID)
            return false;

        return base.TryTransferTrack(target);
    }

    public override Texture GetBackground() => getExternalBackground(false) ?? inner.GetBackground();

    public override Texture GetPanelBackground() => getExternalBackground(true) ?? inner.GetPanelBackground();

    public override Stream GetStream(string storagePath) => inner.GetStream(storagePath);

    /// <summary>
    ///     Switches the currently-active preview track (if any) to clock-only mode.  Gameplay
    ///     seeks and starts this track as a clock source, but audible O2LAZER playback should come from the
    ///     <see cref="O2LazerBackgroundAudioPlayer" />.
    /// </summary>
    internal static void SwitchActivePreviewToGameplayClockOnly()
    {
        var track = ActivePreviewTrack;

        track?.PlaybackMode = O2LazerPreviewTrackPlaybackMode.GameplayClockOnly;
    }

    internal static void RestoreActivePreview(double? gameplayTime)
    {
        var track = ActivePreviewTrack;

        if (track == null || track.IsDisposed)
            return;

        track.RestorePreview(gameplayTime);
    }

    protected override Track GetBeatmapTrack()
    {
        var beatmap = Beatmap;

        if (beatmap is IO2LazerBeatmap o2lazerBeatmap)
        {
            var track = createPreviewTrack(beatmap, o2lazerBeatmap);

            // Stop and remove the previous preview track before registering the new one.
            // Disposing via the audio update loop also releases the per-chart SampleStore.
            if (ActivePreviewTrack != null)
            {
                ActivePreviewTrack.Stop();
                ActivePreviewTrack.Dispose();
            }

            audioManager.AddItem(track);
            ActivePreviewTrack = track;

            return track;
        }

        return null!; // fall back to TrackVirtual by WorkingBeatmap.LoadTrack
    }

    internal static IReadOnlyList<O2LazerPreviewSampleEvent> CreatePreviewEvents(
        IBeatmap beatmap,
        IO2LazerBeatmap o2lazerBeatmap,
        CancellationToken cancellationToken = default)
    {
        var allEvents = new List<O2LazerPreviewSampleEvent>(o2lazerBeatmap.BackgroundSampleEvents.Count);

        foreach (var evt in o2lazerBeatmap.BackgroundSampleEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            allEvents.Add(new O2LazerPreviewSampleEvent(evt, true));
        }

        if (O2LazerRulesetRuntime.PreviewPlayKeysounds)
        {
            foreach (var obj in beatmap.HitObjects)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (obj is not O2LazerHitObject hit)
                    continue;

                if (hit.SampleKey is { } sampleKey)
                    allEvents.Add(new O2LazerPreviewSampleEvent(new O2LazerSampleEvent(hit.StartTime, 0, sampleKey, hit.SampleVolume), false));
            }
        }

        allEvents.Sort(static (a, b) => a.Event.Time.CompareTo(b.Event.Time));
        return allEvents;
    }

    private O2LazerEventPreviewTrack createPreviewTrack(IBeatmap beatmap, IO2LazerBeatmap o2lazerBeatmap)
    {
        return new O2LazerEventPreviewTrack(
            cancellationToken => O2LazerEventPreviewTimeline.Create(
                token => CreatePreviewEvents(beatmap, o2lazerBeatmap, token),
                o2lazerBeatmap.SampleDefinitions,
                cancellationToken),
            Metadata.Source,
            audioManager);
    }

    protected override IBeatmap GetBeatmap() => tryDecodeExternalBeatmap(BeatmapInfo) ?? inner.Beatmap;

    // O2LAZER charts do not contain osu! beatmap skins. Keeping this source out of the skin chain also
    // prevents an empty LegacyBeatmapSkin from overriding the selected user skin during gameplay.
    protected override ISkin GetSkin() => null!;

    protected override Storyboard GetStoryboard() => inner.Storyboard;

    protected override Waveform GetWaveform() => inner.Waveform;

    private static BeatmapInfo createWrapperBeatmapInfo(WorkingBeatmap inner)
    {
        var beatmapInfo = inner.BeatmapInfo;

        // Embedded covers and persisted external markers need no mutation, so reusing the
        // original BeatmapInfo avoids cloning the full BeatmapSet on every panel request.
        if (TryReadExternalBackgroundMarker(beatmapInfo, out _, out _) || hasEmbeddedBackground(beatmapInfo))
            return beatmapInfo;

        var mutableClone = cloneBeatmapInfo(beatmapInfo);
        var o2lazerBeatmap = tryDecodeExternalBeatmap(mutableClone) as IO2LazerBeatmap ?? inner.Beatmap as IO2LazerBeatmap;
        var backgroundPath = resolveExternalBackgroundPaths(mutableClone.Metadata.Source, o2lazerBeatmap, false).FirstOrDefault();
        var panelBackgroundPath = resolveExternalBackgroundPaths(mutableClone.Metadata.Source, o2lazerBeatmap, true).FirstOrDefault();
        applyExternalBackgroundMarker(mutableClone, backgroundPath, panelBackgroundPath);

        return mutableClone;
    }

    private static BeatmapInfo cloneBeatmapInfo(BeatmapInfo source)
    {
        var clone = source.Clone();
        clone.Metadata = source.Metadata.DeepClone();
        clone.BeatmapSet = cloneBeatmapSetInfo(clone.BeatmapSet, clone);
        return clone;
    }

    private static BeatmapSetInfo? cloneBeatmapSetInfo(BeatmapSetInfo? source, BeatmapInfo owner)
    {
        if (source == null)
            return null;

        var clone = new BeatmapSetInfo
        {
            ID = source.ID,
            OnlineID = source.OnlineID,
            DateAdded = source.DateAdded,
            DateSubmitted = source.DateSubmitted,
            DateRanked = source.DateRanked,
            Status = source.Status,
            DeletePending = source.DeletePending,
            Hash = source.Hash,
            Protected = source.Protected,
        };

        foreach (var file in source.Files)
            clone.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = file.File.Hash }, file.Filename));

        clone.Beatmaps.Add(owner);

        return clone;
    }

    private static void applyExternalBackgroundMarker(BeatmapInfo beatmapInfo, string? backgroundPath, string? panelBackgroundPath)
    {
        var markerPath = panelBackgroundPath ?? backgroundPath;

        if (markerPath == null)
            return;

        // Song-select panels check this metadata before loading textures, so it must be ready
        // as soon as the wrapper is constructed rather than on first GetBackground().
        beatmapInfo.Metadata.BackgroundFile = markerPath;

        if (beatmapInfo.BeatmapSet?.GetFile(markerPath) == null)
            beatmapInfo.BeatmapSet?.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = $"{backgroundPath}|{panelBackgroundPath}" }, markerPath));
    }

    private static IBeatmap? tryDecodeExternalBeatmap(BeatmapInfo beatmapInfo)
    {
        var chartPath = tryResolveExternalChartPath(beatmapInfo);

        if (chartPath == null)
            return null;

        try
        {
            // DecodeBeatmap already loads and caches the OJN by path; reading the bytes here
            // again would copy every selected chart's file on the song-select draw thread.
            return OjnDecoder.DecodeBeatmap([], cloneBeatmapInfo(beatmapInfo), chartPath);
        }
        catch (Exception e)
        {
            O2LazerLogger.Error(e, $"O2Jam external beatmap decode failed for {chartPath}");
            return null;
        }
    }

    internal static bool IsExternalChartAvailable(BeatmapInfo beatmapInfo) =>
        tryResolveExternalChartPath(beatmapInfo) != null;

    private static string? tryResolveExternalChartPath(BeatmapInfo beatmapInfo)
    {
        if (string.IsNullOrWhiteSpace(beatmapInfo.Metadata.Source) || !Directory.Exists(beatmapInfo.Metadata.Source) || string.IsNullOrWhiteSpace(beatmapInfo.Path))
            return null;

        var baseFullPath = Path.GetFullPath(beatmapInfo.Metadata.Source);

        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(baseFullPath, beatmapInfo.Path));
            return O2LazerFileResourceStore.IsPathInsideDirectory(fullPath, baseFullPath) && File.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static List<string> resolveExternalBackgroundPaths(string? sourceDirectory, IO2LazerBeatmap? o2lazerBeatmap, bool preferBanner)
    {
        var paths = new List<string>();

        // Source is only a directory for external-audio charts; bail before touching
        // inner.Beatmap so normal imports don't pay for a decode just to resolve backgrounds.
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
            return paths;

        if (o2lazerBeatmap == null)
            return paths;

        var baseFullPath = Path.GetFullPath(sourceDirectory);

        foreach (var candidate in getBackgroundCandidates(o2lazerBeatmap, preferBanner))
        {
            string fullPath;

            try
            {
                fullPath = Path.GetFullPath(Path.Combine(baseFullPath, candidate));
            }
            catch (Exception)
            {
                // #STAGEFILE / #BACKBMP / #BANNER values are chart-controlled and only
                // quote-trimmed by the parser; illegal path characters would otherwise throw
                // and crash background loading for the whole chart.
                continue;
            }

            if (O2LazerFileResourceStore.IsPathInsideDirectory(fullPath, baseFullPath) && File.Exists(fullPath))
                paths.Add(fullPath);
        }

        return paths;
    }

    internal static bool TryReadExternalBackgroundMarker(BeatmapInfo beatmapInfo, out string? backgroundPath, out string? panelBackgroundPath)
    {
        backgroundPath = null;
        panelBackgroundPath = null;

        var markerPath = beatmapInfo.Metadata.BackgroundFile;
        if (string.IsNullOrEmpty(markerPath) || !Path.IsPathRooted(markerPath))
            return false;

        var combined = beatmapInfo.BeatmapSet?.GetFile(markerPath)?.File.Hash;
        if (string.IsNullOrEmpty(combined))
            return false;

        var separator = combined.IndexOf('|');
        backgroundPath = separator >= 0 ? combined[..separator] : combined;
        panelBackgroundPath = separator >= 0 ? combined[(separator + 1)..] : combined;

        if (string.IsNullOrEmpty(backgroundPath))
            backgroundPath = panelBackgroundPath;
        if (string.IsNullOrEmpty(panelBackgroundPath))
            panelBackgroundPath = backgroundPath;

        return !string.IsNullOrEmpty(backgroundPath);
    }

    private static bool hasEmbeddedBackground(BeatmapInfo beatmapInfo)
    {
        var backgroundFile = beatmapInfo.Metadata.BackgroundFile;
        return !string.IsNullOrEmpty(backgroundFile)
               && !Path.IsPathRooted(backgroundFile)
               && beatmapInfo.BeatmapSet?.GetFile(backgroundFile) != null;
    }

    private static IEnumerable<string> getBackgroundCandidates(IO2LazerBeatmap o2lazerBeatmap, bool preferBanner)
    {
        if (preferBanner && !string.IsNullOrWhiteSpace(o2lazerBeatmap.Banner))
            yield return o2lazerBeatmap.Banner;

        foreach (var candidate in o2lazerBeatmap.GetSongSelectBackgroundCandidates())
        {
            if (preferBanner && string.Equals(candidate, o2lazerBeatmap.Banner, StringComparison.OrdinalIgnoreCase))
                continue;

            yield return candidate;
        }
    }

    private Texture? getExternalBackground(bool preferBanner)
    {
        ensureExternalBackgroundResolved();

        if (externalTextureStore == null)
            return null;

        var paths = preferBanner ? resolvedPanelBackgroundPaths : resolvedBackgroundPaths;

        // Try each candidate in priority order so a corrupt or unreadable primary asset
        // (e.g. a broken #STAGEFILE) still falls back to #BACKBMP / #BANNER instead of
        // caching a permanent miss for the wrapper's lifetime.
        foreach (var path in paths)
        {
            var texture = externalTextureStore.Get(path);

            if (texture != null)
                return texture;
        }

        return null;
    }

    private void ensureExternalBackgroundResolved()
    {
        if (externalBackgroundResolved)
            return;

        lock (externalBackgroundResolutionLock)
        {
            if (externalBackgroundResolved)
                return;

            string? backgroundPath;
            string? panelBackgroundPath;

            if (TryReadExternalBackgroundMarker(BeatmapInfo, out backgroundPath, out panelBackgroundPath))
            {
                resolvedBackgroundPaths = [backgroundPath!];
                resolvedPanelBackgroundPaths = [panelBackgroundPath!];
            }
            else if (hasEmbeddedBackground(BeatmapInfo))
            {
                // Embedded covers are served by the inner osu! WorkingBeatmap's panel store;
                // decoding the OJN here would only be needed to discover external assets.
                resolvedBackgroundPaths = [];
                resolvedPanelBackgroundPaths = [];
            }
            else
            {
                var o2lazerBeatmap = tryDecodeExternalBeatmap(BeatmapInfo) as IO2LazerBeatmap ?? inner.Beatmap as IO2LazerBeatmap;
                resolvedBackgroundPaths = resolveExternalBackgroundPaths(Metadata.Source, o2lazerBeatmap, false);
                resolvedPanelBackgroundPaths = resolveExternalBackgroundPaths(Metadata.Source, o2lazerBeatmap, true);
            }

            var selectedBackgroundPath = resolvedBackgroundPaths.FirstOrDefault();
            var selectedPanelBackgroundPath = resolvedPanelBackgroundPaths.FirstOrDefault();
            applyExternalBackgroundMarker(BeatmapInfo, selectedBackgroundPath, selectedPanelBackgroundPath);

            // Publish completion only after both lists and their metadata marker are ready for readers.
            externalBackgroundResolved = true;
        }
    }
}


