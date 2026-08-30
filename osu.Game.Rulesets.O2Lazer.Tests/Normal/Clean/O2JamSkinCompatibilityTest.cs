using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Skinning;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Screens.Play;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamSkinCompatibilityTest
{
    [Test]
    public void PercyExtensionBeginsOnlyAfterNativeSpan()
    {
        Assert.Multiple(() =>
        {
            Assert.That(O2JamLegacyHoldBodyPiece.ComputeExtensionHeights(32800), Is.Empty);
            Assert.That(O2JamLegacyHoldBodyPiece.ComputeExtensionHeights(32801), Is.EqualTo(new[] { 1f }));
            Assert.That(O2JamLegacyHoldBodyPiece.ComputeExtensionHeights(65601),
                Is.EqualTo(new[] { 16400f, 16400f, 1f }));
        });
    }

    [Test]
    public void PercyExtensionSupportsAnimatedBodyFrameZeroLookup()
    {
        Assert.That(O2JamLegacyHoldBodyPiece.AnimationFrameName("mania-note1L", 0),
            Is.EqualTo("mania-note1L-0"));
    }

    [Test]
    public void CompatibilitySettingsHaveLocalisedLabels()
    {
        Assert.Multiple(() =>
        {
            Assert.That(O2LazerStrings.O2JamLongNoteVisualDescription.ToString(), Is.Not.Empty);
            Assert.That(O2LazerStrings.PercyLongNoteBodyRepeat.ToString(), Is.Not.Empty);
            Assert.That(O2LazerStrings.PercyLongNoteBodyRepeatDescription.ToString(), Is.Not.Empty);
        });
    }

    [Test]
    public void ProvidesO2JamHudAndPlayfieldEditorLayers()
    {
        var ruleset = new O2LazerRuleset();
        using var drawableRuleset = new O2JamDrawableRuleset(
            ruleset,
            new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120)));
        using var overlay = new HUDOverlay(drawableRuleset, [], new PlayerConfiguration());
        var layers = overlay.ChildrenOfType<SkinnableContainer>()
                            .Select(container => container.Lookup)
                            .Where(lookup => lookup.Ruleset?.Equals(ruleset.RulesetInfo) == true)
                            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(layers.Count(lookup => lookup.Lookup == GlobalSkinnableContainers.MainHUDComponents), Is.EqualTo(1));
            Assert.That(layers.Count(lookup => lookup.Lookup == GlobalSkinnableContainers.Playfield), Is.EqualTo(1));
            Assert.That(layers, Has.All.Matches<GlobalSkinnableContainerLookup>(lookup => lookup.ToString().Contains("O2Jam", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void ExposesO2JamComponentsAndKeepsOldLayoutTypesResolvable()
    {
        var ruleset = new O2LazerRuleset().RulesetInfo;
        var availableComponents = SerialisedDrawableInfo.GetAllAvailableDrawables(ruleset);
        var oldCombo = Type.GetType("osu.Game.Rulesets.O2Lazer.UI.HudComponents.O2LazerComboCounter, osu.Game.Rulesets.O2Lazer");
        var oldJudgement = Type.GetType("osu.Game.Rulesets.O2Lazer.UI.HudComponents.O2LazerJudgementDisplay, osu.Game.Rulesets.O2Lazer");
        var oldComboInstance = (ISerialisableDrawable)Activator.CreateInstance(oldCombo!)!;
        var oldJudgementInstance = (ISerialisableDrawable)Activator.CreateInstance(oldJudgement!)!;

        Assert.Multiple(() =>
        {
            Assert.That(availableComponents, Does.Contain(typeof(O2JamComboCounter)));
            Assert.That(oldCombo, Is.Not.Null);
            Assert.That(oldJudgement, Is.Not.Null);
            Assert.That(typeof(ISerialisableDrawable).IsAssignableFrom(oldCombo!), Is.True);
            Assert.That(typeof(ISerialisableDrawable).IsAssignableFrom(oldJudgement!), Is.True);
            Assert.That(oldComboInstance.IsEditable, Is.False);
            Assert.That(oldJudgementInstance.IsEditable, Is.False);
        });
    }

}
