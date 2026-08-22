using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Input;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Input.Handlers;
using osu.Game.Replays;
using osu.Game.Rulesets.O2Lazer.Media.Audio.Samples;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.IO.Input;
using osu.Game.Rulesets.O2Lazer.UI.Gameplay;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Rulesets.O2Lazer.UI.HudComponents;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.O2Lazer.UI;

public partial class O2LazerDrawableRuleset : DrawableRuleset<O2LazerHitObject>
{
    public O2LazerDrawableRuleset(Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
        : base(ruleset, beatmap, mods)
    {
        var o2lazerBeatmap = (O2LazerBeatmap)beatmap;
        audioController = new O2LazerGameplayAudioController(o2lazerBeatmap, Mods);
        samplePlayback = audioController.SamplePlayback;
    }

    internal O2LazerStageHudController StageHudController => field ??= new O2LazerStageHudController((O2LazerPlayfield)Playfield);

    internal O2LazerSamplePlayback SamplePlayback => samplePlayback;

    internal BindableBool BackgroundAudioPaused => audioController.BackgroundAudioPaused;

    public new PassThroughInputManager KeyBindingInputManager => base.KeyBindingInputManager;

    public override int Variant => (int)((O2LazerBeatmap)Beatmap).LayoutVariant;

    public string BeatmapSourceDirectory => O2LazerGameplayAudioController.ResolveBeatmapSource((O2LazerBeatmap)Beatmap);

    // HUD components live in Player.HUDOverlay (a sibling of this DrawableRuleset under Player),
    // so they cannot resolve the local [Cached] above. They reach this instance via the
    // DrawableRuleset the Player caches for them — same cast pattern as O2LazerJudgementDisplay.
    public IO2LazerGameplayEvents GameplayEvents => gameplayEvents;

    [Cached(typeof(IO2LazerGameplayEvents))]
    private readonly O2LazerGameplayEvents gameplayEvents = new();

    private readonly O2LazerGameplayAudioController audioController;
    private O2LazerGameplayCompletionController? completionController;
    private O2LazerGameplaySettingsController? settingsController;

    [Cached]
    private readonly O2LazerSamplePlayback samplePlayback;

    // Resolved from Player's DI cache — available after Player.LoadComplete registers them.
    [Resolved(CanBeNull = true)]
    private HealthProcessor? healthProcessor { get; set; }

    [Resolved(CanBeNull = true)]
    private ScoreProcessor? scoreProcessor { get; set; }

    [Resolved(CanBeNull = true)]
    private GameplayState? gameplayState { get; set; }

    [Resolved(CanBeNull = true)]
    private GameplayClockContainer? gameplayClockContainer { get; set; }

    [Resolved]
    private GameHost host { get; set; } = null!;

    [Resolved]
    private FrameworkConfigManager frameworkConfig { get; set; } = null!;

    protected override void Dispose(bool isDisposing)
    {
        var previewRestoreTime = isDisposing ? gameplayClockContainer?.CurrentTime : null;

        base.Dispose(isDisposing);

        if (!isDisposing)
            return;

        completionController?.Dispose();
        completionController = null;
        settingsController?.Dispose();
        settingsController = null;
        audioController.Dispose(previewRestoreTime);
    }

    public static double ComputeScrollTime(double scrollSpeed) => O2LazerGameplayScrollController.ComputeScrollTime(scrollSpeed);

    public override DrawableHitObject<O2LazerHitObject>? CreateDrawableRepresentation(O2LazerHitObject h) => null;

    public override void SetReplayScore(Score replayScore)
    {
        base.SetReplayScore(replayScore);

    }

    protected override Playfield CreatePlayfield() => new O2LazerPlayfield((O2LazerBeatmap)Beatmap);

    protected override ResumeOverlay CreateResumeOverlay() => new DelayedResumeOverlay();

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (gameplayClockContainer != null)
            audioController.BindPauseSource(gameplayClockContainer.IsPaused);
        else
            audioController.BindPauseSource(IsPaused);

        if (scoreProcessor != null && healthProcessor != null && gameplayState != null)
            completionController = new O2LazerGameplayCompletionController(
                scoreProcessor,
                healthProcessor,
                gameplayState,
                ReplayScore,
                Config as O2LazerRulesetConfigManager);
    }

    protected override void Update()
    {
        base.Update();

        audioController.Update();
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();

        // Columns receive input independently while the playfield updates. Submitting here preserves
        // one mixer target for every keysound produced by the same ruleset update.
        audioController.SubmitLivePlayBatch();
    }

    protected override PassThroughInputManager CreateInputManager() => new O2LazerInputManager(Ruleset.RulesetInfo, Variant);

    protected override ReplayInputHandler CreateReplayInputHandler(Replay replay) => new O2LazerFramedReplayInputHandler(replay);

    protected override ReplayRecorder CreateReplayRecorder(Score score)
    {
        return new O2LazerReplayRecorder(score);
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var beatmap = (O2LazerBeatmap)Beatmap;

        Overlays.Add(StageHudController);

        // Sample playback follows the gameplay clock to load PCM samples before their first use.
        FrameStableComponents.Add(samplePlayback);

        // This component also coordinates pause/seek blocking for KeySounds in the shared Track
        // playback component, so it must exist even when the chart has no background sample events.
        FrameStableComponents.Add(audioController.CreateBackgroundAudioPlayer(beatmap));
        settingsController = new O2LazerGameplaySettingsController(
            Config as O2LazerRulesetConfigManager,
            (O2LazerPlayfield)Playfield,
            O2LazerGameplayAudioController.GetPlaybackRate(Mods).Rate,
            host,
            frameworkConfig);
    }
}

