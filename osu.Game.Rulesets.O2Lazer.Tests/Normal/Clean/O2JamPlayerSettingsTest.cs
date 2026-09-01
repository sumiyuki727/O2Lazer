using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Localisation;
using osu.Game.Rulesets;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.PlayerSettings;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public partial class O2JamPlayerSettingsTest
{
    [SetUp]
    public void SetUp()
    {
        _ = new O2LazerRuleset();
        Assert.That(O2JamPlayerSettingsPatch.IsInstalled, Is.True);
    }

    [TestCase("player", true)]
    [TestCase("replay-loader", true)]
    [TestCase("replay-player", true)]
    [TestCase("player", false)]
    [TestCase("replay-loader", false)]
    [TestCase("replay-player", false)]
    public void NativeGroupsHideOnlyUnsupportedO2JamSettings(string page, bool o2jam)
    {
        using var storage = new TemporaryNativeStorage($"O2JamPlayerSettings-{Guid.NewGuid():N}");
        using var config = new OsuConfigManager(storage);
        using var screen = createScreen(page, o2jam);
        using var visual = new VisualSettings();
        using var audio = new AudioSettings();
        using var input = new InputSettings();

        config.SetValue(OsuSetting.ShowStoryboard, true);
        config.SetValue(OsuSetting.BeatmapSkins, true);
        config.SetValue(OsuSetting.BeatmapColours, true);
        config.SetValue(OsuSetting.BeatmapHitsounds, true);
        config.SetValue(OsuSetting.MouseDisableButtons, true);
        config.SetValue(OsuSetting.TouchDisableGameplayTaps, true);
        config.SetValue(OsuSetting.ComboColourNormalisationAmount, 0.37f);

        finishNativeGroup(visual, screen, config);
        finishNativeGroup(audio, screen, config);
        finishNativeGroup(input, screen, config);

        Assert.Multiple(() =>
        {
            Assert.That(visual.Children.Count(child => child.IsPresent), Is.EqualTo(o2jam ? 2 : 6));
            Assert.That(visual.Children.OfType<PlayerSliderBar<double>>().All(slider => slider.IsPresent), Is.True);
            Assert.That(audio.Children.OfType<BeatmapOffsetControl>().Single().IsPresent, Is.True);
            Assert.That(audio.Children.OfType<PlayerCheckbox>().Single().IsPresent, Is.EqualTo(!o2jam));
            Assert.That(input.Children.Single().IsPresent, Is.EqualTo(!o2jam));
            Assert.That(input.IsPresent, Is.EqualTo(!o2jam), "Do not leave an empty input section behind.");
            Assert.That(visual.IsPresent && audio.IsPresent, Is.True);
            Assert.That(visual.Children.OfType<PlayerCheckbox>().All(checkbox => checkbox.CanBeShown.Value == !o2jam), Is.True);
            Assert.That(visual.Children.OfType<PlayerSliderBar<float>>().Single().CanBeShown.Value, Is.EqualTo(!o2jam));
            Assert.That(config.Get<bool>(OsuSetting.ShowStoryboard), Is.True);
            Assert.That(config.Get<bool>(OsuSetting.BeatmapSkins), Is.True);
            Assert.That(config.Get<bool>(OsuSetting.BeatmapColours), Is.True);
            Assert.That(config.Get<bool>(OsuSetting.BeatmapHitsounds), Is.True);
            Assert.That(config.Get<bool>(OsuSetting.MouseDisableButtons), Is.True);
            Assert.That(config.Get<bool>(OsuSetting.TouchDisableGameplayTaps), Is.True);
            Assert.That(config.Get<float>(OsuSetting.ComboColourNormalisationAmount), Is.EqualTo(0.37f));
        });

        // Existing native bindings must survive hiding, including edits from the global settings.
        config.SetValue(OsuSetting.BeatmapHitsounds, false);
        var hitsounds = audio.Children.OfType<PlayerCheckbox>().Single();
        Assert.That(hitsounds.Current.Value, Is.False);
        Assert.That(hitsounds.IsPresent, Is.EqualTo(!o2jam));
    }

    [Test]
    public void UnrelatedPagesAndUnparentedGroupsKeepTheirSettings()
    {
        using var screen = new OtherScreen();
        setRuleset(screen, true);
        using var visual = new VisualSettings();
        using var audio = new AudioSettings();

        O2JamPlayerSettingsPatch.Apply(visual, screen);
        O2JamPlayerSettingsPatch.Apply(audio, null);

        Assert.That(visual.Children.Concat(audio.Children).All(child => child.IsPresent), Is.True);
    }

    [Test]
    public void AdditionalInputControlsArePreservedAlongWithTheirSection()
    {
        using var screen = createScreen("player", true);
        using var input = new InputSettings
        {
            Children =
            [
                new PlayerCheckbox { LabelText = MouseSettingsStrings.DisableClicksDuringGameplay },
                new PlayerCheckbox { LabelText = "Another setting" },
            ],
        };

        O2JamPlayerSettingsPatch.Apply(input, screen);

        Assert.Multiple(() =>
        {
            Assert.That(input.Children.First().IsPresent, Is.False);
            Assert.That(input.Children.Last().IsPresent, Is.True);
            Assert.That(input.IsPresent, Is.True);
        });
    }

    [Test]
    public void DoesNotAttachToOtherNativeSettingsGroups()
    {
        using var group = new PlayerSettingsGroup("Other settings");
        Assert.That(typeof(Drawable).GetField("OnLoadComplete", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(group), Is.Null);
    }

    private static OsuScreen createScreen(string page, bool o2jam)
    {
        var score = new Score { ScoreInfo = new ScoreInfo(ruleset: rulesetInfo(o2jam)) };
        OsuScreen screen = page switch
        {
            "replay-loader" => new ReplayPlayerLoader(score),
            "replay-player" => new ReplayPlayer(score),
            _ => new PlayerLoader(() => throw new AssertionException("No player or chart should be loaded by a settings test.")),
        };

        // Replay loaders apply Score.Ruleset on entering, which can follow group loading.
        setRuleset(screen, page == "replay-loader" ? !o2jam : o2jam);
        return screen;
    }

    private static RulesetInfo rulesetInfo(bool o2jam) => new() { ShortName = o2jam ? O2LazerIdentity.ShortName : "mania" };

    private static void setRuleset(OsuScreen screen, bool o2jam) =>
        typeof(OsuScreen).GetProperty(nameof(OsuScreen.Ruleset))!.SetValue(screen, new Bindable<RulesetInfo>(rulesetInfo(o2jam)));

    private static void finishNativeGroup(PlayerSettingsGroup group, OsuScreen screen, OsuConfigManager config)
    {
        // Exercise the native binding setup and the constructor-installed event without loading
        // a Player, decoding a chart, or subscribing BeatmapOffsetControl to a user's database.
        object[] args = group is AudioSettings ? [config, new SessionStatics()] : [config];
        group.GetType().GetMethod("load", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(group, args);
        typeof(Drawable).GetProperty(nameof(Drawable.Parent))!.SetValue(group, screen);
        var onLoaded = (Action<Drawable>?)typeof(Drawable).GetField("OnLoadComplete", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(group);
        Assert.That(onLoaded, Is.Not.Null, "The single native constructor hook must attach the visibility adapter.");
        onLoaded!(group);
    }

    private partial class OtherScreen : OsuScreen;
}
