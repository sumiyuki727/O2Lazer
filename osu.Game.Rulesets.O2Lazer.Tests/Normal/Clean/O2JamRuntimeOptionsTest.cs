using System;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Configuration;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
[Category("Isolated")]
public class O2JamRuntimeOptionsTest
{
    [TestCase(O2JamRulesetSetting.O2JamStyleDroppedHold)]
    [TestCase(O2JamRulesetSetting.PercyLongNoteBodyRepeat)]
    [Explicit("Forces process-wide GC; run in isolation from native Realm scheduler/lifetime tests.")]
    public void RuntimeProjectionSurvivesGarbageCollection(O2JamRulesetSetting setting)
    {
        using var config = new O2JamRulesetConfigManager(null, new RulesetInfo { ShortName = O2LazerIdentity.ShortName });
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        config.SetValue(setting, true);
        try
        {
            Assert.That(runtimeValue(), Is.True, "Changing the setting must still reach gameplay after the bound-copy subscribers have been collected.");
        }
        finally
        {
            config.SetValue(setting, false);
        }
        Assert.That(runtimeValue(), Is.False);

        bool runtimeValue() => setting == O2JamRulesetSetting.O2JamStyleDroppedHold
            ? O2JamRuntimeOptions.UseO2JamLongNoteMissVisual
            : O2JamRuntimeOptions.UsePercyLongNoteBodyRepeat;
    }
}
