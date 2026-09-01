using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.IO.Stores;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Audio;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osu.Game.Overlays.Mods;
using osu.Game.Resources;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Screens.Select;
using osu.Game.Utils;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
public partial class O2JamModBadgeLoadedTest
{
    [Test]
    public void LoadedNativeButtonRunsCustomAndNativeTransitions()
        => runProbe();

    [Test]
    public void LoadedNativeButtonRefreshesDoNotRestartCustomTransitions()
        => runProbe(refreshSelection: true);

    [Test]
    public void NativeModSelectionUsesTheLeftLowerTransition()
        => runProbe(throughOverlay: true);

    [Test]
    public void LoadedNativeRankingFooterDoesNotFlashForUnchangedO2JamEligibility()
        => runProbe(throughOverlay: true, checkFooter: true);

    [Test]
    [Explicit("Loads another ruleset's bundled Harmony into an isolated test process.")]
    [Category("LocalDiagnostics")]
    public void LoadedNativeButtonAnimatesWithBmsLoadedFirst()
    {
        loadBms();
        runProbe(refreshSelection: true);
    }

    [Test]
    [Explicit("Loads another ruleset's bundled Harmony into an isolated test process.")]
    [Category("LocalDiagnostics")]
    public void LoadedNativeButtonAnimatesWithBmsLoadedLast()
    {
        _ = new O2LazerRuleset();
        loadBms();
        runProbe(refreshSelection: true);
    }

    private static void loadBms()
    {
        var path = Environment.GetEnvironmentVariable("O2JAM_BMS_RULESET_PATH");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            Assert.Ignore("Set O2JAM_BMS_RULESET_PATH to the installed or separately-built BmsRuleset DLL.");
        var assembly = Assembly.LoadFrom(path!);
        _ = Activator.CreateInstance(assembly.GetType("osu.Game.Rulesets.BmsRuleset.BmsRuleset", throwOnError: true)!);
    }

    private static void runProbe(bool refreshSelection = false, bool throughOverlay = false, bool checkFooter = false)
    {
        using var host = new TestRunHeadlessGameHost($"O2JamModBadge-{Guid.NewGuid():N}");
        var game = new BadgeProbeGame(refreshSelection, throughOverlay, checkFooter);
        host.Run(game);
        foreach (var observation in game.Observations)
            TestContext.Progress.WriteLine(observation);
        if (game.Failure != null)
            throw game.Failure;
        Assert.That(game.Completed, Is.True);
    }

    private partial class BadgeProbeGame : Framework.Game
    {
        private readonly ManualClock sourceClock = new();
        private readonly FramedClock frameClock;
        private readonly bool refreshSelection;
        private readonly bool throughOverlay;
        private readonly bool checkFooter;
        private readonly ModProbeContext gameContext = new();
        private readonly TemporaryNativeStorage storage = new($"O2JamModOverlay-{Guid.NewGuid():N}");
        private readonly OsuConfigManager config;
        private readonly TrackVirtual previewTrack = new(0);
        private readonly PreviewTrackManager previewTracks;
        private readonly OsuMenuSamples menuSamples = new();
        private readonly O2LazerRuleset ruleset = new();
        private readonly ManiaRuleset mania = new();
        private readonly BeatmapDifficultyCache difficultyCache = new();
        private readonly (string From, string To)[] routes;
        private UserModSelectOverlay overlay = null!;
        private ModSelectFooterContent footer = null!;
        private readonly Bindable<IReadOnlyList<Mod>> songMods = new([]);
        private FooterButtonMods button = null!;
        private Drawable badge = null!;
        private float rightMargin;
        private int route;
        private int phase;
        private int frames;
        public Exception? Failure;
        public bool Completed;
        public List<string> Observations { get; } = [];

