using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Localisation;
using osu.Game.Overlays.Mods;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Expanded.Statistics;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public partial class O2JamPerformanceEligibilityTest
{
    [TestCase(true, "", null, 0.5f)]
    [TestCase(true, "NF", null, 0.5f)]
    [TestCase(true, "MR", null, 0.5f)]
    [TestCase(true, "MS,RD", null, 0.5f)]
    [TestCase(true, "", 0d, 0.5f)]
    [TestCase(true, "", 123.4d, 0.5f)]
    [TestCase(true, "MS", null, 1f)]
    [TestCase(true, "MS", 123.4d, 1f)]
    [TestCase(false, "", null, 1f)]
    [TestCase(false, "", 123.4d, 1f)]
    [TestCase(false, "RD", 123.4d, 0.5f)]
    public async Task ResultsPpStylingDoesNotDependOnHavingAPerformanceValue(bool o2lazer, string selection, double? pp, float expectedAlpha)
    {
        var o2Ruleset = new O2LazerRuleset();
        Assert.That(O2JamPerformanceEligibilityPatch.IsInstalled, Is.True);
        Ruleset ruleset = o2lazer ? o2Ruleset : new ManiaRuleset();
        Mod[] mods = o2lazer ? createMods(o2Ruleset, selection) : selection == "RD" ? [new ManiaModRandom()] : [];
        var score = new ScoreInfo(ruleset: ruleset.RulesetInfo)
        {
            BeatmapInfo = new BeatmapInfo(ruleset.RulesetInfo) { Status = BeatmapOnlineStatus.Ranked },
            Mods = mods,
            Rank = ScoreRank.A,
            PP = pp,
        };
        using var cache = new MissingPerformanceCache();
        using var statistic = new PerformanceStatistic(score);

        // Exercise the native construction, load, and appear paths. The null calculator
        // path used to skip setPerformanceValue entirely, leaving the default zero bright.
        typeof(StatisticDisplay).GetMethod("load", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(statistic, null);
        typeof(PerformanceStatistic).GetMethod("load", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(statistic, [cache, null]);
        if (!pp.HasValue)
            await cache.LookupCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        statistic.Appear();
        statistic.FinishTransforms(true);

        Assert.That(statistic.Alpha, Is.EqualTo(expectedAlpha));
        Assert.That(statistic.TooltipText, Is.EqualTo(expectedAlpha == 0.5f ? ResultsScreenStrings.NoPPForUnrankedMods : default(LocalisableString)));
        Assert.That(statistic.ChildrenOfType<StatisticCounter>().Single().Current.Value,
            Is.EqualTo((int)Math.Round(pp ?? 0, MidpointRounding.AwayFromZero)));
        Assert.That(score.PP, Is.EqualTo(pp), "Styling must not manufacture a persisted PP result.");
        Assert.That(score.Mods, Is.EqualTo(mods));
    }

    private sealed partial class MissingPerformanceCache : BeatmapDifficultyCache
    {
        public TaskCompletionSource LookupCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task<StarDifficulty?> GetDifficultyAsync(IBeatmapInfo beatmapInfo, IRulesetInfo? rulesetInfo = null,
                                                               IEnumerable<Mod>? mods = null, CancellationToken cancellationToken = default, int computationDelay = 0)
        {
            LookupCompleted.TrySetResult();
            return Task.FromResult<StarDifficulty?>(new StarDifficulty(4, 500));
        }
    }

    [TestCase("", false)]
    [TestCase("NF", false)]
    [TestCase("MR", false)]
    [TestCase("RD", false)]
    [TestCase("AT", false)]
    [TestCase("SD", false)]
    [TestCase("PF", false)]
    [TestCase("NR", false)]
    [TestCase("FI", false)]
    [TestCase("HD", false)]
    [TestCase("CO", false)]
    [TestCase("FL", false)]
    [TestCase("AC", false)]
    [TestCase("IN", false)]
    [TestCase("CS", false)]
    [TestCase("WU", false)]
    [TestCase("WD", false)]
    [TestCase("MU", false)]
    [TestCase("AS", false)]
    [TestCase("NF,CS", false)]
    [TestCase("MS", true)]
    [TestCase("MS,NF", true)]
    [TestCase("MS,MR", true)]
    [TestCase("MS,NF,MR", true)]
    [TestCase("MS,RD", false)]
    [TestCase("MS,NF,RD", false)]
    [TestCase("MS,AT", false)]
    [TestCase("MS,SD", true)]
    [TestCase("MS,PF", true)]
    [TestCase("MS,HT", true)]
    [TestCase("MS,DC", true)]
    [TestCase("MS,DT", true)]
    [TestCase("MS,NC", true)]
    [TestCase("MS,NR", false)]
    [TestCase("MS,FI", true)]
    [TestCase("MS,HD", true)]
    [TestCase("MS,CO", true)]
    [TestCase("MS,FL", true)]
    [TestCase("MS,AC", true)]
    [TestCase("MS,IN", false)]
    [TestCase("MS,CS", false)]
    [TestCase("MS,WU", false)]
    [TestCase("MS,WD", false)]
    [TestCase("MS,MU", true)]
    [TestCase("MS,AS", false)]
    [TestCase("MS,SD,CS", false)]
    [TestCase("MS,NF,CS", false)]
    public void NativeModFooterAndStoredScoresUseSelectionEligibility(string selection, bool expected)
    {
        var ruleset = new O2LazerRuleset();
        Assert.That(O2JamPerformanceEligibilityPatch.IsInstalled, Is.True);
        var mods = createMods(ruleset, selection);
        var originalRankedFlags = mods.Select(mod => mod.Ranked).ToArray();
        var selectedMods = new Bindable<IReadOnlyList<Mod>>(mods);
        using var overlay = new ModSelectOverlay();
        using var footer = new ModSelectFooterContent(overlay);
        using var display = new RankingInformationDisplay();
        footer.ActiveMods.BindTo(selectedMods);
        footer.Ruleset.BindTo(new Bindable<RulesetInfo?>(ruleset.RulesetInfo));
        footer.Beatmap.BindTo(new Bindable<WorkingBeatmap?>(new FlatWorkingBeatmap(new Beatmap())));
        setField(footer, "rankingInformationDisplay", display);

        invoke(footer, "updateInformation");
        var score = new ScoreInfo(ruleset: ruleset.RulesetInfo) { Mods = mods };

        Assert.Multiple(() =>
        {
            Assert.That(display.Ranked.Value, Is.EqualTo(expected));
            Assert.That(display.ModMultiplier.Value,
                Is.EqualTo(ruleset.CreateScoreMultiplierCalculator(new ScoreMultiplierContext(new BeatmapDifficulty())).CalculateFor(mods)));
            Assert.That(O2JamPerformanceEligibility.IsEligible(mods), Is.EqualTo(expected));
            Assert.That(hasUnrankedMods(typeof(PerformanceStatistic), score), Is.EqualTo(!expected));
            Assert.That(hasUnrankedMods(typeof(BeatmapLeaderboardScore.LeaderboardScoreTooltip.PerformanceStatisticRow), score), Is.EqualTo(!expected));
            Assert.That(mods.Select(mod => mod.Ranked), Is.EqualTo(originalRankedFlags));
            Assert.That(score.Mods.Select(mod => mod.Acronym), Is.EqualTo(mods.Select(mod => mod.Acronym)));
            Assert.That(ruleset.CreatePerformanceCalculator(), Is.Null);
        });
    }

    [Test]
    public void SwitchingMsAndRulesetsDoesNotAffectAnotherSelectionOrStoredScore()
    {
        var ruleset = new O2LazerRuleset();
        var mania = new ManiaRuleset();
        var selectedRuleset = new Bindable<RulesetInfo?>(ruleset.RulesetInfo);
        var selectedMods = new Bindable<IReadOnlyList<Mod>>([]);
        using var overlay = new ModSelectOverlay();
        using var footer = new ModSelectFooterContent(overlay);
        using var display = new RankingInformationDisplay();
        footer.Ruleset.BindTo(selectedRuleset);
        footer.ActiveMods.BindTo(selectedMods);
        footer.Beatmap.BindTo(new Bindable<WorkingBeatmap?>(new FlatWorkingBeatmap(new Beatmap())));
        setField(footer, "rankingInformationDisplay", display);
        var stored = new ScoreInfo(ruleset: ruleset.RulesetInfo) { Mods = [] };
        var nativeScore = new ScoreInfo(ruleset: mania.RulesetInfo) { Mods = [] };

        check([], false);
        check([new O2JamModManiaScore(), new O2JamModNoFail()], true);
        Assert.That(hasUnrankedMods(typeof(PerformanceStatistic), stored), Is.True);
        Assert.That(hasUnrankedMods(typeof(PerformanceStatistic), nativeScore), Is.False);
        Assert.That(O2JamPerformanceEligibility.IsEligible([]), Is.False);
        check([new O2JamModNoFail()], false);
        check([new O2JamModManiaScore(), new O2JamModRandom()], false);
        check([new O2JamModManiaScore()], true);
        check([], false);

        selectedRuleset.Value = mania.RulesetInfo;
        check([], true);
        check([new ManiaModNoFail(), new ManiaModMirror()], true);
        check([new ManiaModRandom()], false);

        selectedRuleset.Value = new RulesetInfo { ShortName = "another-custom-ruleset" };
        // A null beatmap avoids needing that deliberately unregistered ruleset's calculator.
        footer.Beatmap.UnbindAll();
        footer.Beatmap.BindTo(new Bindable<WorkingBeatmap?>(null));
        display.Ranked.Value = true;
        check([], true);

        selectedRuleset.Value = ruleset.RulesetInfo;
        check([], false);
        check([new O2JamModManiaScore(), new O2JamModMirror()], true);

        void check(Mod[] mods, bool expected)
        {
            selectedMods.Value = mods;
            invoke(footer, "updateInformation");
            Assert.That(display.Ranked.Value, Is.EqualTo(expected));
        }
    }

    private static Mod[] createMods(O2LazerRuleset ruleset, string selection)
    {
        var available = ruleset.CreateAllMods().ToDictionary(mod => mod.Acronym);
        return selection.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(acronym => available[acronym]).ToArray();
    }

    [Test]
    public void UnrankedModChangesNeverPublishAnIntermediateRankedState()
    {
        var o2lazer = new O2LazerRuleset();
        var selectedRuleset = new Bindable<RulesetInfo?>(new ManiaRuleset().RulesetInfo);
        var selectedMods = new Bindable<IReadOnlyList<Mod>>([new ManiaModRandom()]);
        using var overlay = new ModSelectOverlay();
        using var footer = new ModSelectFooterContent(overlay);
        using var display = new RankingInformationDisplay();
        footer.Ruleset.BindTo(selectedRuleset);
        footer.ActiveMods.BindTo(selectedMods);
        footer.Beatmap.BindTo(new Bindable<WorkingBeatmap?>(new FlatWorkingBeatmap(new Beatmap())));
        setField(footer, "rankingInformationDisplay", display);
        invoke(footer, "updateInformation");
        Assert.That(display.Ranked.Value, Is.False);

        var transitions = new List<bool>();
        display.Ranked.BindValueChanged(change => transitions.Add(change.NewValue));
        selectedRuleset.Value = o2lazer.RulesetInfo;
        invoke(footer, "updateInformation");
        foreach (var selection in new[] { "", "MR", "PF", "NF", "CS", "RD", "", "MS,RD", "MS,CS", "" })
        {
            selectedMods.Value = createMods(o2lazer, selection);
            invoke(footer, "updateInformation");
            Assert.That(transitions, Is.Empty, $"{selection} must not restart the native ranking flash.");
        }

        selectedMods.Value = createMods(o2lazer, "MS");
        invoke(footer, "updateInformation");
        selectedMods.Value = [];
        invoke(footer, "updateInformation");
        Assert.That(transitions, Is.EqualTo(new[] { true, false }), "Real eligibility changes still reach the native animation.");
    }

    private static bool hasUnrankedMods(Type owner, ScoreInfo score) =>
        (bool)owner.GetMethod("hasUnrankedMods", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [score])!;

    private static void invoke(object target, string method) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);

    private static void setField(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

}
