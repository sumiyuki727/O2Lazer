using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Scoring.Judgements;
using osu.Game.Rulesets.O2Lazer.UI.Objects.LnHelper;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class O2LazerLongNoteJudgementControllerTest
{
    private static (O2LazerLongNoteJudgementController controller, FakeLongNoteHooks hooks) makeController(
        double start, double duration, int column = 1, int rank = 2)
    {
        var ln = new O2LazerLongNote
        {
            StartTime = start,
            Duration = duration,
            Column = column,
            Beatmap = new O2LazerBeatmap
            {
                LayoutVariant = O2LazerLayoutVariant.O2Jam7K,
                TotalColumns = 7,
                Rank = rank,
                LockedLongNoteMode = O2LazerLongNoteMode.Undefined,
            },
        };
        var hooks = new FakeLongNoteHooks();
        var controller = new O2LazerLongNoteJudgementController();
        controller.Bind(ln, hooks);
        return (controller, hooks);
    }

    private static O2LazerJudgementWindowTable tailTable(int rank = 2)
        => O2LazerJudgementProfileProvider.GetTable(O2LazerLayoutVariant.O2Jam7K, 1, rank, true);

    [Test]
    public void TestO2JamMissedHeadCountsHeadAndTailMiss()
    {
        var (controller, hooks) = makeController(1000, 500);

        controller.CheckPassiveResult(1300);

        Assert.Multiple(() =>
        {
            Assert.That(controller.IsO2Jam, Is.True);
            Assert.That(controller.TailJudged, Is.True);
            Assert.That(hooks.AppliedResults, Is.EqualTo(new[] { HitResult.Miss }));
            Assert.That(hooks.SyntheticJudgements.Select(result => result.result), Is.EqualTo(new[] { HitResult.Miss }));
        });
    }

    [Test]
    public void TestO2JamJudgesHeadAndTailIndependently()
    {
        var (controller, hooks) = makeController(1000, 500);

        Assert.That(controller.TryHit(1000, HitResult.Perfect), Is.True);
        Assert.That(controller.TryRelease(1500, 0, tailTable()), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(hooks.AppliedResults, Is.EqualTo(new[] { HitResult.Perfect }));
            Assert.That(hooks.SyntheticJudgements.Select(result => result.result), Is.EqualTo(new[] { HitResult.Perfect }));
        });
    }

    [Test]
    public void TestO2JamBadHeadStartsHoldAndTailJudgedIndependently()
    {
        var (controller, hooks) = makeController(1000, 500);

        Assert.That(controller.TryHit(1000, HitResult.Ok), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(controller.LongNoteStarted, Is.True);
            Assert.That(controller.TailJudged, Is.False);
            Assert.That(hooks.SyntheticJudgements, Is.Empty);
        });

        Assert.That(controller.TryRelease(1500, 0, tailTable()), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(hooks.AppliedResults, Is.EqualTo(new[] { HitResult.Ok }));
            Assert.That(hooks.SyntheticJudgements.Select(result => result.result), Is.EqualTo(new[] { HitResult.Perfect }));
        });
    }

    [Test]
    public void TestO2JamLateTailReleaseIsMissNotMeh()
    {
        var (controller, hooks) = makeController(1000, 500);

        Assert.That(controller.TryHit(1000, HitResult.Perfect), Is.True);
        Assert.That(controller.TryRelease(1800, 300, tailTable()), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(hooks.SyntheticJudgements.Select(result => result.result), Is.EqualTo(new[] { HitResult.Miss }));
            Assert.That(hooks.ClearedTails, Is.EqualTo(new[] { HitResult.Miss }));
        });
    }

    [Test]
    public void TestO2JamPassiveTailMissFiresMissNotMeh()
    {
        var (controller, hooks) = makeController(1000, 500);

        Assert.That(controller.TryHit(1000, HitResult.Perfect), Is.True);
        controller.CheckPassiveResult(1500 + 600);

        Assert.Multiple(() =>
        {
            Assert.That(hooks.SyntheticJudgements.Select(result => result.result), Is.EqualTo(new[] { HitResult.Miss }));
            Assert.That(controller.TailJudged, Is.True);
        });
    }

    private sealed class FakeLongNoteHooks : IO2LazerLongNoteHooks
    {
        public List<(HitResult result, IReadOnlyList<O2LazerLongNoteEndpointResult> endpoints)> AppliedJudgements { get; } = [];

        public IReadOnlyList<HitResult> AppliedResults => AppliedJudgements.Select(j => j.result).ToList();

        public IReadOnlyList<(double endpointTime, double eventTime, HitResult result)> AppliedEndpoints =>
            AppliedJudgements.SelectMany(j => j.endpoints.Select(e => (e.ExpectedTime, e.EventTime, e.Result))).ToList();

        public List<HitResult> ClearedTails { get; } = [];

        public List<(HitResult result, O2LazerLongNoteEndpointResult endpoint)> SyntheticJudgements { get; } = [];

        public IReadOnlyList<(double endpointTime, double eventTime, HitResult result)> SyntheticEndpoints =>
            SyntheticJudgements.Select(j => (j.endpoint.ExpectedTime, j.endpoint.EventTime, j.endpoint.Result)).ToList();

        public List<(double eventTime, double lifetimeEnd)> HellChargeHeadPoor { get; } = [];

        public List<(bool holding, double scale)> HellChargeTicks { get; } = [];

        public int UserHeadJudgedCount;

        public int RetireCount;

        public void OnUserHeadJudged() => UserHeadJudgedCount++;

        public void OnHellChargeHeadPoor(double eventTime, double lifetimeEnd) => HellChargeHeadPoor.Add((eventTime, lifetimeEnd));

        public void ApplyJudgementResult(HitResult result, IReadOnlyList<O2LazerLongNoteEndpointResult> endpoints)
            => AppliedJudgements.Add((result, endpoints));

        public void ClearVisualIfTailWasNotPoor(HitResult result)
        {
            if (result == HitResult.Meh)
                return;

            ClearedTails.Add(result);
        }

        public void ApplySyntheticEndpoint(HitResult result, O2LazerLongNoteEndpointResult endpoint)
            => SyntheticJudgements.Add((result, endpoint));

        public void ApplyHellChargeTick(bool holding, double scale) => HellChargeTicks.Add((holding, scale));

        public void Retire() => RetireCount++;
    }
}