        public BadgeProbeGame(bool refreshSelection, bool throughOverlay, bool checkFooter)
        {
            this.refreshSelection = refreshSelection;
            this.throughOverlay = throughOverlay;
            this.checkFooter = checkFooter;
            config = new OsuConfigManager(storage);
            previewTracks = new PreviewTrackManager(previewTrack);
            frameClock = new FramedClock(sourceClock);
            typeof(BeatmapDifficultyCache).GetProperty("currentRuleset", BindingFlags.Instance | BindingFlags.NonPublic)!
                                        .SetValue(difficultyCache, new Bindable<RulesetInfo>(ruleset.RulesetInfo));
            typeof(BeatmapDifficultyCache).GetProperty("currentMods", BindingFlags.Instance | BindingFlags.NonPublic)!
                                        .SetValue(difficultyCache, new Bindable<IReadOnlyList<Mod>>([]));
            gameContext.Mode.Value = ruleset.RulesetInfo;
            routes = throughOverlay ? [("", "MS"), ("MS", "")] :
            [
                ("", "NF"), ("NF", ""), ("", "MS"), ("MS", ""),
                ("", "N"), ("N", ""), ("", "NU"), ("NU", ""), ("", "NM"), ("NM", ""),
                ("NF", "MS"), ("MS", "NF"),
            ];
        }

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
            dependencies.CacheAs<OsuGameBase>(gameContext);
            dependencies.Cache(new OsuColour());
            dependencies.Cache(new OverlayColourProvider(OverlayColourScheme.Green));
            dependencies.Cache(new SessionStatics());
            dependencies.Cache(config);
            dependencies.Cache(previewTracks);
            dependencies.Cache(menuSamples);
            dependencies.Cache(difficultyCache);
            dependencies.CacheAs<IBeatSyncProvider>(new SilentBeatSync(frameClock));
            dependencies.CacheAs<IBindable<WorkingBeatmap>>(new Bindable<WorkingBeatmap>(new FlatWorkingBeatmap(new Beatmap())));
            return dependencies;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            if (throughOverlay)
            {
                Resources.AddStore(new DllResourceStore(OsuResources.ResourceAssembly));
                Add(menuSamples);
                gameContext.AvailableMods.Value = Enum.GetValues<ModType>().ToDictionary(type => type, type => (IReadOnlyList<Mod>)ruleset.GetModsFor(type).ToArray());
                songMods.BindTo(gameContext.ModSelection);
                Add(overlay = new UserModSelectOverlay
                {
                    Clock = frameClock,
                    State = { Value = Visibility.Visible },
                    Ruleset = { Value = ruleset.RulesetInfo },
                    SelectedMods = { BindTarget = songMods },
                });
                if (checkFooter)
                {
                    footer = (ModSelectFooterContent)overlay.CreateFooterContent();
                    footer.Clock = frameClock;
                    Add(footer);
                    footer.Show();
                }
            }
            Add(new Container
            {
                Clock = frameClock,
                RelativeSizeAxes = Axes.Both,
                Child = button = new FooterButtonMods(null!)
                {
                    X = 100,
                    Y = 100,
                    Ruleset = { Value = ruleset.RulesetInfo },
                },
            });
            if (throughOverlay)
                button.Mods = songMods;
            button.Mods.BindValueChanged(e => Observations.Add($"mods event {string.Join(',', e.NewValue.Select(m => m.Acronym))}, actual={string.Join(',', button.Mods.Value.Select(m => m.Acronym))}"));
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();
            if (Completed || Failure != null)
                return;

