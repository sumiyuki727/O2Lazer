using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using O2Jam.Core;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;
using osu.Game.Skinning;
using osu.Game.Storyboards;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps;

/// <summary>
/// Adapts external OJN/OJM files to osu!'s WorkingBeatmap boundary without leaking file-format concerns into gameplay.
/// </summary>
public sealed class O2JamWorkingBeatmap : WorkingBeatmap
{
    private readonly WorkingBeatmap inner;
    private readonly AudioManager audioManager;
    private readonly string chartPath;
    private readonly O2JamDifficulty difficulty;
    private readonly O2JamExternalChartResources resources;
    private O2JamBeatmapSkin? sampleSkin;

    public O2JamWorkingBeatmap(WorkingBeatmap inner, AudioManager audioManager, string chartPath)
        : base(inner.BeatmapInfo, audioManager)
    {
        this.inner = inner;
        this.audioManager = audioManager;
        this.chartPath = chartPath;
        difficulty = resolveDifficulty(BeatmapInfo.DifficultyName);
        resources = new O2JamExternalChartResources(chartPath, difficulty);
    }

    internal bool CanReuse => resources.CanReuse;

    public override bool TryTransferTrack(WorkingBeatmap target)
    {
        var transferLabel = $"{Path.GetFileName(chartPath)}: {BeatmapInfo.DifficultyName} -> {target.BeatmapInfo.DifficultyName}";

        if (target is not O2JamWorkingBeatmap o2Target)
        {
            Logger.Log($"O2Lazer preview transfer rejected ({transferLabel}): target wrapper is {target.GetType().FullName}.", level: LogLevel.Verbose);
            return false;
        }

        if (!string.Equals(chartPath, o2Target.chartPath, StringComparison.OrdinalIgnoreCase))
        {
            Logger.Log($"O2Lazer preview transfer rejected ({transferLabel}): source chart paths differ.", level: LogLevel.Verbose);
            return false;
        }

        // A changed OJM must start a fresh track even if its authored BGM sample IDs match.
        if (!CanReuse || !o2Target.CanReuse)
        {
            Logger.Log($"O2Lazer preview transfer rejected ({transferLabel}): OJM resource changed or failed to load.", level: LogLevel.Verbose);
            return false;
        }

        if (Track is not O2JamPreviewTrack previewTrack)
        {
            Logger.Log($"O2Lazer preview transfer rejected ({transferLabel}): source track is {Track.GetType().FullName}.", level: LogLevel.Verbose);
            return false;
        }

        if (o2Target.Beatmap is not O2JamBeatmap targetBeatmap)
        {
            Logger.Log($"O2Lazer preview transfer rejected ({transferLabel}): target OJN failed to decode.", level: LogLevel.Verbose);
            return false;
        }

        if (!previewTrack.CanTransferSchedule(targetBeatmap))
        {
            Logger.Log(
                $"O2Lazer preview transfer rejected ({transferLabel}): BGM identity {previewTrack.DescribeBackgroundIdentity()} -> {previewTrack.DescribeBackgroundIdentity(targetBeatmap)}.",
                level: LogLevel.Verbose);
            return false;
        }

        if (!BeatmapInfo.AudioEquals(o2Target.BeatmapInfo))
        {
            Logger.Log($"O2Lazer preview transfer rejected ({transferLabel}): osu! AudioEquals is false.", level: LogLevel.Verbose);
            return false;
        }

        if (!base.TryTransferTrack(target))
        {
            Logger.Log($"O2Lazer preview transfer rejected ({transferLabel}): osu! refused the live track.", level: LogLevel.Verbose);
            return false;
        }

        // Matching BGM schedules can retain the clock while difficulty-specific keysounds change.
        // Multi-song OJN sets fail the check above and let MusicController restart the new track.
        previewTrack.ReplaceSchedule(targetBeatmap);

        O2JamPreviewCoordinator.Activate(previewTrack);
        Logger.Log($"O2Lazer preview transfer retained the live track ({transferLabel}).", level: LogLevel.Verbose);
        return true;
    }

    public override Texture GetBackground() => inner.GetBackground();

    public override Texture GetPanelBackground() => inner.GetPanelBackground();

    public override Stream GetStream(string storagePath) => inner.GetStream(storagePath);

    protected override IBeatmap GetBeatmap()
    {
        var started = Stopwatch.GetTimestamp();
        // A selected chart must not queue behind speculative panel preloads.
        _ = resources.Archive;
        var source = resources.Document;
        var beatmap = new OjnBeatmapFactory().Create(source, difficulty);
        beatmap.BeatmapInfo = BeatmapInfo.Clone();
        Logger.Log(
            $"O2Lazer prepared OJN {Path.GetFileName(chartPath)} {difficulty} in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:N1} ms.",
            level: LogLevel.Verbose);
        return beatmap;
    }

    protected override Track GetBeatmapTrack()
    {
        if (Beatmap is not O2JamBeatmap beatmap || Skin is not O2JamBeatmapSkin sampleSkin)
            return null!;

        var track = new O2JamPreviewTrack(beatmap, sampleSkin, audioManager);
        O2JamPreviewCoordinator.Activate(track);
        return track;
    }

    protected override ISkin GetSkin()
    {
        var skin = sampleSkin = new O2JamBeatmapSkin(resources.Archive, audioManager);

        if (Beatmap is O2JamBeatmap beatmap)
        {
            const double startup_prefetch_duration = 10_000;
            var schedule = O2JamPreviewSchedule.Create(beatmap, true);
            skin.PrefetchPreview(schedule, startup_prefetch_duration);
        }

        return skin;
    }

    internal void DisposeCachedResources() => sampleSkin?.Dispose();

    protected override Storyboard GetStoryboard() => new()
    {
        BeatmapInfo = BeatmapInfo,
        Beatmap = Beatmap,
    };

    // The metadata audio identity points at the managed OJN file, which is intentionally not an
    // audio stream. OJM layers have no single waveform that osu!'s editor could represent safely.
    protected override Waveform GetWaveform() => new(null);

    private static O2JamDifficulty resolveDifficulty(string name)
    {
        foreach (var difficulty in Enum.GetValues<O2JamDifficulty>())
        {
            if (name.StartsWith(difficulty.ToString(), StringComparison.OrdinalIgnoreCase))
                return difficulty;
        }

        var byLevel = Enum.GetValues<O2JamDifficulty>()
                          .FirstOrDefault(difficulty => name.Contains(difficulty.ToString(), StringComparison.OrdinalIgnoreCase));
        return byLevel;
    }
}
