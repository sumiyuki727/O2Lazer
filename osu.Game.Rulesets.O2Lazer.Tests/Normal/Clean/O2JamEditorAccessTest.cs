using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Overlays;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Screens;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public partial class O2JamEditorAccessTest
{
    [TestCase(true, false, true)]
    [TestCase(false, true, true)]
    [TestCase(true, true, true)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(false, false, true)]
    [TestCase(false, false, false)]
    public void EditorPushIsRejectedBeforeSuspendingOrLoading(bool o2Mode, bool o2Beatmap, bool loader)
    {
        var o2lazer = new O2LazerRuleset().RulesetInfo;
        var mania = new ManiaRuleset().RulesetInfo;
        Assert.That(O2JamEditorAccessPatch.IsInstalled, Is.True);
        using var source = new ProbeScreen();
        bind(source, o2Mode ? o2lazer : mania, o2Beatmap ? o2lazer : mania);
        using var stack = new ScreenStack();
        stack.Push(source);
        using var music = new MusicController();
        using OsuScreen target = loader ? new EditorLoader() : createEditor(music);
        stack.Push(target);

        Assert.That(stack.CurrentScreen, Is.SameAs(o2Mode || o2Beatmap ? source : target));
        Assert.That(target.IsLoaded, Is.False);
    }

    [Test]
    public void O2LazerCanStillPushOtherScreens()
    {
        var ruleset = new O2LazerRuleset().RulesetInfo;
        using var source = new ProbeScreen();
        bind(source, ruleset, ruleset);
        using var stack = new ScreenStack();
        stack.Push(source);
        using var target = new ProbeScreen();
        stack.Push(target);
        Assert.That(stack.CurrentScreen, Is.SameAs(target));
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public void SongSelectDisablesOnlyTheEditorAction(bool o2Mode, bool o2Beatmap)
    {
        var o2lazer = new O2LazerRuleset().RulesetInfo;
        var mania = new ManiaRuleset().RulesetInfo;
        using var screen = new SoloSongSelect();
        bind(screen, o2Mode ? o2lazer : mania, o2Beatmap ? o2lazer : mania);
        var actions = screen.GetForwardActions(screen.Beatmap.Value.BeatmapInfo).Take(2).ToArray();
        Assert.That(actions[0].Action.Disabled, Is.False);
        Assert.That(actions[1].Action.Disabled, Is.EqualTo(o2Mode || o2Beatmap));
    }

    [Test]
    public void AnExistingNativeEditorCannotCreateOrSwitchToO2LazerDifficulties()
    {
        var o2lazer = new O2LazerRuleset().RulesetInfo;
        var mania = new ManiaRuleset().RulesetInfo;
        using var music = new MusicController();
        using var editor = createEditor(music);
        bind(editor, mania, mania);
        typeof(Editor).GetMethod("CreateNewDifficulty", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, [o2lazer]);
        editor.SwitchToDifficulty(new BeatmapInfo(o2lazer));
        var switching = typeof(Editor).GetField("switchingDifficulty", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.That(switching.GetValue(editor), Is.False, "Reject before native switching state or database mutations.");
        editor.SwitchToDifficulty(new BeatmapInfo(mania));
        Assert.That(switching.GetValue(editor), Is.True);
    }

    private static void bind(OsuScreen screen, RulesetInfo mode, RulesetInfo beatmapRuleset)
    {
        typeof(OsuScreen).GetProperty(nameof(OsuScreen.Ruleset))!.SetValue(screen, new Bindable<RulesetInfo>(mode));
        typeof(OsuScreen).GetProperty(nameof(OsuScreen.Beatmap))!.SetValue(screen,
            new Bindable<WorkingBeatmap>(new FlatWorkingBeatmap(new Beatmap { BeatmapInfo = new BeatmapInfo(beatmapRuleset) })));
    }

    private static Editor createEditor(MusicController music)
    {
        var editor = new Editor();
        // The unloaded editor's disposal still unsubscribes from its resolved controller.
        typeof(Editor).GetProperty("musicController", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(editor, music);
        return editor;
    }

    private partial class ProbeScreen : OsuScreen;
}
