using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Media.Audio.Playback;
using osu.Game.Rulesets.O2Lazer.Media.Audio.Preview;
using osu.Game.Rulesets.O2Lazer.Media.Audio.Samples;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.Scoring.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.UI.Gameplay;

internal sealed class O2LazerGameplayAudioController
{
    private O2LazerPreviewTrack? previewTrackBeforePlay;
    private bool stoppedPreviewForGameplay;

    internal O2LazerSamplePlayback SamplePlayback { get; }

    internal BindableBool BackgroundAudioPaused { get; } = new(true);

    internal O2LazerGameplayAudioController(O2LazerBeatmap beatmap, IReadOnlyList<Mod>? mods)
    {
        var playbackRate = GetPlaybackRate(mods);
        SamplePlayback = new O2LazerSamplePlayback(
            beatmap.SampleDefinitions,
            ResolveBeatmapSource(beatmap),
            playbackRate.Rate,
            getSampleUsages(beatmap),
            playbackRate.AdjustPitch);
    }

    internal O2LazerBackgroundAudioPlayer CreateBackgroundAudioPlayer(O2LazerBeatmap beatmap)
    {
        var events = beatmap.BackgroundSampleEvents
            .OrderBy(evt => evt.Time)
            .Where(evt => beatmap.SampleDefinitions.ContainsKey(evt.SampleKey))
            .Select(evt => new O2LazerBackgroundAudioPlayer.BgmEvent(evt.Time, evt.SampleKey, evt.Volume))
            .ToList();

        if (O2LazerRulesetRuntime.AutoPlayKeysounds)
        {
            events.AddRange(GetAutoKeysoundEvents(beatmap));
            events.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        return new O2LazerBackgroundAudioPlayer(events, BackgroundAudioPaused);
    }

    internal static IEnumerable<O2LazerBackgroundAudioPlayer.BgmEvent> GetAutoKeysoundEvents(O2LazerBeatmap beatmap)
    {
        foreach (var hitObject in beatmap.HitObjects)
        {
            if (hitObject.SampleKey is { } sampleKey && beatmap.SampleDefinitions.ContainsKey(sampleKey))
                yield return new O2LazerBackgroundAudioPlayer.BgmEvent(hitObject.StartTime, sampleKey, hitObject.SampleVolume);
        }
    }

    internal void BindPauseSource(IBindable<bool> paused)
    {
        ((IBindable<bool>)BackgroundAudioPaused).BindTo(paused);
    }

    internal void Update()
    {
        if (!BackgroundAudioPaused.Value && !stoppedPreviewForGameplay)
            StopPreviewForGameplay();
    }

    internal void SubmitLivePlayBatch() => SamplePlayback.SubmitLivePlayBatch();

    internal void StopPreviewForGameplay()
    {
        if (stoppedPreviewForGameplay)
            return;

        previewTrackBeforePlay = O2LazerWorkingBeatmap.ActivePreviewTrack;

        if (previewTrackBeforePlay == null)
            return;

        O2LazerWorkingBeatmap.SwitchActivePreviewToGameplayClockOnly();
        stoppedPreviewForGameplay = true;
    }

    internal void Dispose(double? previewRestoreTime)
    {
        BackgroundAudioPaused.UnbindAll();

        if (previewTrackBeforePlay == null || !stoppedPreviewForGameplay)
            return;

        // A newly selected beatmap owns its own preview and must not be replaced by the old session.
        if (O2LazerWorkingBeatmap.ActivePreviewTrack == previewTrackBeforePlay)
            O2LazerWorkingBeatmap.RestoreActivePreview(previewRestoreTime);

        previewTrackBeforePlay = null;
    }

    internal static string ResolveBeatmapSource(O2LazerBeatmap beatmap) =>
        beatmap.BeatmapInfo.BeatmapSet?.Beatmaps.FirstOrDefault(candidate => candidate.ID == beatmap.BeatmapInfo.ID)
            ?.Metadata.Source ?? beatmap.BeatmapInfo.Metadata.Source;

    internal static (double Rate, bool AdjustPitch) GetPlaybackRate(IReadOnlyList<Mod>? mods)
    {
        var rateMods = mods?.OfType<ModRateAdjust>().ToArray();
        var rateMod = rateMods?.FirstOrDefault();
        if (rateMod == null)
            return (1, false);

        var adjustPitch = rateMods!.Any(mod => mod is O2LazerModDaycore or O2LazerModNightcore)
                          || rateMods!.Any(mod => mod is O2LazerModDoubleTime { AdjustPitch.Value: true } or O2LazerModHalfTime { AdjustPitch.Value: true });

        return (rateMod.SpeedChange.Value, adjustPitch);
    }

    private static IEnumerable<O2LazerSampleUsage> getSampleUsages(O2LazerBeatmap beatmap)
    {
        foreach (var evt in beatmap.BackgroundSampleEvents)
            yield return new O2LazerSampleUsage(evt.SampleKey, evt.Time, ResumeAfterSeek: true);

        foreach (var column in beatmap.HitObjects.GroupBy(hitObject => hitObject.Column))
        {
            O2LazerHitObject? previousHitObject = null;

            foreach (var hitObject in column.OrderBy(hitObject => hitObject.StartTime))
            {
                if (hitObject.SampleKey is { } sampleKey)
                {
                    // A column exposes its next note's sample as soon as the preceding note can be judged.
                    var earliestTriggerTime = previousHitObject == null
                        ? 0
                        : Math.Max(0, previousHitObject.StartTime - getFastJudgementWindow(previousHitObject));
                    var latestTriggerTime = hitObject.StartTime + getSlowJudgementWindow(hitObject);

                    yield return new O2LazerSampleUsage(sampleKey, hitObject.StartTime, earliestTriggerTime, latestTriggerTime);
                }

                previousHitObject = hitObject;
            }
        }
    }

    private static double getFastJudgementWindow(O2LazerHitObject hitObject) =>
        O2LazerJudgementProfileProvider.GetTable(
            hitObject.Beatmap.LayoutVariant,
            hitObject.Column,
            hitObject.EffectiveJudgementRate,
            tail: false).FastWindowFor(HitResult.Ok);

    private static double getSlowJudgementWindow(O2LazerHitObject hitObject) =>
        O2LazerJudgementProfileProvider.GetTable(
            hitObject.Beatmap.LayoutVariant,
            hitObject.Column,
            hitObject.EffectiveJudgementRate,
            tail: false).SlowWindowFor(HitResult.Ok);
}



