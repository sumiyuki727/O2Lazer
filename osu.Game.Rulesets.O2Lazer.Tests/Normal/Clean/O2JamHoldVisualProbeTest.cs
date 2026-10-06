using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using O2Jam.Core;
using osu.Framework.Allocation;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Input.States;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Audio;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Objects.Drawables;
using osu.Game.Rulesets.Mania.Skinning.Legacy;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.O2Lazer.Skinning;
using osu.Game.Rulesets.O2Lazer.UI.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI.Scrolling;
using osu.Game.Rulesets.UI.Scrolling.Algorithms;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
public partial class O2JamHoldVisualProbeTest
{
    [TestCase(407.617)]
    [TestCase(153.513)]
    [TestCase(160.938)]
    [Explicit("Observes the current LN visual/lifetime path in a headless drawable tree without changing gameplay code.")]
    [Category("LocalDiagnostics")]
    public void InspectEarlyReleaseVisual(double earlyRelease)
    {
        using var host = new TestRunHeadlessGameHost($"O2JamHoldVisualProbe-{Guid.NewGuid():N}");
        var game = new ProbeGame(earlyRelease);
        host.Run(game);
        foreach (var line in game.Observations)
            TestContext.Progress.WriteLine(line);
        if (game.Failure != null)
            throw game.Failure;
        Assert.That(game.Completed, Is.True);
    }

    [Test]
    public void TailResolutionUsesResolvedAccuracyForClipping(
        [Values(false, true)] bool o2Visual,
        [Values(ScrollingDirection.Down, ScrollingDirection.Up)] ScrollingDirection direction,
        [Values(50, 200, 407.617, 500)] double earlyRelease)
        => runVisualProbe(earlyRelease, o2Visual, direction);

    [Test]
    public void RejectedHeadDoesNotClip(
        [Values(O2JamAccuracy.Bad, O2JamAccuracy.Miss)] O2JamAccuracy headAccuracy,
        [Values(ScrollingDirection.Down, ScrollingDirection.Up)] ScrollingDirection direction)
        => runVisualProbe(50, true, direction, headAccuracy);

    [Test]
    public void PillRescuedBadHeadStillForcesTailMiss(
        [Values(false, true)] bool o2Visual,
        [Values(ScrollingDirection.Down, ScrollingDirection.Up)] ScrollingDirection direction,
        [Values(false, true)] bool rewind)
        => runVisualProbe(50, o2Visual, direction, O2JamAccuracy.Bad, true, rewind);

    [TestCase(true, false, 407.617)]
    [TestCase(false, true, 500)]
    public void DrawableUsesInjectedRulesetSettingWhenRuntimeProjectionDiffers(bool configuredVisual, bool projectedVisual, double earlyRelease)
    {
        using var config = new O2JamRulesetConfigManager(null, new O2LazerRuleset().RulesetInfo);
        config.SetValue(O2JamRulesetSetting.O2JamStyleDroppedHold, configuredVisual);
        var previousVisual = O2JamRuntimeOptions.UseO2JamLongNoteMissVisual;
        O2JamRuntimeOptions.UseO2JamLongNoteMissVisual = projectedVisual;
        try
        {
            using var host = new TestRunHeadlessGameHost($"O2JamInjectedHoldVisual-{Guid.NewGuid():N}");
            var game = new ProbeGame(earlyRelease, configuredVisual, injectedConfig: config);
            host.Run(game);
            if (game.Failure != null)
                throw game.Failure;
            Assert.That(game.Completed, Is.True);
        }
        finally
        {
            O2JamRuntimeOptions.UseO2JamLongNoteMissVisual = previousVisual;
        }
    }

    [Test]
    public void NoReleaseAutomaticallyResolvesAHeldTail()
    {
        using var host = new TestRunHeadlessGameHost($"O2JamNoRelease-{Guid.NewGuid():N}");
        var game = new ProbeGame(0, noRelease: true);
        host.Run(game);
        if (game.Failure != null)
            throw game.Failure;
        Assert.That(game.Completed, Is.True);
    }

    [TestCase(50)]
    [TestCase(200)]
    [TestCase(407.617)]
    public void RewindBeforeHeadRestoresGameplayAndAllowsReplay(double earlyRelease)
    {
        using var host = new TestRunHeadlessGameHost($"O2JamHoldRewind-{Guid.NewGuid():N}");
        var game = new ProbeGame(earlyRelease, true, rewind: true);
        host.Run(game);
        if (game.Failure != null)
            throw game.Failure;
        Assert.That(game.Completed, Is.True);
    }

