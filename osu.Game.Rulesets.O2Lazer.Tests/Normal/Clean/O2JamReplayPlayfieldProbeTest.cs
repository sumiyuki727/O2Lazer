using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Colour;
using osu.Framework.Input.Bindings;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osuTK.Graphics;
using osu.Game.Configuration;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Objects.Drawables;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Rulesets.O2Lazer.UI.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.UI.Scrolling;
using osu.Game.Rulesets.UI.Scrolling.Algorithms;
using osu.Game.Scoring;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
[Category("LocalDiagnostics")]
public partial class O2JamReplayPlayfieldProbeTest
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    [Explicit("Replays the reported chart through real input, frame stability, column and pooling without touching the user's database.")]
    public void InspectActualReplay(bool o2Visual, bool frameAccurate)
    {
        var replayPath = Environment.GetEnvironmentVariable("O2JAM_REPLAY_DIAGNOSTIC_PATH");
        var corpusPath = Environment.GetEnvironmentVariable("O2JAM_CORPUS_PATH");
        if (string.IsNullOrWhiteSpace(replayPath) || string.IsNullOrWhiteSpace(corpusPath))
            Assert.Ignore("Set O2JAM_CORPUS_PATH, O2JAM_REPLAY_DIAGNOSTIC_PATH, O2JAM_DIAGNOSTIC_REALM and O2JAM_DIAGNOSTIC_SKIN (see docs/development.md).");
        var replayBytes = File.ReadAllBytes(replayPath!);
        Assert.That(O2JamReplayArchive.TryReadMetadata(replayBytes, out var metadata), Is.True);
        Assert.That(metadata.Statistics, Is.Not.Empty);
        Assert.That(O2JamReplayArchive.TryReadScore(new ScoreInfo { Statistics = metadata.Statistics }, replayBytes, out var score), Is.True);
        using var source = File.OpenRead(Path.Combine(corpusPath!, "ESong", "o2ma387.ojn"));
        var document = new OjnReader().ReadChart(source, O2JamDifficulty.HX);
        var beatmap = new OjnBeatmapFactory().Create(document, O2JamDifficulty.HX);
        foreach (var note in beatmap.HitObjects)
        {
            note.Samples.Clear();
            note.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        }

        using var host = new TestRunHeadlessGameHost($"O2JamReplayPlayfieldProbe-{Guid.NewGuid():N}");
        var game = new ProbeGame(beatmap, score, o2Visual, frameAccurate);
        host.Run(game);
        foreach (var line in game.Observations)
            TestContext.Progress.WriteLine(line);
        if (game.Failure != null)
            throw game.Failure;
        Assert.That(game.Completed, Is.True);
        Assert.That(game.VerifiedFinalGroup, Is.True);
        Assert.That(game.VerifiedRemainder, Is.True);
    }

    private partial class ProbeGame(O2JamBeatmap beatmap, Score score, bool o2Visual, bool frameAccurate) : Framework.Game
    {
        private readonly ManualClock referenceClock = new();
        private readonly ProbeScrollingInfo scrolling = new();
        private readonly O2LazerRuleset ruleset = new();
        private readonly O2JamScoreProcessor processor = new(new O2LazerRuleset());
        private FrameStabilityContainer stability = null!;
        private O2JamManiaColumn column = null!;
        private O2JamManiaColumn[] columns = [];
        private JudgementContainer<DrawableManiaJudgement> judgements = null!;
        private O2JamRulesetConfigManager config = null!;
        private OsuConfigManager gameConfig = null!;
        private O2JamReadOnlySkinProbe skin = null!;
        private bool optionChanged;
        private int frames;
        private string lastState = string.Empty;
        private string lastDisplayedJudgement = string.Empty;
        private float releaseHeight;
        private readonly Dictionary<int, float> missedReleaseHeights = [];
        public Exception? Failure;
        public bool Completed;
        public bool VerifiedFinalGroup;
        public bool VerifiedRemainder;
        public List<string> Observations { get; } = [];

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
            dependencies.Cache(gameConfig = new OsuConfigManager(Host.Storage));
            dependencies.Cache(new osu.Game.Graphics.OsuColour());
            dependencies.Cache(new StageDefinition(7));
            dependencies.CacheAs<IGameplaySettings>(new ProbeSettings());
            dependencies.CacheAs<IScrollingInfo>(scrolling);
            dependencies.CacheAs<ScoreProcessor>(processor);
            config = new O2JamRulesetConfigManager(null, ruleset.RulesetInfo);
            config.SetValue(O2JamRulesetSetting.O2JamStyleDroppedHold, !o2Visual);
            dependencies.Cache(config);
            return dependencies;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            processor.ApplyBeatmap(beatmap);
            var realmPath = Environment.GetEnvironmentVariable("O2JAM_DIAGNOSTIC_REALM")!;
            var skinId = Guid.Parse(Environment.GetEnvironmentVariable("O2JAM_DIAGNOSTIC_SKIN")!);
            skin = O2JamReadOnlySkinProbe.Load(Host.Renderer, realmPath, skinId);
            var transformed = ruleset.CreateSkinTransformer(skin, beatmap)!;
            var handler = new O2JamFramedReplayInputHandler(score.Replay) { FrameAccuratePlayback = frameAccurate };
            var firstAction = ManiaAction.Key1;
            var stage = new O2JamManiaStage(0, beatmap.Stages[0], ref firstAction);
            columns = stage.Columns.Cast<O2JamManiaColumn>().ToArray();
            judgements = (JudgementContainer<DrawableManiaJudgement>)typeof(Stage)
                         .GetField("judgements", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(stage)!;
            foreach (var item in columns)
            {
                item.NewResult += (drawable, result) =>
                {
                    processor.ApplyResult(result);
                    if (stability.CurrentTime >= 92400 && result is O2JamJudgementResult o2Result)
                        Observations.Add($"result {stability.CurrentTime:F3}: column={item.Index + 1}, object={result.HitObject.GetType().Name}, start={result.HitObject.StartTime:F3}, offset={result.TimeOffset:F3}, requested={o2Result.RequestedAccuracy}, resolved={o2Result.Resolution.ResolvedAccuracy}, pill={o2Result.Resolution.PillConsumed}, framework={result.Type}, display={drawable.DisplayResult}");
                };
                item.RevertResult += processor.RevertResult;
            }
            column = columns[3];
            var input = new ProbeInputManager(ruleset.RulesetInfo)
            {
                ReplayInputHandler = handler,
                RelativeSizeAxes = Axes.Both,
                Child = new ObservingContainer(observe) { RelativeSizeAxes = Axes.Both, Child = stage },
            };
            Add(new SkinProvidingContainer(transformed)
            {
                Clock = new FramedClock(referenceClock),
                Child = stability = new FrameStabilityContainer(0)
                {
                    ReplayInputHandler = handler,
                    Child = input,
                },
            });
            foreach (var note in beatmap.HitObjects)
                columns[note.Column].Add(note);
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();
            if (Failure != null || Completed)
                return;
            if (++frames > 20000)
            {
                Failure = new InvalidOperationException($"Replay probe timed out at {stability.CurrentTime}.");
                Exit();
                return;
            }
            referenceClock.CurrentTime = 94000;
            if (stability.CurrentTime >= 94000)
            {
                try
                {
                    Observations.Add($"Final statistics: {string.Join(", ", processor.Statistics.Select(entry => $"{entry.Key}={entry.Value}"))}");
                    foreach (var statistic in score.ScoreInfo.Statistics)
                        Assert.That(processor.Statistics.GetValueOrDefault(statistic.Key), Is.EqualTo(statistic.Value), $"Replay statistic {statistic.Key} must not change with LN presentation.");
                    Assert.That(columns.SelectMany(col => col.AllHitObjects).OfType<O2JamDrawableHoldNote>()
                                       .Where(h => h.HitObject.StartTime > 92460), Is.Empty);
                    Completed = true;
                }
                catch (Exception ex)
                {
                    Failure = ex;
                }
                Exit();
            }
        }

        private void observe()
        {
            try
            {
                if (stability.CurrentTime > 92000 && !optionChanged)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    config.SetValue(O2JamRulesetSetting.O2JamStyleDroppedHold, o2Visual);
                    optionChanged = true;
                }
                if (stability.CurrentTime < 92400 || stability.CurrentTime > 93500)
                    return;
                var displayed = judgements.Children.LastOrDefault();
                var displayedState = displayed?.Result == null ? "none"
                    : $"{displayed.Result.Type}, column={((osu.Game.Rulesets.Mania.Objects.ManiaHitObject)displayed.JudgedHitObject!).Column + 1}, object={displayed.JudgedHitObject.GetType().Name}, time={displayed.Result.TimeAbsolute:F3}";
                if (displayedState != lastDisplayedJudgement)
                {
                    lastDisplayedJudgement = displayedState;
                    Observations.Add($"HUD {stability.CurrentTime:F3}: {displayedState}, frameAccurate={frameAccurate}");
                }
                var hold = column.AllHitObjects.OfType<O2JamDrawableHoldNote>().FirstOrDefault(h => h.HitObject.StartTime > 92460);
                if (VerifiedFinalGroup && !VerifiedRemainder && stability.CurrentTime > 93000)
                {
                    if (o2Visual)
                    {
                        Assert.That(hold, Is.Not.Null, "The pill-rescued tail must not remove the remaining LN.");
                        var retainedSizing = (Container)typeof(DrawableHoldNote).GetField("sizingContainer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hold)!;
                        Assert.That(hold!.IsPresent, Is.True);
                        Assert.That(hold.Tail.Alpha, Is.EqualTo(1));
                        Assert.That(hold.IsHolding.Value, Is.False);
                        Assert.That(retainedSizing.Height, Is.LessThan(releaseHeight));
                        Assert.That(retainedSizing.Height, Is.EqualTo(1 - hold.Y / hold.DrawHeight).Within(0.0001));
                    }
                    else
                        Assert.That(hold, Is.Null, "The mania-compatible hit hiding must remain unchanged when the option is disabled.");
                    foreach (var (index, height) in missedReleaseHeights)
                    {
                        var missed = columns[index].AllHitObjects.OfType<O2JamDrawableHoldNote>()
                                            .Single(h => h.HitObject.StartTime > 92000 && h.Tail.Result.Type == HitResult.Miss);
                        var missedSizing = (Container)typeof(DrawableHoldNote).GetField("sizingContainer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(missed)!;
                        Assert.That(missed.IsPresent, Is.True);
                        Assert.That(missedSizing.Height, Is.EqualTo(height), "Neighbouring MISS holds must scroll without further clipping.");
                    }
                    VerifiedRemainder = true;
                }
                var state = hold == null ? "absent" : $"head={hold.Head.Result.Type}, tail={hold.Tail.Result.Type}, parent={hold.Result.Type}, alpha={hold.Alpha}, colour={hold.Colour}, missing={hold.MissingStartTime.Value}, holding={hold.IsHolding.Value}";
                if (state == lastState)
                    return;
                lastState = state;
                Observations.Add($"{stability.CurrentTime:F3}: {state}; o2Visual={O2JamRuntimeOptions.UseO2JamLongNoteMissVisual}");
                if (hold != null)
                {
                    var sizing = (Container)typeof(DrawableHoldNote).GetField("sizingContainer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hold)!;
                    Observations.Add($"  lifetime={hold.LifetimeEnd}, sizing={sizing.Height}, height={hold.DrawHeight}, y={hold.Y}, tailAlpha={hold.Tail.Alpha}, resultTime={hold.Tail.Result.TimeAbsolute}, tailEnd={hold.Tail.HitObject.StartTime}, time={hold.Clock.CurrentTime}, present={hold.IsPresent}");
                    var tail = (Objects.O2JamHoldTail)hold.Tail.HitObject;
                    var result = (O2JamJudgementResult)hold.Tail.Result;
                    Observations.Add($"  directJudge={tail.Judge(stability.CurrentTime, true)}; requested={result.RequestedAccuracy}, resolved={result.Resolution.ResolvedAccuracy}, pill={result.Resolution.PillConsumed}; positionTime={tail.TimingMap.TimeAt(tail.ChartPosition)}; judgementTarget={result.HitObject.StartTime}, mapEvents={tail.TimingMap.Events.Count}");
                    if (hold.Tail.Judged && !VerifiedFinalGroup)
                    {
                        Assert.That(result.RequestedAccuracy, Is.EqualTo(O2JamAccuracy.Bad));
                        Assert.That(result.Resolution.ResolvedAccuracy, Is.EqualTo(O2JamAccuracy.Cool));
                        Assert.That(result.Resolution.PillConsumed, Is.True);
                        Assert.That(hold.Alpha, Is.EqualTo(o2Visual ? 1 : 0));
                        if (o2Visual)
                        {
                            Assert.That(hold.Tail.Alpha, Is.EqualTo(1));
                            Assert.That(hold.LifetimeEnd, Is.GreaterThanOrEqualTo(hold.HitObject.EndTime));
                        }
                        Assert.That(displayed?.Result, Is.SameAs(result), "The native stage must display this tail result, not a prior neighbouring Miss.");
                        var misses = columns.SelectMany(col => col.AllHitObjects).OfType<O2JamDrawableHoldNote>()
                                            .Where(h => h.HitObject.StartTime > 92000 && h.Tail.Result.Type == HitResult.Miss).ToArray();
                        Assert.That(misses, Has.Length.EqualTo(6));
                        foreach (var missed in misses)
                        {
                            Assert.That(missed.Alpha, Is.EqualTo(1));
                            Assert.That(missed.LifetimeEnd, Is.GreaterThanOrEqualTo(missed.HitObject.EndTime));
                            Assert.That(missed.Colour, Is.EqualTo((ColourInfo)(o2Visual ? Colour4.White : Colour4.DarkGray)));
                            var missedSizing = (Container)typeof(DrawableHoldNote).GetField("sizingContainer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(missed)!;
                            missedReleaseHeights[missed.HitObject.Column] = missedSizing.Height;
                        }
                        VerifiedFinalGroup = true;
                        releaseHeight = sizing.Height;
                    }
                }
                foreach (var missed in columns.SelectMany(col => col.AllHitObjects).OfType<O2JamDrawableHoldNote>().Where(h => h.MissingStartTime.Value != null && h.HitObject.StartTime > 92000))
                    Observations.Add($"  missed column={missed.HitObject.Column + 1}, alpha={missed.Alpha}, colour={missed.Colour}, drawColour={missed.DrawColourInfo.Colour}, tail={missed.Tail.Result.Type}, missing={missed.MissingStartTime.Value}");
            }
            catch (Exception ex)
            {
                Failure = ex;
                Exit();
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            skin?.Dispose();
            config?.Dispose();
            gameConfig?.Dispose();
        }
    }

    private partial class ProbeInputManager(RulesetInfo ruleset) : ManiaInputManager(ruleset, 0)
    {
        protected override KeyBindingContainer<ManiaAction> CreateKeyBindingContainer(RulesetInfo ruleset, int variant, SimultaneousBindingMode unique)
            => new ProbeBindings(unique);
    }

    private partial class ProbeBindings(SimultaneousBindingMode mode) : KeyBindingContainer<ManiaAction>(mode)
    {
        public override IEnumerable<IKeyBinding> DefaultKeyBindings => [];
    }

    private partial class ObservingContainer(Action observe) : Container
    {
        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();
            observe();
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
        public IBindable<double> TimeRange { get; } = new BindableDouble(11485d / 27 * (768 - 467) / (768 - 402));
        public IBindable<IScrollAlgorithm> Algorithm { get; } = new Bindable<IScrollAlgorithm>(new ConstantScrollAlgorithm());
    }
}
