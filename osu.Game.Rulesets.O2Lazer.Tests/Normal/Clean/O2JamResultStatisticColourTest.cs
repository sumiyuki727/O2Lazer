using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Expanded.Statistics;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamResultStatisticColourTest
{
    [TestCase(false, false, HitResult.Meh)]
    [TestCase(false, true, HitResult.Ok)]
    [TestCase(true, false, HitResult.Ok)]
    public void NativeStatisticUsesCanonicalResultColour(bool nativeMania, bool maniaScore, HitResult colourResult)
    {
        var o2lazer = new O2LazerRuleset();
        var score = new ScoreInfo(ruleset: nativeMania ? new ManiaRuleset().RulesetInfo : o2lazer.RulesetInfo)
        {
            Mods = maniaScore ? [new O2JamModManiaScore()] : [],
        };
        var result = nativeMania || maniaScore ? HitResult.Ok : HitResult.Meh;
        score.Statistics[result] = 7;
        var statistic = System.Linq.Enumerable.Single(score.GetStatisticsForDisplay(), item => item.Result == result);
        using var display = new HitResultStatistic(statistic);
        var text = new SpriteText();
        typeof(StatisticDisplay).GetProperty("HeaderText", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(display, text);
        var colours = new OsuColour();
        typeof(HitResultStatistic).GetMethod("load", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(display, [colours]);

        Assert.Multiple(() =>
        {
            Assert.That(text.Colour, Is.EqualTo((osu.Framework.Graphics.Colour.ColourInfo)colours.ForHitResult(colourResult)));
            Assert.That(display.Result, Is.EqualTo(result));
            Assert.That(statistic.Result, Is.EqualTo(result));
            Assert.That(statistic.Count, Is.EqualTo(7));
            Assert.That(score.Statistics[result], Is.EqualTo(7));
            Assert.That(score.Statistics.ContainsKey(result == HitResult.Meh ? HitResult.Ok : HitResult.Meh), Is.False);
        });
    }
}