            try
            {
                if (++frames > 150)
                    throw new InvalidOperationException($"Native badge probe did not complete: button={button.LoadState}, phase={phase}, route={route}, footer={footer?.LoadState}, display={footer?.ChildrenOfType<RankingInformationDisplay>().SingleOrDefault()?.LoadState}.");
                if (!button.IsLoaded)
                    return;
                if (throughOverlay && !overlay.IsLoaded)
                    return;

                if (checkFooter)
                {
                    var display = footer.ChildrenOfType<RankingInformationDisplay>().SingleOrDefault();
                    if (display?.IsLoaded != true)
                    {
                        advance(20);
                        return;
                    }
                    checkRankingFooter(display);
                    Completed = true;
                    Exit();
                    return;
                }

                badge ??= getDrawable(button, "unrankedBadge");
                if (!badge.IsLoaded)
                    return;

                var (from, to) = routes[route];
                switch (phase++)
                {
                    case 0:
                        rightMargin = getDrawable(button, "modDisplayBar").Width + 5;
                        Assert.That(badge.Clock, Is.SameAs(frameClock), "The badge must inherit the real parent clock.");
                        select(from);
                        break;

                    case 1:
                        advance(500);
                        break;

                    case 2:
                        observe("before");
                        select(to);
                        break;

                    case 3:
                        observe("selected");
                        if (from is "MS" or "N" or "NM" && to == "")
                        {
                            Assert.That(badge.X, Is.EqualTo(-rightMargin), "Relocate to LeftLower before starting the upward fade.");
                            Assert.That(badge.Y, Is.EqualTo(20));
                            Assert.That(badge.Alpha, Is.Zero, "The jump to LeftLower must remain hidden.");
                        }
                        advance(60);
                        break;

                    case 4:
                        observe("60ms");
                        if ((from == "" || to == "") && (from is "NF" or "NU" || to is "NF" or "NU"))
                        {
                            Assert.That(badge.X, Is.GreaterThan(-rightMargin).And.LessThan(0));
                            Assert.That(badge.Y, Is.EqualTo(-5));
                            Assert.That(badge.Alpha, Is.EqualTo(1));
                            var progress = (badge.X + rightMargin) / rightMargin;
                            Assert.That(button.Width, Is.EqualTo(rightMargin - 5 + progress * (5 + badge.DrawWidth)).Within(0.001));
                        }
                        else if (from == "" || to == "")
                        {
                            Assert.That(badge.Alpha, Is.GreaterThan(0).And.LessThan(1));
                            Assert.That(badge.Y, Is.GreaterThan(-5).And.LessThan(20));
                            Assert.That(badge.X, Is.EqualTo(-rightMargin),
                                "Only relocate horizontally while hidden, never before or during a vertical fade.");
                        }
                        else
                        {
                            Assert.That(badge.Y, Is.EqualTo(-5), "Native ranked/unranked changes stay at the upper Y position.");
                            Assert.That(badge.X, Is.GreaterThan(-badge.DrawWidth).And.LessThan(0));
                            Assert.That(badge.Alpha, Is.GreaterThan(0).And.LessThan(1));
                        }
                        if (refreshSelection && (from == "" || to == ""))
                            button.Mods.TriggerChange();
                        advance(179);
                        break;

                    case 5:
                        if (from == "" && to is "MS" or "N" or "NM")
                            Assert.That(badge.X, Is.EqualTo(-rightMargin), "Remain in the left column until the downward fade completes.");
                        advance(1);
                        break;

                    case 6:
                        observe("240ms");
                        Assert.That(badge.Margin.Left, Is.EqualTo(rightMargin));
                        if (to != "N")
                            Assert.That(badge.X, Is.EqualTo(to == "" ? -rightMargin : to is "MS" or "NM" ? -badge.DrawWidth : 0));
                        Assert.That(badge.Y, Is.EqualTo(to == "N" ? 20 : -5));
                        Assert.That(badge.Alpha, Is.EqualTo(to is "MS" or "NM" or "N" ? 0 : 1));
                        var multiplierText = (OsuSpriteText)typeof(FooterButtonMods).GetProperty("multiplierText", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!;
                        Assert.That(multiplierText.Text, Is.EqualTo(ModUtils.FormatScoreMultiplier(to is "NF" or "NM" ? 0.5 : 1)));
                        if (++route == routes.Length)
                        {
                            Completed = true;
                            Exit();
                        }
                        else
                            phase = 0;
                        break;
                }
            }
            catch (Exception exception)
            {
                Failure = exception;
                Exit();
            }
        }

