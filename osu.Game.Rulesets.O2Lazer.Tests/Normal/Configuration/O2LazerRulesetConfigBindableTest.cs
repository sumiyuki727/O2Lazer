using System.Collections.Generic;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Game.Rulesets.O2Lazer;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.UI;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Configuration;

[TestFixture]
public class O2LazerRulesetConfigBindableTest
{
    [Test]
    public void TestBindableChangeNotifiesSubscribers()
    {
        var ruleset = new O2LazerRuleset();
        var config = new O2LazerRulesetConfigManager(null, ruleset.RulesetInfo);
        var bindable = config.GetBindable<O2LazerScrollingDirection>(O2LazerRulesetSetting.ScrollDirection);

        var received = new List<O2LazerScrollingDirection>();
        bindable.BindValueChanged(e => received.Add(e.NewValue));

        bindable.Value = O2LazerScrollingDirection.Up;
        bindable.Value = O2LazerScrollingDirection.Down;

        Assert.That(received, Is.EqualTo(new[] { O2LazerScrollingDirection.Up, O2LazerScrollingDirection.Down }));
    }

    [Test]
    public void TestSeparateGetBindableCallsStayInSync()
    {
        var ruleset = new O2LazerRuleset();
        var config = new O2LazerRulesetConfigManager(null, ruleset.RulesetInfo);

        var first = config.GetBindable<O2LazerScrollingDirection>(O2LazerRulesetSetting.ScrollDirection);
        var second = config.GetBindable<O2LazerScrollingDirection>(O2LazerRulesetSetting.ScrollDirection);

        var received = new List<O2LazerScrollingDirection>();
        second.BindValueChanged(e => received.Add(e.NewValue));

        first.Value = O2LazerScrollingDirection.Up;

        Assert.Multiple(() =>
        {
            Assert.That(second.Value, Is.EqualTo(O2LazerScrollingDirection.Up));
            Assert.That(received, Is.EqualTo(new[] { O2LazerScrollingDirection.Up }));
        });
    }

    [Test]
    public void TestConfigBindWithNotifiesBoundBindable()
    {
        var ruleset = new O2LazerRuleset();
        var config = new O2LazerRulesetConfigManager(null, ruleset.RulesetInfo);
        var bound = new Bindable<O2LazerScrollingDirection>();
        config.BindWith(O2LazerRulesetSetting.ScrollDirection, bound);

        var received = new List<O2LazerScrollingDirection>();
        bound.BindValueChanged(e => received.Add(e.NewValue));

        config.GetBindable<O2LazerScrollingDirection>(O2LazerRulesetSetting.ScrollDirection).Value = O2LazerScrollingDirection.Up;

        Assert.Multiple(() =>
        {
            Assert.That(bound.Value, Is.EqualTo(O2LazerScrollingDirection.Up));
            Assert.That(received, Is.EqualTo(new[] { O2LazerScrollingDirection.Up }));
        });
    }
}
