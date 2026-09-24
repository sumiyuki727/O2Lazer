using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Input.Bindings;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Rulesets.O2Lazer.UI.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.UI.Scrolling;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamHoldVisualProbeTest
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void SyntheticReplayRestoresHoldAfterRewind(bool afterPooling, bool frameAccurate)
    {
        using var host = new TestRunHeadlessGameHost($"O2JamSyntheticRewind-{Guid.NewGuid():N}");
        var game = new ReplayRewindGame(afterPooling, frameAccurate);
        host.Run(game);
        if (game.Failure != null)
            throw game.Failure;
        Assert.That(game.Completed, Is.True);
    }

    [TestCase("early-good", false)]
    [TestCase("early-bad", true)]
    [TestCase("early-miss", true)]
    [TestCase("head-bad", true)]
    [TestCase("head-miss", false)]
    public void SyntheticReplayReplaysRejectedHoldsAndNeighbour(string scenario, bool frameAccurate)
    {
        using var host = new TestRunHeadlessGameHost($"O2JamMixedReplay-{Guid.NewGuid():N}");
        var game = new ReplayRewindGame(true, frameAccurate, scenario);
        host.Run(game);
        if (game.Failure != null)
            throw game.Failure;
        Assert.That(game.Completed, Is.True);
    }

    private partial class ReplayRewindGame(bool afterPooling, bool frameAccurate, string? scenario = null) : Framework.Game
    {
        private readonly ManualClock clock = new();
        private readonly O2LazerRuleset ruleset = new();
        private readonly O2JamScoreProcessor processor = new(new O2LazerRuleset());
        private FrameStabilityContainer stability = null!;
        private O2JamManiaColumn column = null!;
        private O2JamRulesetConfigManager config = null!;
        private OsuConfigManager gameConfig = null!;
        private O2JamGameplaySnapshot initial;
        private O2JamGameplaySnapshot finished;
        private long nativeScore;
        private int phase;
        private int frames;
        public Exception? Failure;
        public bool Completed;

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
            dependencies.Cache(gameConfig = new OsuConfigManager(Host.Storage));
            dependencies.Cache(new osu.Game.Graphics.OsuColour());
            dependencies.Cache(new StageDefinition(7));
            dependencies.CacheAs<IGameplaySettings>(new ProbeSettings());
            dependencies.CacheAs<IScrollingInfo>(new ProbeScrollingInfo());
            dependencies.CacheAs<ScoreProcessor>(processor);
            config = new O2JamRulesetConfigManager(null, ruleset.RulesetInfo);
            config.SetValue(O2JamRulesetSetting.O2JamStyleDroppedHold, false);
            dependencies.Cache(config);
            return dependencies;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            var timing = new O2JamTimingMap(120);
            var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, timing);
            var note = new O2JamHoldNote
            {
                StartTime = 1000, Duration = 1000, Column = 0, TimingMap = timing,
                HeadChartPosition = timing.PositionAt(1000), TailChartPosition = timing.PositionAt(2000),
            };
            note.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
            beatmap.HitObjects.Add(note);
            processor.ApplyBeatmap(beatmap);
            initial = processor.GameplayState.Current;
            if (scenario != null)
            {
                var neighbour = new O2JamNote { StartTime = 2200, Column = 0, TimingMap = timing, ChartPosition = timing.PositionAt(2200) };
                neighbour.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
                beatmap.HitObjects.Add(neighbour);
                processor.ApplyBeatmap(beatmap);
                initial = processor.GameplayState.Current;
            }
            var replay = new O2JamAutoGenerator(beatmap).Generate();
            if (scenario != null)
            {
                var press = scenario == "head-bad" ? 1240 : scenario == "head-miss" ? 1400 : 1000;
                var release = scenario switch { "early-good" => 1900, "early-bad" => 1760, "early-miss" => 1600, _ => 2000 };
                replay.Frames = [new O2JamReplayFrame(press, ManiaAction.Key1), new O2JamReplayFrame(release),
                    new O2JamReplayFrame(2200, ManiaAction.Key1), new O2JamReplayFrame(2220)];
            }
            var handler = new O2JamFramedReplayInputHandler(replay) { FrameAccuratePlayback = frameAccurate };
            var action = ManiaAction.Key1;
            var stage = new O2JamManiaStage(0, beatmap.Stages[0], ref action);
            column = (O2JamManiaColumn)stage.Columns[0];
            column.NewResult += (_, result) =>
            {
                processor.ApplyResult(result);
                if (phase == 0 && scenario != null && result is O2JamJudgementResult judged)
                {
                    var expected = result.HitObject is O2JamHoldTail ? scenario switch
                    {
                        "early-good" => O2JamAccuracy.Good,
                        "early-bad" => O2JamAccuracy.Bad,
                        _ => O2JamAccuracy.Miss,
                    } : result.HitObject is O2JamHoldHead ? scenario switch
                    {
                        "head-bad" => O2JamAccuracy.Bad,
                        "head-miss" => O2JamAccuracy.Miss,
                        _ => O2JamAccuracy.Cool,
                    } : O2JamAccuracy.Cool;
                    Assert.That(judged.RequestedAccuracy, Is.EqualTo(expected));
                }
            };
            column.RevertResult += processor.RevertResult;
            Add(new SkinProvidingContainer(new ProbeSkin())
            {
                Clock = new FramedClock(clock),
                Child = stability = new FrameStabilityContainer(0)
                {
                    ReplayInputHandler = handler,
                    Child = new ReplayProbeInput(ruleset.RulesetInfo)
                    {
                        ReplayInputHandler = handler,
                        RelativeSizeAxes = Axes.Both,
                        Child = stage,
                    },
                },
            });
            foreach (var hitObject in beatmap.HitObjects)
                column.Add(hitObject);
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();
            if (Completed || Failure != null)
                return;
            try
            {
                if (++frames > 2000)
                    throw new InvalidOperationException($"Replay rewind timeout: phase={phase}, time={stability.CurrentTime}");
                var target = phase switch { 0 => afterPooling ? 2500 : 2050, 1 => afterPooling ? 900 : 1500, _ => 2500 };
                clock.CurrentTime = target;
                if (stability.CurrentTime != target)
                    return;
                var hold = column.AllHitObjects.OfType<O2JamDrawableHoldNote>().SingleOrDefault();
                switch (phase++)
                {
                    case 0:
                        finished = processor.GameplayState.Current;
                        nativeScore = processor.TotalScore.Value;
                        Assert.That(finished.Score, Is.GreaterThan(0));
                        if (scenario == null)
                            Assert.That(finished.Combo, Is.EqualTo(1));
                        if (afterPooling)
                            Assert.That(hold, Is.Null, "The first pass must actually return the hold to its pool.");
                        break;
                    case 1:
                        Assert.That(hold, Is.Not.Null);
                        Assert.That(hold!.Tail.Judged, Is.False);
                        if (afterPooling)
                        {
                            Assert.That(hold.Head.Judged, Is.False);
                            Assert.That(processor.GameplayState.Current, Is.EqualTo(initial));
                            Assert.That(processor.Combo.Value, Is.EqualTo(-1));
                        }
                        else
                        {
                            Assert.That(hold.Head.Judged, Is.True);
                            Assert.That(hold.GameplayState.IsHolding, Is.True);
                            Assert.That(processor.GameplayState.Current.Combo, Is.Zero);
                        }
                        break;
                    case 2:
                        Assert.That(processor.GameplayState.Current, Is.EqualTo(finished));
                        Assert.That(processor.TotalScore.Value, Is.EqualTo(nativeScore));
                        Assert.That(column.AllHitObjects.OfType<O2JamDrawableHoldNote>(), Is.Empty);
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

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            config?.Dispose();
            gameConfig?.Dispose();
        }
    }

    private partial class ReplayProbeInput(RulesetInfo ruleset) : ManiaInputManager(ruleset, 0)
    {
        protected override KeyBindingContainer<ManiaAction> CreateKeyBindingContainer(RulesetInfo ruleset, int variant, SimultaneousBindingMode unique)
            => new ReplayProbeBindings(unique);
    }

    private partial class ReplayProbeBindings(SimultaneousBindingMode mode) : KeyBindingContainer<ManiaAction>(mode)
    {
        public override IEnumerable<IKeyBinding> DefaultKeyBindings => [];
    }
}
