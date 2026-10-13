using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Carousel;
using osu.Game.Rulesets;
using osu.Game.Rulesets.O2Lazer.SongSelect;
using osu.Game.Screens;
using osu.Game.Screens.Select;
using NativeSongSelect = osu.Game.Screens.Select.SongSelect;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamCarouselSelectionTest
{
    private static SoloSongSelect? observedScreen;
    private static int validationRequests;

    [TestCase("o2lazer", "mania", "mania", false, 1)]
    [TestCase("o2lazer", "mania", "o2lazer", false, 0)]
    [TestCase("o2lazer", "o2lazer", "mania", false, 0)]
    [TestCase("mania", "mania", "mania", false, 0)]
    [TestCase("o2lazer", "mania", "mania", true, 0)]
    [TestCase("o2lazer", "mania", null, false, 1)]
    public void CompletedFilterRechecksOnlyRejectedForeignPendingSelection(
        string mode, string current, string? pending, bool filtering, int expected)
    {
        Assert.That(O2JamCarouselTransitionPatch.InstallOnce(), Is.True);
        using var screen = new SoloSongSelect();
        using var control = new FilterControl();
        var searchField = AccessTools.Field(typeof(FilterControl), "searchTextBox");
        using var searchBox = (Drawable)Activator.CreateInstance(searchField.FieldType, true)!;
        searchField.SetValue(control, searchBox);
        using var carousel = new BeatmapCarousel { RequestRecommendedSelection = _ => { }, RequestSelection = _ => { } };
        var modeInfo = new RulesetInfo { ShortName = mode };
        AccessTools.Property(typeof(OsuScreen), nameof(OsuScreen.Ruleset)).SetValue(screen, new Bindable<RulesetInfo>(modeInfo));
        AccessTools.Property(typeof(OsuScreen), nameof(OsuScreen.Beatmap)).SetValue(screen,
            new Bindable<WorkingBeatmap>(new FlatWorkingBeatmap(new Beatmap
            {
                BeatmapInfo = new BeatmapInfo(new RulesetInfo { ShortName = current }),
            })));
        AccessTools.Field(typeof(NativeSongSelect), "carousel").SetValue(screen, carousel);
        AccessTools.Property(typeof(NativeSongSelect), "FilterControl").SetValue(screen, control);
        AccessTools.Property(typeof(BeatmapCarousel), nameof(BeatmapCarousel.Criteria))
                   .SetValue(carousel, new FilterCriteria { Ruleset = modeInfo });
        AccessTools.Field(typeof(NativeSongSelect), "debounceQueuedSelection").SetValue(screen,
            pending == null ? null : new BeatmapInfo(new RulesetInfo { ShortName = pending }));
        var task = new TaskCompletionSource<IEnumerable<CarouselItem>>();
        if (!filtering)
            task.SetResult([]);
        AccessTools.Field(typeof(Carousel<BeatmapInfo>), "filterTask").SetValue(carousel, task.Task);

        Assert.That(screen.Ruleset.Value.ShortName, Is.EqualTo(mode));
        Assert.That(screen.Beatmap.Value.BeatmapInfo.Ruleset.ShortName, Is.EqualTo(current));
        Assert.That(screen.IsFiltering, Is.EqualTo(filtering));
        var harmony = new Harmony("O2JamCarouselSelectionTest.NativeBoundary");
        try
        {
            observedScreen = screen;
            validationRequests = 0;
            // Keep native newItemsPresented and its pending-selection branch real; isolate UI
            // dependencies and observe the existing selection validator instead of cloning it.
            harmony.Patch(AccessTools.Method(typeof(NativeSongSelect), "ensureGlobalBeatmapValid"),
                prefix: new HarmonyMethod(typeof(O2JamCarouselSelectionTest), nameof(observeValidation)));
            string[] uiUpdates = ["updateNoResultsPlaceholder", "updateWedgeVisibility"];
            foreach (var name in uiUpdates)
                harmony.Patch(AccessTools.Method(typeof(NativeSongSelect), name),
                    prefix: new HarmonyMethod(typeof(O2JamCarouselSelectionTest), nameof(skipScreenUi)));
            AccessTools.Method(typeof(NativeSongSelect), "newItemsPresented").Invoke(screen, [(IEnumerable<CarouselItem>)[]]);
            Assert.That(validationRequests, Is.EqualTo(expected));
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            observedScreen = null;
        }
    }

    private static bool observeValidation(NativeSongSelect __instance, ref bool __result)
    {
        if (!ReferenceEquals(__instance, observedScreen))
            return true;
        validationRequests++;
        __result = true;
        return false;
    }

    private static bool skipScreenUi(NativeSongSelect __instance) => !ReferenceEquals(__instance, observedScreen);

}
