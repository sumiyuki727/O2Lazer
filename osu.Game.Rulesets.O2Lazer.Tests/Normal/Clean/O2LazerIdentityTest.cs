using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Bindings;
using osu.Framework.IO.Stores;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.SongSelect;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Rulesets.O2Lazer.UI.Icons;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2LazerIdentityTest
{
    [Test]
    public void ReleaseVersionsRemainConsistent()
    {
        var assembly = typeof(O2LazerRuleset).Assembly;
        var version = assembly.GetName().Version!;

        Assert.Multiple(() =>
        {
            Assert.That(version, Is.EqualTo(new Version(1, 0, 0, 0)));
            Assert.That(assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version, Is.EqualTo(version.ToString()));
            Assert.That(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0],
                Is.EqualTo(version.ToString(3)));
        });
    }

    [Test]
    public void PreservesRulesetIdentity()
    {
        var ruleset = new O2LazerRuleset();

        Assert.Multiple(() =>
        {
            Assert.That(typeof(O2LazerRuleset).Assembly.GetName().Name, Is.EqualTo("osu.Game.Rulesets.O2Lazer"));
            Assert.That(typeof(O2LazerRuleset).FullName, Is.EqualTo("osu.Game.Rulesets.O2Lazer.O2LazerRuleset"));
            Assert.That(ruleset.ShortName, Is.EqualTo("o2lazer"));
            Assert.That(ruleset.AvailableVariants, Is.EqualTo(new[] { 207 }));
            Assert.That(ruleset, Is.Not.InstanceOf<ILegacyRuleset>());
            Assert.That(ruleset.RulesetInfo.OnlineID, Is.EqualTo(-1));
            Assert.That(O2JamWorkingBeatmapHook.IsInstalled, Is.True);
            Assert.That(O2JamDifficultyIconPatch.IsInstalled, Is.True);
            Assert.That(O2JamComboCompatibilityPatches.IsInstalled, Is.True);
            Assert.That(O2JamSongSelectRankPatch.IsInstalled, Is.True);
            Assert.That(O2JamBeatmapBoundaryPatches.IsInstalled, Is.True);
        });
    }

    [Test]
    public void RejectsBothDirectionsOfCrossRulesetConversion()
    {
        var o2Ruleset = new O2LazerRuleset().RulesetInfo;
        var maniaRuleset = new ManiaRuleset().RulesetInfo;
        var o2Beatmap = new BeatmapInfo(o2Ruleset);
        var maniaBeatmap = new BeatmapInfo(maniaRuleset);

        Assert.Multiple(() =>
        {
            Assert.That(O2JamBeatmapBoundary.Crosses(maniaBeatmap, o2Ruleset), Is.True);
            Assert.That(O2JamBeatmapBoundary.Crosses(o2Beatmap, maniaRuleset), Is.True);
            Assert.That(O2JamBeatmapBoundary.Crosses(o2Beatmap, o2Ruleset), Is.False);
            Assert.That(new O2JamFilterCriteria().Matches(o2Beatmap, new osu.Game.Screens.Select.FilterCriteria()), Is.True);
            Assert.That(new O2JamFilterCriteria().Matches(maniaBeatmap, new osu.Game.Screens.Select.FilterCriteria()), Is.False);
        });
    }

    [Test]
    public void UsesBundledO2JamRulesetIcon()
    {
        var ruleset = new O2LazerRuleset();
        var resources = typeof(O2LazerRuleset).Assembly.GetManifestResourceNames();

        Assert.Multiple(() =>
        {
            Assert.That(ruleset.CreateIcon(), Is.TypeOf<O2JamRulesetIcon>());
            Assert.That(resources, Does.Contain("osu.Game.Rulesets.O2Lazer.Resources.Textures.Icons.RulesetO2Jam.png"));
        });
    }

    [TestCase("Textures/Icons/RulesetO2Jam")]
    [TestCase("Textures/Icons/Mods/mod-mania-score")]
    public void BundledIconsDecodeAtTheirNativeStyleResourcePaths(string path)
    {
        using var resources = new NamespacedResourceStore<byte[]>(new DllResourceStore(typeof(O2LazerRuleset).Assembly), "Resources");
        using var textures = new TextureLoaderStore(resources);
        using var texture = textures.Get(path);

        Assert.That(texture, Is.Not.Null);
        Assert.That(texture!.Width, Is.GreaterThan(0));
        Assert.That(texture.Height, Is.GreaterThan(0));
    }

    [Test]
    public void DifficultyIconFollowsNativeBeatmapWhenDisplayRulesetIsOverridden()
    {
        var o2Ruleset = new O2LazerRuleset().RulesetInfo;
        var maniaRuleset = new ManiaRuleset().RulesetInfo;

        Assert.Multiple(() =>
        {
            Assert.That(O2JamDifficultyIconPatch.ShouldUseO2JamIcon(maniaRuleset, new BeatmapInfo(o2Ruleset)), Is.True);
            Assert.That(O2JamDifficultyIconPatch.ShouldUseO2JamIcon(o2Ruleset, new BeatmapInfo(maniaRuleset)), Is.True);
            Assert.That(O2JamDifficultyIconPatch.ShouldUseO2JamIcon(maniaRuleset, new BeatmapInfo(maniaRuleset)), Is.False);
        });
    }

    [Test]
    public void SongSelectRulesetIconPrecedesDifficultyDotsAfterReplacement()
    {
        var flow = new FillFlowContainer();
        var nativeIcon = new Box();
        var firstDifficulty = new Box();
        var secondDifficulty = new Box();
        flow.AddRange([nativeIcon, firstDifficulty, secondDifficulty]);

        O2JamDifficultyIconPatch.ReplaceSongSelectRulesetIcon(flow);
        var o2JamIcon = flow.Children.OfType<O2JamRulesetIcon>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(flow.GetLayoutPosition(o2JamIcon), Is.LessThan(flow.GetLayoutPosition(firstDifficulty)));
            Assert.That(flow.GetLayoutPosition(o2JamIcon), Is.LessThan(flow.GetLayoutPosition(secondDifficulty)));
        });
    }

    [Test]
    public void AcceptsNativeRulesetStartupCompatibilityProbe()
    {
        var ruleset = new O2LazerRuleset();
        var converter = ruleset.CreateBeatmapConverter(new Beatmap());

        Assert.Multiple(() =>
        {
            Assert.That(converter.CanConvert(), Is.False);
            Assert.That(() => ruleset.CreateBeatmapProcessor(converter.Convert()), Throws.Nothing);
        });
    }

    [Test]
    public void GameplayLoaderUsesRegisteredRulesetConfigContract()
    {
        var loader = typeof(O2JamDrawableRuleset).GetMethod("load", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(loader, Is.Not.Null);
        Assert.That(loader!.GetParameters().Select(parameter => parameter.ParameterType),
            Is.EqualTo(new[] { typeof(ISkinSource) }));
    }

    [Test]
    public void PreservesActionValuesAndKeyBindingVariant()
    {
        var bindings = new O2LazerRuleset().GetDefaultKeyBindings(207).ToArray();

        Assert.That(bindings.Select(binding => (int)binding.GetAction<ManiaAction>()), Is.EqualTo(Enumerable.Range(0, 7)));
        Assert.That(bindings, Has.Length.EqualTo(7));
        Assert.That(bindings.Select(binding => binding.GetAction<ManiaAction>()), Is.EqualTo(System.Enum.GetValues<ManiaAction>().Take(7)));
    }

    [Test]
    public void CleanAssemblyDoesNotReferenceLegacyPatchDependencies()
    {
        var assembly = typeof(O2LazerRuleset).Assembly;
        var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        var resources = assembly.GetManifestResourceNames();

        Assert.That(references, Does.Not.Contain("0Harmony"));
        Assert.That(references.Any(reference => reference?.Contains("BMS", System.StringComparison.OrdinalIgnoreCase) == true), Is.False);
        Assert.That(resources.Any(resource => resource.Contains(".Skins.")
                                              || resource.Contains(".Fonts.")
                                              || resource.Contains(".Textures.")
                                              && resource != "osu.Game.Rulesets.O2Lazer.Resources.Textures.Icons.RulesetO2Jam.png"
                                              && resource != "osu.Game.Rulesets.O2Lazer.Resources.Textures.Icons.Mods.mod-mania-score.png"), Is.False);
    }
}
