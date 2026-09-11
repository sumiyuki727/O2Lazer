using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Game;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.UI;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public partial class O2JamManiaScoreModDependencyTest
{
    [Test]
    public void DependentModsAreGreyOnlyWhileManiaScoreIsInactive()
    {
        var easy = new O2JamModEasy();

        Assert.That(O2JamManiaScoreModAvailabilityPatch.ShouldDisplayAsDependent(easy, []), Is.True);
        Assert.That(O2JamManiaScoreModAvailabilityPatch.ShouldDisplayAsDependent(easy, [new O2JamModManiaScore()]), Is.False);
        Assert.That(O2JamManiaScoreModAvailabilityPatch.ShouldDisplayAsDependent(new O2JamModNoFail(), []), Is.False);
    }

    [TestCase(typeof(O2JamModEasy))]
    [TestCase(typeof(O2JamModHardRock))]
    [TestCase(typeof(O2JamModClassic))]
    public void SelectingDependentModAlsoSelectsManiaScore(System.Type type)
    {
        var dependent = (Mod)System.Activator.CreateInstance(type)!;
        var maniaScore = new O2JamModManiaScore();
        var result = O2JamManiaScoreModAvailabilityPatch.ApplyDependency([], [dependent], [dependent], maniaScore);

        Assert.That(result, Is.EqualTo(new Mod[] { dependent, maniaScore }));
    }

    [Test]
    public void DeselectingManiaScoreAlsoDeselectsAllDependentMods()
    {
        Mod[] dependents = [new O2JamModEasy(), new O2JamModClassic()];
        var maniaScore = new O2JamModManiaScore();
        var result = O2JamManiaScoreModAvailabilityPatch.ApplyDependency(
            [maniaScore, .. dependents], dependents, dependents, maniaScore);

        Assert.That(result.Any(mod => mod is O2JamModManiaScore or O2JamModEasy or O2JamModClassic), Is.False);
    }

    [TestCase(typeof(ManiaModEasy), typeof(O2JamModEasy))]
    [TestCase(typeof(ManiaModHardRock), typeof(O2JamModHardRock))]
    [TestCase(typeof(ManiaModClassic), typeof(O2JamModClassic))]
    public void RulesetSwitchAddsManiaScoreToTheGlobalSelection(Type sourceType, Type convertedType)
    {
        var o2Lazer = new O2LazerRuleset();
        using var game = new TestOsuGame();
        var mania = new ManiaRuleset().RulesetInfo;
        game.ModSelection.Value = [(Mod)Activator.CreateInstance(sourceType)!];

        typeof(OsuGameBase).GetMethod("onRulesetChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                           .Invoke(game, [new ValueChangedEvent<RulesetInfo>(mania, o2Lazer.RulesetInfo)]);

        Assert.Multiple(() =>
        {
            Assert.That(O2JamManiaScoreRulesetSelectionPatch.IsInstalled, Is.True);
            Assert.That(game.ModSelection.Value.Any(mod => mod.GetType() == convertedType), Is.True);
            Assert.That(game.ModSelection.Value.Count(mod => mod is O2JamModManiaScore), Is.EqualTo(1));
        });
    }

    [Test]
    public void RulesetSwitchAdapterDoesNotAlterOtherRulesets()
    {
        IReadOnlyList<Mod> selection = [new ManiaModClassic()];
        var result = O2JamManiaScoreRulesetSelectionPatch.ApplySelection(selection, [new ManiaModClassic()]);

        Assert.That(result, Is.SameAs(selection));
    }

    [Test]
    public void RemovingDependentModKeepsAutomaticallySelectedManiaScore()
    {
        var maniaScore = new O2JamModManiaScore();
        var oldSelection = new Mod[] { new O2JamModClassic(), maniaScore };
        var result = O2JamManiaScoreModAvailabilityPatch.ApplyDependency(
            oldSelection, [maniaScore], [maniaScore], maniaScore);

        Assert.That(result, Is.EqualTo(new[] { maniaScore }));
    }

    [Test]
    public void GameplayProfileSeparatesPresentationSwitchesFromStarChangingMods()
    {
        Assert.Multiple(() =>
        {
            Assert.That(O2JamGameplayProfile.RequiresStarCalculation(
                [new O2JamModManiaScore(), new O2JamModEasy(), new O2JamModClassic()]), Is.False);
            Assert.That(O2JamGameplayProfile.RequiresStarCalculation([new O2JamModDoubleTime()]), Is.True);
            Assert.That(O2JamGameplayProfile.RequiresStarCalculation([new O2JamModInvert()]), Is.True);
            Assert.That(O2JamGameplayProfile.UsesManiaScore([new O2JamModEasy()]), Is.False,
                "The dependency policy must add MS before gameplay changes profile.");
        });
    }

    private sealed partial class TestOsuGame : OsuGameBase
    {
        public Bindable<IReadOnlyList<Mod>> ModSelection => SelectedMods;
    }
}