    private static void runVisualProbe(double earlyRelease, bool o2Visual, ScrollingDirection direction,
                                       O2JamAccuracy? rejectedHead = null, bool rescueHead = false, bool rewind = false)
    {
        var previousVisual = O2JamRuntimeOptions.UseO2JamLongNoteMissVisual;
        O2JamRuntimeOptions.UseO2JamLongNoteMissVisual = o2Visual;
        try
        {
            using var host = new TestRunHeadlessGameHost($"O2JamHoldClipping-{Guid.NewGuid():N}");
            var game = new ProbeGame(earlyRelease, o2Visual, direction, rejectedHead, rewind: rewind, rescueHead: rescueHead);
            host.Run(game);
            foreach (var line in game.Observations)
                TestContext.Progress.WriteLine(line);
            if (game.Failure != null)
                throw game.Failure;
            Assert.That(game.Completed, Is.True);
        }
        finally
        {
            O2JamRuntimeOptions.UseO2JamLongNoteMissVisual = previousVisual;
        }
    }

    private partial class ProbeGame : Framework.Game
    {
        private readonly ManualClock sourceClock = new();
        private readonly FramedClock frameClock;
        private readonly ProbeScrollingInfo scrolling = new();
        private readonly double releaseTime;
        private readonly bool? verifyO2Visual;
        private readonly O2JamAccuracy expectedAccuracy;
        private readonly O2JamAccuracy? rejectedHead;
        private readonly bool noRelease;
        private readonly bool rescueHead;
        private readonly bool rewind;
        private readonly O2JamRulesetConfigManager? injectedConfig;
        private readonly O2JamScoreProcessor processor = new(new O2LazerRuleset());
        private O2JamGameplaySnapshot initialState;
        private O2JamGameplaySnapshot completedState;
        private int revertedResults;
        private long completedNativeScore;
        private int completedNativeCombo;
        private float releasedHeight;
        private float releasedY;
        private ProbePlayfield playfield = null!;
        private O2JamDrawableHoldNote hold = null!;
        private int phase;
        private int frames;
        public Exception? Failure;
        public bool Completed;
        public List<string> Observations { get; } = [];

        private double headAttemptTime => rejectedHead switch
        {
            O2JamAccuracy.Bad => 1400,
            O2JamAccuracy.Miss => 1500,
            _ => 999.051,
        };

