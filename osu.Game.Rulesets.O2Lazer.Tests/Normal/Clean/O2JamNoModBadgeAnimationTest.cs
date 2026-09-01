using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Screens.Play.HUD;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamNoModBadgeAnimationTest
{
    [TestCase("L", "U")]
    [TestCase("U", "L")]
    [TestCase("L", "M")]
    [TestCase("M", "L")]
    [TestCase("L", "N")]
    [TestCase("N", "L")]
    [TestCase("L", "NU")]
    [TestCase("NU", "L")]
    [TestCase("L", "NM")]
    [TestCase("NM", "L")]
    public void CustomRoutesPreserveTheirVisiblePath(string from, string to)
    {
        using var display = new Harness(from);
        display.Select(to);
        display.Flush();
        var upperOnly = from is "U" or "NU" || to is "U" or "NU";
        if (!upperOnly && to == "L")
        {
            Assert.That(display.Badge.X, Is.EqualTo(-display.NativeMargin));
            Assert.That(display.Badge.Y, Is.EqualTo(20));
            Assert.That(display.Badge.Alpha, Is.Zero);
        }

        display.Advance(60);
        if (upperOnly)
        {
            Assert.That(display.Badge.X, Is.GreaterThan(-display.NativeMargin).And.LessThan(0));
            Assert.That(display.Badge.Y, Is.EqualTo(-5));
            Assert.That(display.Badge.Alpha, Is.EqualTo(1));
            var progress = (display.Badge.X + display.NativeMargin) / display.NativeMargin;
            Assert.That(display.Button.Width,
                Is.EqualTo(display.NativeWidth + progress * (5 + display.Badge.DrawWidth)).Within(0.001),
                "Both directions resize the button concurrently with the horizontal slide.");
        }
        else
        {
            Assert.That(display.Badge.X, Is.EqualTo(-display.NativeMargin));
            Assert.That(display.Badge.Y, Is.GreaterThan(-5).And.LessThan(20));
            Assert.That(display.Badge.Alpha, Is.GreaterThan(0).And.LessThan(1));
        }

        display.Advance(179);
        if (from == "L" && !upperOnly)
            Assert.That(display.Badge.X, Is.EqualTo(-display.NativeMargin), "No horizontal relocation before the fade has completed.");
        display.Advance(1);
        assertEndpoint(display, to);
        display.Advance(500);
        assertEndpoint(display, to);
    }

    [TestCase("U", "M", "NU", "NM")]
    [TestCase("M", "U", "NM", "NU")]
    [TestCase("NU", "N", "NU", "N")]
    [TestCase("NM", "N", "NM", "N")]
    [TestCase("N", "NU", "N", "NU")]
    [TestCase("N", "NM", "N", "NM")]
    public void NativeRoutesKeepTheirOriginalTransforms(string from, string to, string referenceFrom, string referenceTo)
    {
        using var actual = new Harness(from);
        using var reference = new Harness(referenceFrom);
        actual.Select(to);
        reference.Select(referenceTo);
        actual.Flush();
        reference.Flush();
        foreach (var elapsed in new[] { 0, 60, 120, 60, 240 })
        {
            actual.Advance(elapsed);
            reference.Advance(elapsed);
            assertSameVisuals(actual, reference);
        }
    }

    [TestCase("L", "U")]
    [TestCase("U", "L")]
    [TestCase("L", "M")]
    [TestCase("M", "L")]
    [TestCase("L", "N")]
    [TestCase("N", "L")]
    public void RepeatedRefreshesDoNotRestartCustomBadgeMovement(string from, string to)
    {
        using var actual = new Harness(from);
        using var reference = new Harness(from);
        actual.Select(to);
        reference.Select(to);
        actual.Flush();
        reference.Flush();
        actual.Advance(60);
        reference.Advance(60);
        actual.Select(to);
        actual.Flush();
        foreach (var elapsed in new[] { 60, 120, 480 })
        {
            actual.Advance(elapsed);
            reference.Advance(elapsed);
            Assert.That(actual.Badge.X, Is.EqualTo(reference.Badge.X).Within(0.0001));
            Assert.That(actual.Badge.Y, Is.EqualTo(reference.Badge.Y).Within(0.0001));
            Assert.That(actual.Badge.Alpha, Is.EqualTo(reference.Badge.Alpha).Within(0.0001));
        }
    }

    [TestCase("L", "U", "M")]
    [TestCase("M", "U", "L")]
    [TestCase("NU", "L", "NM")]
    [TestCase("NM", "L", "N")]
    [TestCase("L", "N", "NU")]
    [TestCase("L", "U", "N")]
    public void SameFrameNotificationsUseOnlyTheFinalSelection(string from, string intermediate, string to)
    {
        using var actual = new Harness(from);
        using var reference = new Harness(from);
        actual.Select(intermediate);
        actual.Select(to);
        reference.Select(to);
        actual.Flush();
        reference.Flush();
        foreach (var elapsed in new[] { 0, 60, 180, 500 })
        {
            actual.Advance(elapsed);
            reference.Advance(elapsed);
            assertSameVisuals(actual, reference);
        }
    }

    [TestCase("L", "U", "L")]
    [TestCase("U", "L", "U")]
    [TestCase("L", "M", "L")]
    [TestCase("M", "L", "M")]
    [TestCase("L", "N", "L")]
    [TestCase("N", "L", "NU")]
    public void InterruptionsContinueFromCurrentValuesAndCancelOldRelocations(string from, string intermediate, string to)
    {
        using var display = new Harness(from);
        display.Select(intermediate);
        display.Flush();
        display.Advance(60);
        var previous = (display.Badge.X, display.Badge.Y, display.Badge.Alpha, display.Button.Width);
        display.Select(to);
        display.Flush();
        display.Advance(0);
        Assert.That(display.Badge.X, Is.EqualTo(previous.X).Within(0.0001));
        Assert.That(display.Badge.Y, Is.EqualTo(previous.Y).Within(0.0001));
        Assert.That(display.Badge.Alpha, Is.EqualTo(previous.Alpha).Within(0.0001));
        Assert.That(display.Button.Width, Is.EqualTo(previous.Width).Within(0.0001));
        display.Advance(240);
        assertEndpoint(display, to);
        display.Advance(1000);
        assertEndpoint(display, to);
    }

    [Test]
    public void NativeNoModKeepsItsHiddenHorizontalHistoryAcrossLeftUpper()
    {
        using var display = new Harness("NM");
        display.Select("N");
        display.Advance(500);
        Assert.That(display.Badge.X, Is.EqualTo(-display.Badge.DrawWidth));
        display.Select("L");
        display.Flush();
        display.Advance(240);
        assertEndpoint(display, "L");
        display.Select("N");
        display.Flush();
        display.Advance(239);
        Assert.That(display.Badge.X, Is.EqualTo(-display.NativeMargin));
        display.Advance(1);
        Assert.That(display.Badge.Alpha, Is.Zero);
        Assert.That(display.Badge.X, Is.EqualTo(-display.Badge.DrawWidth));
        Assert.That(display.Badge.Y, Is.EqualTo(20));
    }

    [TestCase("L", "N", false)]
    [TestCase("L", "N", true)]
    [TestCase("L", "NU", false)]
    [TestCase("L", "NU", true)]
    [TestCase("L", "NM", false)]
    [TestCase("L", "NM", true)]
    [TestCase("N", "L", false)]
    [TestCase("N", "L", true)]
    [TestCase("NU", "L", false)]
    [TestCase("NU", "L", true)]
    [TestCase("NM", "L", false)]
    [TestCase("NM", "L", true)]
    public void CrossRulesetNotificationsWorkInBothOrders(string from, string to, bool modsFirst)
    {
        using var actual = new Harness(from);
        using var reference = new Harness(from);
        actual.SelectWithSeparateNotifications(to, modsFirst);
        reference.Select(to);
        actual.Flush();
        reference.Flush();
        foreach (var elapsed in new[] { 0, 60, 180, 500 })
        {
            actual.Advance(elapsed);
            reference.Advance(elapsed);
            assertSameVisuals(actual, reference);
        }
    }

    [Test]
    public void InterruptedNativeModChangesRetainManiaBehaviour()
    {
        using var actual = new Harness("U");
        using var reference = new Harness("NU");
        foreach (var (o2State, maniaState) in new[] { ("M", "NM"), ("U", "NU"), ("M", "NM") })
        {
            actual.Select(o2State);
            reference.Select(maniaState);
            actual.Flush();
            reference.Flush();
            actual.Advance(60);
            reference.Advance(60);
            assertSameVisuals(actual, reference);
        }
        actual.Advance(240);
        reference.Advance(240);
        assertSameVisuals(actual, reference);
    }

    private static void assertSameVisuals(Harness actual, Harness reference)
    {
        Assert.That(actual.Badge.X, Is.EqualTo(reference.Badge.X).Within(0.0001));
        Assert.That(actual.Badge.Y, Is.EqualTo(reference.Badge.Y).Within(0.0001));
        Assert.That(actual.Badge.Alpha, Is.EqualTo(reference.Badge.Alpha).Within(0.0001));
        Assert.That(actual.Button.Width, Is.EqualTo(reference.Button.Width).Within(0.0001));
        Assert.That(actual.ModBar.Y, Is.EqualTo(reference.ModBar.Y).Within(0.0001));
        Assert.That(actual.ModBar.Alpha, Is.EqualTo(reference.ModBar.Alpha).Within(0.0001));
    }

    [Test]
    public void ChangingBadgeTextWidthDuringASlideDoesNotRestartItsMovement()
    {
        using var actual = new Harness("L");
        using var reference = new Harness("L");
        actual.Select("U");
        reference.Select("U");
        actual.Flush();
        reference.Flush();
        actual.Advance(60);
        reference.Advance(60);
        actual.Badge.Width += 50;
        actual.Select("U");
        actual.Flush();
        actual.Advance(180);
        reference.Advance(180);
        Assert.That(actual.Badge.X, Is.EqualTo(reference.Badge.X));
        Assert.That(actual.Badge.Alpha, Is.EqualTo(1));
        actual.Advance(60);
        Assert.That(actual.Button.Width, Is.EqualTo(actual.NativeWidth + 5 + actual.Badge.DrawWidth));
    }

    private static void assertEndpoint(Harness display, string state)
    {
        var badge = display.Badge;
        Assert.That(badge.Margin.Left, Is.EqualTo(display.NativeMargin), "Native margin must always remain intact.");
        Assert.That(badge.X, Is.EqualTo(state switch { "L" => -display.NativeMargin, "M" or "NM" => -badge.DrawWidth, _ => 0 }));
        Assert.That(badge.Y, Is.EqualTo(state == "N" ? 20 : -5));
        Assert.That(badge.Alpha, Is.EqualTo(state is "L" or "U" or "NU" ? 1 : 0));
    }

    private sealed class Harness : IDisposable
    {
        private readonly ManualFramedClock clock = new();
        private readonly Drawable[] drawables;
        private readonly O2LazerRuleset o2 = new();
        private readonly ManiaRuleset mania = new();
        public FooterButtonMods Button { get; }
        public Box Badge { get; }
        public Container ModBar { get; }
        public float NativeWidth { get; }
        public float NativeMargin { get; }

        public Harness(string initial)
        {
            Assert.That(O2JamPerformanceEligibilityPatch.IsInstalled, Is.True);
            Button = new FooterButtonMods(null!);
            NativeWidth = Button.Width;
            NativeMargin = NativeWidth + 5;
            Badge = new Box { Width = 80, Margin = new MarginPadding { Left = NativeMargin }, Y = -5 };
            ModBar = new Container { Width = NativeWidth };
            var modDisplay = new ModDisplay();
            var overflow = new FooterButtonMods.ModCountText();
            var multiplier = new OsuSpriteText();
            drawables = [Button, Badge, ModBar, modDisplay, overflow, multiplier];
            foreach (var drawable in drawables)
                drawable.Clock = clock;
            setField("unrankedBadge", Badge);
            setField("modDisplayBar", ModBar);
            setField("modDisplay", modDisplay);
            setField("overflowModCountDisplay", overflow);
            setProperty("multiplierText", multiplier);
            setProperty("beatmap", new Bindable<WorkingBeatmap>(new FlatWorkingBeatmap(new Beatmap())));
            setProperty("colours", new OsuColour());
            Select(initial);
            Flush();
            Advance(500);
        }

        public void Select(string state)
        {
            Button.Ruleset.Value = state.StartsWith('N') ? mania.RulesetInfo : o2.RulesetInfo;
            Button.Mods.Value = modsFor(state);
            invoke(Button, "updateDisplay");
        }

        public void SelectWithSeparateNotifications(string state, bool modsFirst)
        {
            if (modsFirst)
            {
                Button.Mods.Value = modsFor(state);
                invoke(Button, "updateDisplay");
                Button.Ruleset.Value = state.StartsWith('N') ? mania.RulesetInfo : o2.RulesetInfo;
            }
            else
            {
                Button.Ruleset.Value = state.StartsWith('N') ? mania.RulesetInfo : o2.RulesetInfo;
                invoke(Button, "updateDisplay");
                Button.Mods.Value = modsFor(state);
            }
            invoke(Button, "updateDisplay");
        }

        private static Mod[] modsFor(string state) => state switch
        {
            "U" => [new O2JamModNoFail()],
            "M" => [new O2JamModManiaScore()],
            "NU" => [new ManiaModRandom()],
            "NM" => [new ManiaModNoFail()],
            _ => [],
        };

        public void Flush() => O2JamPerformanceEligibilityPatch.FlushBadgeUpdate(Button);

        public void Advance(double milliseconds)
        {
            clock.CurrentTime += milliseconds;
            foreach (var drawable in drawables)
                invoke(drawable, "UpdateTransforms");
        }

        public void Dispose()
        {
            foreach (var drawable in drawables)
                drawable.Dispose();
        }

        private void setField(string name, object value) => typeof(FooterButtonMods).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Button, value);
        private void setProperty(string name, object value) => typeof(FooterButtonMods).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Button, value);
        private static void invoke(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);
    }
}
