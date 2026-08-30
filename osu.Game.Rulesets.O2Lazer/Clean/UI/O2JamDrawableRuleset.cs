using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Extensions.IEnumerableExtensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Input;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Configuration;
using osu.Game.Input.Handlers;
using osu.Game.Replays;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Configuration;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mania.Replays;
using osu.Game.Rulesets.Mania.Skinning;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.UI.Scrolling;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.UI;

public partial class O2JamDrawableRuleset : DrawableScrollingRuleset<ManiaHitObject>
{
    public const double MaximumTimeRange = 11485;

    private static readonly double[] o2jam_speed_multipliers = [0.5, 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8];

    public new ManiaPlayfield Playfield => (ManiaPlayfield)base.Playfield;

    public new O2JamBeatmap Beatmap => (O2JamBeatmap)base.Beatmap;

    protected new O2JamRulesetConfigManager Config => (O2JamRulesetConfigManager)base.Config;

    public IEnumerable<BarLine> BarLines { get; }

    protected override bool RelativeScaleBeatLengths => true;

    private readonly Bindable<ManiaScrollingDirection> configDirection = new();
    private readonly BindableDouble configScrollSpeed = new();
    private readonly BindableBool configConstantSpeed = new();
    private readonly Track speedAdjustmentTrack = new TrackVirtual(0);

    private ISkinSource currentSkin = null!;
    private O2JamPreviewTrack? gameplayTrack;
    private ScheduledDelegate? pendingSkinChange;
    private float hitPosition;

    public double TargetTimeRange { get; private set; }

    public O2JamDrawableRuleset(Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
        : base(ruleset, beatmap, mods)
    {
        BarLines = Beatmap.MeasureLineTimes.Count > 0
            ? Beatmap.MeasureLineTimes.Select((time, index) => new BarLine
            {
                StartTime = time,
                Major = index % 4 == 0,
            }).ToArray()
            : new BarLineGenerator<BarLine>(Beatmap).BarLines;
        TimeRange.MinValue = 1;
        TimeRange.MaxValue = MaximumTimeRange;
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource source)
    {
        gameplayTrack = O2JamPreviewCoordinator.EnterGameplay();

        currentSkin = source;
        currentSkin.SourceChanged += onSkinChange;
        updateSkinPosition();

        foreach (var mod in Mods.OfType<IApplicableToTrack>())
            mod.ApplyToTrack(speedAdjustmentTrack);

        foreach (var point in ControlPoints)
        {
            point.Velocity = 1;
            point.BaseBeatLength *= Beatmap.Difficulty.SliderMultiplier;
            point.EffectPoint = new EffectControlPoint();
        }

        BarLines.ForEach(Playfield.Add);

        Config.BindWith(O2JamRulesetSetting.ScrollDirection, configDirection);
        configDirection.BindValueChanged(direction => Direction.Value = (ScrollingDirection)direction.NewValue, true);

        Config.BindWith(O2JamRulesetSetting.ScrollSpeed, configScrollSpeed);
        configScrollSpeed.BindValueChanged(speed =>
        {
            if (AllowScrollSpeedAdjustment)
                TargetTimeRange = ComputeScrollTime(speed.NewValue);
        });

        Config.BindWith(O2JamRulesetSetting.ConstantScrollSpeed, configConstantSpeed);
        configConstantSpeed.BindValueChanged(constant =>
            VisualisationMethod = constant.NewValue ? ScrollVisualisationMethod.Constant : ScrollVisualisationMethod.Sequential, true);

        TimeRange.Value = TargetTimeRange = ComputeScrollTime(configScrollSpeed.Value);
    }

    public static double ComputeScrollTime(double scrollSpeed) => MaximumTimeRange / scrollSpeed;

    public static double GetO2JamSpeedMultiplier(double scrollSpeed)
    {
        var multiplier = scrollSpeed / O2JamRulesetConfigManager.DefaultScrollSpeed;
        return o2jam_speed_multipliers.MinBy(candidate => System.Math.Abs(candidate - multiplier));
    }

    protected override void AdjustScrollSpeed(int amount) => configScrollSpeed.Value += amount;

    protected override void Update()
    {
        base.Update();

        const float distance_to_default_hit_position = 768 - LegacyManiaSkinConfiguration.DEFAULT_HIT_POSITION;
        var scale = (768 - hitPosition) / distance_to_default_hit_position;
        TimeRange.Value = TargetTimeRange
                          * speedAdjustmentTrack.AggregateTempo.Value
                          * speedAdjustmentTrack.AggregateFrequency.Value
                          * scale;
    }

    public override PlayfieldAdjustmentContainer CreatePlayfieldAdjustmentContainer() => new O2JamPlayfieldAdjustmentContainer(this);

    protected override Playfield CreatePlayfield() => new O2JamManiaPlayfield(Beatmap.Stages);

    public override int Variant => O2LazerIdentity.O2Jam7KVariant;

    protected override PassThroughInputManager CreateInputManager() => new ManiaInputManager(Ruleset.RulesetInfo, Variant);

    public override DrawableHitObject<ManiaHitObject>? CreateDrawableRepresentation(ManiaHitObject hitObject) => null;

    protected override ReplayInputHandler CreateReplayInputHandler(Replay replay) => new O2JamFramedReplayInputHandler(replay);

    protected override ReplayRecorder CreateReplayRecorder(Score score) => new O2JamReplayRecorder(score);

    protected override ResumeOverlay CreateResumeOverlay() => new DelayedResumeOverlay();

    private void onSkinChange()
    {
        pendingSkinChange?.Cancel();
        pendingSkinChange = Scheduler.Add(updateSkinPosition);
    }

    private void updateSkinPosition()
    {
        hitPosition = currentSkin.GetConfig<ManiaSkinConfigurationLookup, float>(
                          new ManiaSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HitPosition))?.Value
                      ?? Stage.HIT_TARGET_POSITION;
        pendingSkinChange = null;
    }

    protected override void Dispose(bool isDisposing)
    {
        disposeSyncDiagnostics();
        O2JamPreviewCoordinator.ExitGameplay(gameplayTrack);
        base.Dispose(isDisposing);

        if (currentSkin.IsNotNull())
            currentSkin.SourceChanged -= onSkinChange;
    }

    partial void disposeSyncDiagnostics();
}