        public ProbeGame(double earlyRelease, bool? verifyO2Visual = null, ScrollingDirection direction = ScrollingDirection.Down,
                         O2JamAccuracy? rejectedHead = null, bool noRelease = false, bool rewind = false,
                         O2JamRulesetConfigManager? injectedConfig = null, bool rescueHead = false)
        {
            releaseTime = 1821.917808219 - earlyRelease;
            this.verifyO2Visual = verifyO2Visual;
            this.rejectedHead = rejectedHead;
            this.noRelease = noRelease;
            this.rescueHead = rescueHead;
            this.rewind = rewind;
            this.injectedConfig = injectedConfig;
            expectedAccuracy = rejectedHead.HasValue ? O2JamAccuracy.Miss : earlyRelease switch
            {
                50 => O2JamAccuracy.Cool,
                200 => O2JamAccuracy.Good,
                407.617 => O2JamAccuracy.Bad,
                _ => O2JamAccuracy.Miss,
            };
            ((Bindable<ScrollingDirection>)scrolling.Direction).Value = direction;
            frameClock = new FramedClock(sourceClock);
            seek(headAttemptTime);
        }

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
            dependencies.CacheAs<IGameplaySettings>(new ProbeSettings());
            dependencies.CacheAs<IScrollingInfo>(scrolling);
            dependencies.CacheAs<IBindable<ManiaAction>>(new Bindable<ManiaAction>(ManiaAction.Key1));
            dependencies.CacheAs<ScoreProcessor>(processor);
            if (injectedConfig != null)
                dependencies.Cache(injectedConfig);
            dependencies.Cache(new osu.Game.Graphics.OsuColour());
            dependencies.Cache(new Column(3, true));
            dependencies.Cache(new StageDefinition(7));
            return dependencies;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            var timing = new O2JamTimingMap(verifyO2Visual.HasValue ? 73 : 146);
            var note = new O2JamHoldNote
            {
                StartTime = 1000,
                Duration = 821.917808219,
                TimingMap = timing,
                HeadChartPosition = timing.PositionAt(1000),
                TailChartPosition = timing.PositionAt(1821.917808219),
                ReleaseTimingDisabled = noRelease,
            };
            note.ApplyDefaults(new osu.Game.Beatmaps.ControlPoints.ControlPointInfo(), new osu.Game.Beatmaps.BeatmapDifficulty());
            playfield = new ProbePlayfield();
            if (rewind)
            {
                var beatmap = new osu.Game.Rulesets.O2Lazer.Beatmaps.O2JamBeatmap(O2JamDifficulty.EX, timing);
                beatmap.HitObjects.Add(note);
                processor.ApplyBeatmap(beatmap);
                playfield.NewResult += (_, result) => processor.ApplyResult(result);
                playfield.RevertResult += result =>
                {
                    revertedResults++;
                    processor.RevertResult(result);
                };
            }
            if (rescueHead)
                for (var i = 0; i < O2JamGameplayState.CoolHitsPerPill; i++)
                {
                    var preceding = new O2JamNote();
                    processor.Resolve(new O2JamJudgementResult(preceding, preceding.CreateJudgement()), O2JamAccuracy.Cool);
                }
            initialState = processor.GameplayState.Current;
            playfield.Add(note);
            ISkin skin = UseLegacySkin ? createLegacySkin() : new ProbeSkin();
            var realmPath = Environment.GetEnvironmentVariable("O2JAM_DIAGNOSTIC_REALM");
            if (realmPath != null && Guid.TryParse(Environment.GetEnvironmentVariable("O2JAM_DIAGNOSTIC_SKIN"), out var skinId))
            {
                var legacy = O2JamReadOnlySkinProbe.Load(Host.Renderer, realmPath, skinId);
                var mania = new ManiaBeatmap(new StageDefinition(7));
                mania.BeatmapInfo.Ruleset = new ManiaRuleset().RulesetInfo;
                skin = O2JamSkinTransformer.WrapIfNeeded(new ManiaLegacySkinTransformer(legacy, mania));
            }
            Add(skinProvider = new ReloadableProbeSkinProvider(skin)
            {
                Clock = frameClock,
                Child = new Container
                {
                    RelativeSizeAxes = Axes.Y,
                    Width = 100,
                    Child = playfield,
                },
            });
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();
            if (Completed || Failure != null)
                return;
            try
            {
                hold ??= playfield.AllHitObjects.OfType<O2JamDrawableHoldNote>().FirstOrDefault()!;
                if (++frames > 200)
                    throw new InvalidOperationException($"Probe did not complete within 200 frames: hold={hold?.LoadState}, head={hold?.Head?.LoadState}, tail={hold?.Tail?.LoadState}, time={frameClock.CurrentTime}.");
                if (phase == 0 && (hold?.IsLoaded != true || hold.Head?.IsLoaded != true || hold.Tail?.IsLoaded != true))
                    return;

                if (rewind)
                {
                    updateRewindProbe();
                    return;
                }

                if (noRelease)
                {
                    updateNoReleaseProbe();
                    return;
                }

                switch (phase++)
                {
                    case 0:
                        seek(headAttemptTime);
                        break;
                    case 1:
                        ((IKeyBindingHandler<ManiaAction>)hold).OnPressed(new KeyBindingPressEvent<ManiaAction>(new InputState(), ManiaAction.Key1, false));
                        break;
                    case 2:
                        snapshot("head pressed");
                        if (ReloadLegacySkin)
                            skinProvider.Reload();
                        seek(releaseTime);
                        break;
                    case 3:
                        ((IKeyBindingHandler<ManiaAction>)hold).OnReleased(new KeyBindingReleaseEvent<ManiaAction>(new InputState(), ManiaAction.Key1));
                        snapshot("release input");
                        break;
                    case 4:
                        snapshot("after release judgement");
                        if (verifyO2Visual.HasValue)
                        {
                            verifyRelease();
                            releasedHeight = sizing().Height;
                            releasedY = hold.Y;
                        }
                        seek(verifyO2Visual.HasValue ? (releaseTime + 1821.917808219) / 2 : releaseTime + 100);
                        break;
                    case 5:
                        snapshot("remaining body");
                        if (verifyO2Visual.HasValue)
                            verifyRemainder();
                        seek(verifyO2Visual.HasValue ? 1820.917808219 : releaseTime + 300);
                        break;
                    case 6:
                        snapshot("before tail passes");
                        if (verifyO2Visual.HasValue)
                        {
                            verifyRemainder();
                            seek(1841.917808219);
                            break;
                        }
                        Completed = true;
                        Exit();
                        break;
                    case 7:
                        if (verifyO2Visual == true)
                        {
                            if (expectedAccuracy is O2JamAccuracy.Cool or O2JamAccuracy.Good)
                            {
                                Assert.That(hold.Head.Alpha, Is.Zero, "The pinned head must not outlive the body at the hit target.");
                                Assert.That(sizing().Height, Is.LessThan(0), "Clipping must continue through the tail cap, not freeze at zero length.");
                            }
                            else
                            {
                                Assert.That(hold.Head.Alpha, Is.EqualTo(1), "Dropped heads must scroll naturally rather than be pinned or hidden at the charted tail.");
                                Assert.That(sizing().Height, Is.EqualTo(releasedHeight), "BAD/MISS must not resume clipping after the tail passes.");
                            }
                        }
                        seek(2021.917808219);
                        break;
                    case 8:
                        Assert.That(playfield.AllHitObjects.OfType<O2JamDrawableHoldNote>(), Is.Empty, "Resolved LN must return to the pool after the tail passes.");
                        Completed = true;
                        Exit();
                        break;
                }
            }
            catch (Exception exception)
            {
                Failure = exception;
                Exit();
            }
        }

