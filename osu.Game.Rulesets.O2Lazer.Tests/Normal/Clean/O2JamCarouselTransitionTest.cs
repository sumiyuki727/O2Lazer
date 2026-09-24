using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Carousel;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.SongSelect;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamCarouselTransitionTest
{
    private SynchronizationContext? previousContext;
    private static FilterControl? criteriaControl;
    private static FilterCriteria? nextCriteria;

    [SetUp]
    public void SetUp()
    {
        previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
    }

    [TearDown]
    public void TearDown() => SynchronizationContext.SetSynchronizationContext(previousContext);

    [Test]
    public void RulesetAndOptionChangesPublishOnlyFinalCriteria()
    {
        Assert.That(O2JamCarouselTransitionPatch.InstallOnce(), Is.True);
        using var control = new FilterControl();
        var targetRuleset = new RulesetInfo { ShortName = "o2lazer" };
        AccessTools.Property(typeof(FilterControl), "ruleset").SetValue(control, new Bindable<RulesetInfo>(targetRuleset));
        AccessTools.Field(typeof(FilterControl), "currentCriteria").SetValue(control,
            new FilterCriteria { Ruleset = new RulesetInfo { ShortName = "mania" } });
        var published = new List<FilterCriteria>();
        control.CriteriaChanged += published.Add;
        var harmony = new Harmony("O2JamCarouselTransitionTest.Criteria");
        try
        {
            criteriaControl = control;
            harmony.Patch(AccessTools.Method(typeof(FilterControl), nameof(FilterControl.CreateCriteria)),
                prefix: new HarmonyMethod(typeof(O2JamCarouselTransitionTest), nameof(supplyCriteria)));
            var update = AccessTools.Method(typeof(FilterControl), "updateCriteria");
            nextCriteria = new FilterCriteria { Ruleset = targetRuleset };
            update.Invoke(control, [false]);
            var finalCriteria = nextCriteria = new FilterCriteria { Ruleset = targetRuleset, Sort = SortMode.Difficulty };
            update.Invoke(control, [false]);
            Assert.That(published, Is.Empty);
            var scheduler = (Scheduler)AccessTools.Property(typeof(Drawable), "Scheduler").GetValue(control)!;
            scheduler.Update();
            Assert.That(published, Is.EqualTo(new[] { finalCriteria }));
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            criteriaControl = null;
            nextCriteria = null;
        }
    }

    private static bool supplyCriteria(FilterControl __instance, ref FilterCriteria __result)
    {
        if (!ReferenceEquals(__instance, criteriaControl))
            return true;
        __result = nextCriteria!;
        return false;
    }

    [Test]
    public void NativeSwitchSupersedesPendingO2Transition()
    {
        Assert.That(O2JamModeSwitchDirectionPatch.InstallOnce(), Is.True);
        Assert.That(O2JamCarouselTransitionPatch.InstallOnce(), Is.True);
        using var carousel = new BeatmapCarousel { RequestRecommendedSelection = _ => { }, RequestSelection = _ => { } };
        var criteria = AccessTools.Property(typeof(BeatmapCarousel), nameof(BeatmapCarousel.Criteria));
        var beginFilter = AccessTools.Method(typeof(O2JamCarouselTransitionPatch), "beginFilter");
        var directionUntil = AccessTools.Field(typeof(O2JamModeSwitchDirectionPatch), "transitionUntil");

        criteria.SetValue(carousel, new FilterCriteria { Ruleset = new RulesetInfo { ShortName = "o2lazer" } });
        object[] leavingO2 = [carousel, new FilterCriteria { Ruleset = new RulesetInfo { ShortName = "mania" } }, false];
        beginFilter.Invoke(null, leavingO2);
        Assert.That(carousel.DebounceDelay, Is.EqualTo(16));
        Assert.That((long)directionUntil.GetValue(null)!, Is.GreaterThan(0));

        criteria.SetValue(carousel, new FilterCriteria { Ruleset = new RulesetInfo { ShortName = "mania" } });
        object[] nativeSwitch = [carousel, new FilterCriteria { Ruleset = new RulesetInfo { ShortName = "fruits" } }, false];
        beginFilter.Invoke(null, nativeSwitch);
        Assert.Multiple(() =>
        {
            Assert.That(nativeSwitch[2], Is.False, "The native filter keeps its own loading policy.");
            Assert.That(carousel.DebounceDelay, Is.EqualTo(100));
            Assert.That((long)directionUntil.GetValue(null)!, Is.Zero);
        });

        using var panel = new PanelBeatmapStandalone();
        var nativeAlpha = panel.Alpha;
        AccessTools.Method(typeof(O2JamCarouselTransitionPatch), "prepareEntrance").Invoke(null, [carousel, panel]);
        Assert.That(panel.Alpha, Is.EqualTo(nativeAlpha), "The native panel entrance must not inherit O2Lazer state.");
    }

    [TestCase("mania", "o2lazer")]
    [TestCase("o2lazer", "mania")]
    public void SupersededCompletionKeepsNativeSpinnerUntilCurrentFilterCompletes(string previous, string current)
    {
        Assert.That(O2JamCarouselTransitionPatch.InstallOnce(), Is.True);
        using var carousel = new BeatmapCarousel { RequestRecommendedSelection = _ => { }, RequestSelection = _ => { } };
        AccessTools.Property(typeof(BeatmapCarousel), nameof(BeatmapCarousel.Criteria))
                   .SetValue(carousel, new FilterCriteria { Ruleset = new RulesetInfo { ShortName = previous } });
        object[] arguments = [carousel, new FilterCriteria { Ruleset = new RulesetInfo { ShortName = current } }, false];
        AccessTools.Method(typeof(O2JamCarouselTransitionPatch), "beginFilter").Invoke(null, arguments);
        Assert.That(arguments[2], Is.True);
        Assert.That(carousel.DebounceDelay, Is.EqualTo(16));

        var pending = new TaskCompletionSource<IEnumerable<CarouselItem>>();
        AccessTools.Field(typeof(Carousel<BeatmapInfo>), "filterTask").SetValue(carousel, pending.Task);
        var loading = (LoadingLayer)AccessTools.Field(typeof(BeatmapCarousel), "loading").GetValue(carousel)!;
        loading.Show();
        var completion = O2JamCarouselTransitionPatch.FindCompletion()!;
        completion.Invoke(carousel, null);
        Assert.That(carousel.DebounceDelay, Is.EqualTo(16));
        Assert.That(loading.State.Value, Is.EqualTo(Visibility.Visible), "A cancelled request must not hide the current request's spinner.");

        using var newPanel = new PanelBeatmapStandalone();
        AccessTools.Method(typeof(O2JamCarouselTransitionPatch), "prepareEntrance").Invoke(null, [carousel, newPanel]);
        Assert.That(newPanel.Alpha, Is.Zero, "Fresh and recycled panels must enter from the same native fade-in state.");

        pending.SetResult([]);
        completion.Invoke(carousel, null);
        Assert.That(carousel.DebounceDelay, Is.EqualTo(100));
        Assert.That(loading.State.Value, Is.EqualTo(Visibility.Hidden));
    }
}
