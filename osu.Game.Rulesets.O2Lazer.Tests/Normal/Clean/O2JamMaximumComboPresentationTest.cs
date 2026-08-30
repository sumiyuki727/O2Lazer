using NUnit.Framework;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamMaximumComboPresentationTest
{
    [TestCase(0, 0)]
    [TestCase(1, 0)]
    [TestCase(4, 3)]
    [TestCase(2410, 2409)]
    public void AchievableComboUsesFirstHitZeroConvention(int endpoints, int expected)
    {
        var score = new ScoreInfo(ruleset: new O2LazerRuleset().RulesetInfo)
        {
            MaximumStatistics = new() { [HitResult.Perfect] = endpoints, [HitResult.IgnoreHit] = 1736 },
            MaxCombo = 10,
        };

        Assert.Multiple(() =>
        {
            Assert.That(score.GetMaximumAchievableCombo(), Is.EqualTo(expected));
            Assert.That(score.GetMaximumAchievableCombo(), Is.EqualTo(expected), "Repeated display must not subtract twice.");
            Assert.That(score.MaxCombo, Is.EqualTo(10), "The earned maximum combo is already in the correct convention.");
            Assert.That(score.MaximumStatistics[HitResult.Perfect], Is.EqualTo(endpoints));
        });
    }

    [TestCase("mania")]
    [TestCase("bms")]
    public void OtherRulesetsKeepNativeMaximumCombo(string shortName)
    {
        _ = new O2LazerRuleset();
        var score = new ScoreInfo(ruleset: new RulesetInfo { ShortName = shortName })
        {
            MaximumStatistics = new() { [HitResult.Perfect] = 2410, [HitResult.IgnoreHit] = 1736 },
        };
        Assert.That(score.GetMaximumAchievableCombo(), Is.EqualTo(2410));
    }
}