        private void updateRewindProbe()
        {
            switch (phase++)
            {
                case 0:
                case 6:
                    ((IKeyBindingHandler<ManiaAction>)hold).OnPressed(
                        new KeyBindingPressEvent<ManiaAction>(new InputState(), ManiaAction.Key1, false));
                    break;
                case 1:
                case 7:
                    if (rejectedHead.HasValue)
                        verifyRelease();
                    else
                        Assert.That(hold.GameplayState.IsHolding, Is.True);
                    seek(releaseTime);
                    break;
                case 2:
                case 8:
                    ((IKeyBindingHandler<ManiaAction>)hold).OnReleased(
                        new KeyBindingReleaseEvent<ManiaAction>(new InputState(), ManiaAction.Key1));
                    break;
                case 3:
                    Assert.That(hold.AllJudged, Is.True);
                    Assert.That(hold.GameplayState.IsHolding, Is.False);
                    completedState = processor.GameplayState.Current;
                    completedNativeScore = processor.TotalScore.Value;
                    completedNativeCombo = processor.Combo.Value;
                    Assert.That(completedState, Is.Not.EqualTo(initialState));
                    seek(900);
                    break;
                case 4:
                    Assert.That(revertedResults, Is.GreaterThanOrEqualTo(2));
                    Assert.That(hold.Head.Judged, Is.False);
                    Assert.That(hold.Tail.Judged, Is.False);
                    Assert.That(hold.Judged, Is.False);
                    Assert.That(hold.GameplayState.IsHolding, Is.False);
                    Assert.That(processor.GameplayState.Current, Is.EqualTo(initialState));
                    Assert.That(processor.TotalScore.Value, Is.EqualTo(initialState.Score));
                    Assert.That(processor.Combo.Value, Is.EqualTo(initialState.Combo));
                    Assert.That(((O2JamJudgementResult)hold.Head.Result).ResolutionApplied, Is.False);
                    Assert.That(((O2JamJudgementResult)hold.Tail.Result).ResolutionApplied, Is.False);
                    seek(999.051);
                    break;
                case 5:
                    Assert.That(hold.Head.Judged, Is.False);
                    seek(headAttemptTime);
                    break;
                case 9:
                    Assert.That(hold.AllJudged, Is.True);
                    Assert.That(processor.GameplayState.Current, Is.EqualTo(completedState));
                    Assert.That(processor.TotalScore.Value, Is.EqualTo(completedNativeScore));
                    Assert.That(processor.Combo.Value, Is.EqualTo(completedNativeCombo));
                    Completed = true;
                    Exit();
                    break;
            }
        }