        private void checkRankingFooter(RankingInformationDisplay display)
        {
            var flash = (Drawable)typeof(RankingInformationDisplay).GetField("flashLayer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(display)!;
            overlay.Beatmap.Value = new FlatWorkingBeatmap(new Beatmap { BeatmapInfo = new BeatmapInfo(ruleset.RulesetInfo) });
            overlay.Ruleset.Value = mania.RulesetInfo;
            gameContext.AvailableMods.Value = Enum.GetValues<ModType>().ToDictionary(type => type, type => (IReadOnlyList<Mod>)mania.GetModsFor(type).ToArray());
            songMods.Value = [new ManiaModRandom()];
            Assert.That(display.Ranked.Value, Is.False);
            flash.FinishTransforms();
            Assert.That(flash.Alpha, Is.Zero);

            overlay.Ruleset.Value = ruleset.RulesetInfo;
            gameContext.AvailableMods.Value = Enum.GetValues<ModType>().ToDictionary(type => type, type => (IReadOnlyList<Mod>)ruleset.GetModsFor(type).ToArray());
            foreach (var acronym in new[] { "", "MR", "NF", "CS", "RD", "" })
            {
                songMods.Value = acronym.Length == 0 ? [] : [ruleset.CreateAllMods().Single(mod => mod.Acronym == acronym)];
                Assert.That(display.Ranked.Value, Is.False);
                Assert.That(display.ModMultiplier.Value, Is.EqualTo(acronym == "NF" ? 0.5 : acronym == "CS" ? 0.9 : 1));
                Assert.That(flash.Transforms, Is.Empty, $"Switching to {acronym} must not re-highlight the unchanged unranked panel.");
                Assert.That(flash.Alpha, Is.Zero);
            }

            songMods.Value = [new O2JamModManiaScore()];
            Assert.That(display.Ranked.Value, Is.True);
            Assert.That(flash.Transforms, Is.Not.Empty, "Actual eligibility changes must still flash.");
            flash.FinishTransforms();
            songMods.Value = [];
            Assert.That(display.Ranked.Value, Is.False);
            Assert.That(flash.Transforms, Is.Not.Empty);
            flash.FinishTransforms();

            overlay.Ruleset.Value = mania.RulesetInfo;
            gameContext.AvailableMods.Value = Enum.GetValues<ModType>().ToDictionary(type => type, type => (IReadOnlyList<Mod>)mania.GetModsFor(type).ToArray());
            flash.FinishTransforms();
            songMods.Value = [new ManiaModNoFail()];
            Assert.That(display.ModMultiplier.Value, Is.EqualTo(0.5));
            Assert.That(flash.Transforms, Is.Not.Empty, "Other rulesets keep their native multiplier flash.");
        }

        private void select(string acronym)
        {
            if (throughOverlay)
            {
                overlay.AllAvailableMods.Single(state => state.Mod is O2JamModManiaScore).Active.Value = acronym == "MS";
                return;
            }
            button.Ruleset.Value = acronym.StartsWith('N') && acronym != "NF" ? mania.RulesetInfo : ruleset.RulesetInfo;
            button.Mods.Value = acronym switch
            {
                "NF" => [new O2JamModNoFail()],
                "MS" => [new O2JamModManiaScore()],
                "NU" => [new ManiaModRandom()],
                "NM" => [new ManiaModNoFail()],
                _ => [],
            };
        }

        private static Drawable getDrawable(FooterButtonMods target, string field) =>
            (Drawable)typeof(FooterButtonMods).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

        private void advance(double milliseconds)
        {
            sourceClock.CurrentTime += milliseconds;
            frameClock.ProcessFrame();
        }

        private void observe(string stage) => Observations.Add(
            $"{routes[route]} {stage}: clock={badge.Clock.CurrentTime}, margin={badge.Margin.Left}, x={badge.X}, y={badge.Y}, alpha={badge.Alpha}, "
            + $"screen={badge.ScreenSpaceDrawQuad.TopLeft}, transforms={string.Join(';', badge.Transforms.Select(t => $"{t.TargetMember}:{t.StartTime}-{t.EndTime}"))}");

        protected override void Dispose(bool isDisposing)
        {
            gameContext.Dispose();
            difficultyCache.Dispose();
            previewTracks.Dispose();
            previewTrack.Dispose();
            if (!throughOverlay)
                menuSamples.Dispose();
            config.Dispose();
            storage.Dispose();
            base.Dispose(isDisposing);
        }
    }

    private partial class ModProbeContext : OsuGameBase
    {
        public Bindable<IReadOnlyList<Mod>> ModSelection => SelectedMods;
        public Bindable<RulesetInfo> Mode => Ruleset;
    }

    private class SilentBeatSync(IClock clock) : IBeatSyncProvider
    {
        public IClock Clock => clock;
        public ControlPointInfo? ControlPoints => null;
        public ChannelAmplitudes CurrentAmplitudes => ChannelAmplitudes.Empty;
    }
}
