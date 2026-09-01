using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.IO.Stores;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osu.Game.Overlays.Mods;
using osu.Game.Overlays.Settings;
using osu.Game.Resources;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Mods;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
public partial class O2JamPerfectSettingsTest
{
    [Test]
    public void NativeCustomisationHidesPerfectHitsWithoutMsAndKeepsManiaUnchanged()
    {
        using var host = new TestRunHeadlessGameHost($"O2JamPerfectSettings-{Guid.NewGuid():N}");
        var game = new SettingsProbeGame();
        host.Run(game);
        if (game.Failure != null)
            throw game.Failure;
        Assert.That(game.Completed, Is.True);
    }

    private partial class SettingsProbeGame : Framework.Game
    {
        private readonly O2JamModPerfect perfect = new() { RequirePerfectHits = { Value = true } };
        private readonly SettingsScope o2Settings;
        private readonly SettingsScope maniaSettings = new(new ManiaModPerfect());
        private readonly OsuMenuSamples menuSamples = new();
        private int frames;
        private int phase;
        public Exception? Failure;
        public bool Completed;

        public SettingsProbeGame()
        {
            o2Settings = new SettingsScope(perfect);
        }

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
            dependencies.Cache(new OsuColour());
            dependencies.Cache(new OverlayColourProvider(OverlayColourScheme.Green));
            dependencies.Cache(new SessionStatics());
            dependencies.Cache(menuSamples);
            return dependencies;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Resources.AddStore(new DllResourceStore(OsuResources.ResourceAssembly));
            Add(menuSamples);
            Add(o2Settings);
            Add(maniaSettings);
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();
            if (Completed || Failure != null)
                return;

            try
            {
                var controls = o2Settings.ChildrenOfType<SettingsCheckbox>().ToArray();
                var nativeControls = maniaSettings.ChildrenOfType<SettingsCheckbox>().ToArray();
                if (++frames > 100)
                    throw new InvalidOperationException($"The native Perfect settings did not finish loading: phase={phase}, "
                        + string.Join(';', controls.Concat(nativeControls).Select(control => $"{control.LabelText}:{control.LoadState}")));
                if (controls.Length != 2 || nativeControls.Length != 2 || controls.Concat(nativeControls).Any(control => !control.IsLoaded))
                    return;

                var hasMs = phase % 2 != 0;
                string[] expectedLabels = hasMs ? ["Restart on fail", "Require perfect hits"] : ["Restart on fail"];
                Assert.That(controls.Where(control => control.IsPresent).Select(control => control.LabelText.ToString()),
                    Is.EquivalentTo(expectedLabels));
                Assert.That(nativeControls.All(control => control.IsPresent), Is.True, "Another ruleset's settings must remain independent.");
                Assert.That(perfect.RequirePerfectHits.Value, Is.True, "Hiding the control must not erase stored replay settings.");

                if (++phase == 5)
                {
                    Completed = true;
                    Exit();
                    return;
                }

                o2Settings.SelectedMods.Value = hasMs ? [perfect] : [perfect, new O2JamModManiaScore()];
            }
            catch (Exception exception)
            {
                Failure = exception;
                Exit();
            }
        }
    }

    private partial class SettingsScope : Container
    {
        private readonly ModCustomisationPanel panel;

        // Match ModSelectOverlay's per-overlay dependency instead of introducing a global selection.
        [Cached]
        public Bindable<IReadOnlyList<Mod>> SelectedMods { get; } = new([]);

        public SettingsScope(Mod mod)
        {
            RelativeSizeAxes = Axes.Both;
            SelectedMods.Value = [mod];
            Child = panel = new ModCustomisationPanel
            {
                Width = 400,
                State = { Value = Visibility.Visible },
                Enabled = { Value = true },
                SelectedMods = { BindTarget = SelectedMods },
            };
        }

        protected override void Update()
        {
            base.Update();
            panel.ExpandedState.Value = ModCustomisationPanel.ModCustomisationPanelState.Expanded;
        }
    }
}