        private void updateNoReleaseProbe()
        {
            switch (phase++)
            {
                case 0:
                    seek(999.051);
                    break;

                case 1:
                    ((IKeyBindingHandler<ManiaAction>)hold).OnPressed(
                        new KeyBindingPressEvent<ManiaAction>(new InputState(), ManiaAction.Key1, false));
                    break;

                case 2:
                    Assert.That(hold.IsHolding.Value, Is.True);
                    seek(1820.917808219);
                    break;

                case 3:
                    Assert.That(hold.Tail.Judged, Is.False);
                    Assert.That(hold.IsHolding.Value, Is.True);
                    seek(1822.917808219);
                    break;

                case 4:
                    var result = (O2JamJudgementResult)hold.Tail.Result;
                    Assert.That(result.RequestedAccuracy, Is.EqualTo(O2JamAccuracy.Cool));
                    Assert.That(result.Resolution.ResolvedAccuracy, Is.EqualTo(O2JamAccuracy.Cool));
                    Assert.That(hold.AllJudged, Is.True);
                    break;

                case 5:
                    Assert.That(hold.IsHolding.Value, Is.False);
                    Completed = true;
                    Exit();
                    break;
            }
        }

        private Container sizing() => (Container)typeof(DrawableHoldNote)
            .GetField("sizingContainer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hold)!;

        private void verifyRelease()
        {
            var result = (O2JamJudgementResult)hold.Tail.Result;
            Assert.That(result.RequestedAccuracy, Is.EqualTo(expectedAccuracy));
            Assert.That(result.Resolution.ResolvedAccuracy, Is.EqualTo(expectedAccuracy));
            Assert.That(hold.AllJudged, Is.True, "Keeping a visual alive must not defer judgement.");
            if (rejectedHead.HasValue)
            {
                var head = (O2JamJudgementResult)hold.Head.Result;
                Assert.That(head.Resolution.RequestedAccuracy, Is.EqualTo(rejectedHead.Value));
                Assert.That(head.Resolution.ResolvedAccuracy, Is.EqualTo(rescueHead ? O2JamAccuracy.Cool : rejectedHead.Value));
                Assert.That(head.Resolution.PillConsumed, Is.EqualTo(rescueHead));
                Assert.That(hold.GameplayState.CanBeginHold, Is.False);
                if (verifyO2Visual == true)
                    Assert.That(sizing().Height, Is.EqualTo(1), "O2Jam visuals must not clip a rejected head that was never held.");
            }
            Assert.That(hold.IsHolding.Value, Is.False, "Retention must not extend the hold light or hold state.");
            Assert.That(hold.Alpha, Is.EqualTo(verifyO2Visual == true || expectedAccuracy == O2JamAccuracy.Miss ? 1 : 0));
            if (verifyO2Visual == true)
            {
                Assert.That(hold.Tail.Alpha, Is.EqualTo(1));
                Assert.That(hold.LifetimeEnd, Is.GreaterThanOrEqualTo(hold.HitObject.EndTime));
                Assert.That(hold.Tail.LifetimeEnd, Is.GreaterThanOrEqualTo(hold.HitObject.EndTime));
                Assert.That(hold.Colour, Is.EqualTo((osu.Framework.Graphics.Colour.ColourInfo)Colour4.White));
            }
        }

        private void verifyRemainder()
        {
            if (UseLegacySkin)
                verifyLegacyColours();

            if (verifyO2Visual == true)
            {
                Assert.That(hold.HitObject, Is.Not.Null);
                Assert.That(hold.IsPresent, Is.True);
                Assert.That(hold.Tail.Alpha, Is.EqualTo(1));
                Assert.That(hold.Colour, Is.EqualTo((osu.Framework.Graphics.Colour.ColourInfo)Colour4.White));
                if (expectedAccuracy is O2JamAccuracy.Cool or O2JamAccuracy.Good)
                {
                    Assert.That(sizing().Height, Is.LessThan(releasedHeight));
                    var offset = scrolling.Direction.Value == ScrollingDirection.Up ? -hold.Y : hold.Y;
                    Assert.That(sizing().Height, Is.EqualTo(1 - offset / hold.DrawHeight).Within(0.0001));
                }
                else
                {
                    Assert.That(sizing().Height, Is.EqualTo(releasedHeight), "BAD/MISS must preserve the remaining length.");
                    Assert.That(hold.Y, Is.Not.EqualTo(releasedY), "Stopping clipping must not stop scrolling.");
                }
            }
            else if (expectedAccuracy == O2JamAccuracy.Miss)
            {
                Assert.That(hold.IsPresent, Is.True);
                Assert.That(hold.Colour, Is.EqualTo((osu.Framework.Graphics.Colour.ColourInfo)Colour4.DarkGray));
                if (rejectedHead == O2JamAccuracy.Bad)
                    Assert.That(sizing().Height, Is.LessThan(releasedHeight), "Disabled O2Jam visuals retain mania clipping for an IsHit head.");
                else
                    Assert.That(sizing().Height, Is.EqualTo(releasedHeight));
            }
            else
                Assert.That(playfield.AllHitObjects.OfType<O2JamDrawableHoldNote>(), Is.Empty);
        }

        private void seek(double time)
        {
            sourceClock.CurrentTime = time;
            frameClock.ProcessFrame();
        }

        private void snapshot(string stage)
        {
            if (hold.HitObject == null || hold.Head == null || hold.Tail == null)
            {
                Observations.Add($"{stage}: t={frameClock.CurrentTime:F3}; drawable returned to pool; alpha={hold.Alpha}; present={hold.IsPresent}");
                return;
            }
            var sizing = (Container)typeof(DrawableHoldNote).GetField("sizingContainer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hold)!;
            var body = (SkinnableDrawable)typeof(DrawableHoldNote).GetField("bodyPiece", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hold)!;
            Observations.Add($"{stage}: t={frameClock.CurrentTime:F3}; head={hold.Head.Result.Type}; tail={hold.Tail.Result.Type}; parent={hold.Result.Type}; alpha={hold.Alpha}; tailAlpha={hold.Tail.Alpha}; lifetime={hold.LifetimeEnd:F3}; tailLifetime={hold.Tail.LifetimeEnd:F3}; missing={hold.MissingStartTime.Value}; holding={hold.IsHolding.Value}; dropped={hold.Result.DroppedHoldAfter(hold.HitObject.StartTime)}; sizing={sizing.Height}; drawHeight={hold.DrawHeight}; y={hold.Y}; alive={hold.IsAlive}; present={hold.IsPresent}");
            var textures = string.Join(';', body.ChildrenOfType<Sprite>().Select(sprite => $"{sprite.Texture?.Width}x{sprite.Texture?.Height} alpha={sprite.Alpha} present={sprite.IsPresent} draw={sprite.DrawSize} scale={sprite.Scale}"));
            Observations.Add($"  Body={body.Drawable?.GetType().FullName}, headHeight={hold.Head.Height}, tailHeight={hold.Tail.Height}, bodyY={body.Y}, bodyHeight={body.Height}, headSkin={hold.Head.ChildrenOfType<SkinnableDrawable>().FirstOrDefault()?.Drawable?.GetType().Name}; textures={textures}");
        }
    }

    private partial class ProbePlayfield : ScrollingPlayfield
    {
        [BackgroundDependencyLoader]
        private void load()
        {
            RegisterPool<O2JamHoldNote, O2JamDrawableHoldNote>(1);
            RegisterPool<O2JamHoldHead, O2JamDrawableHoldHead>(1);
            RegisterPool<O2JamHoldTail, O2JamDrawableHoldTail>(1);
            RegisterPool<O2JamHoldBody, O2JamDrawableHoldBody>(1);
        }
    }

    private sealed class ProbeSettings : IGameplaySettings
    {
        public IBindable<float> ComboColourNormalisationAmount { get; } = new BindableFloat();
        public IBindable<float> PositionalHitsoundsLevel { get; } = new BindableFloat();
    }

    private sealed class ProbeScrollingInfo : IScrollingInfo
    {
        public IBindable<ScrollingDirection> Direction { get; } = new Bindable<ScrollingDirection>(ScrollingDirection.Down);
        public IBindable<double> TimeRange { get; } = new BindableDouble(1000);
        public IBindable<IScrollAlgorithm> Algorithm { get; } = new Bindable<IScrollAlgorithm>(new ConstantScrollAlgorithm());
    }

    private sealed class ProbeSkin : ISkin
    {
        public Drawable? GetDrawableComponent(ISkinComponentLookup lookup) => lookup is ManiaSkinComponentLookup mania
            ? new Box { RelativeSizeAxes = mania.Component == ManiaSkinComponents.HoldNoteBody ? Axes.Both : Axes.X, Height = 10 }
            : null;
        public Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT) => null;
        public ISample? GetSample(ISampleInfo sample) => null;
        public IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup) where TLookup : notnull where TValue : notnull => null;
    }
}
