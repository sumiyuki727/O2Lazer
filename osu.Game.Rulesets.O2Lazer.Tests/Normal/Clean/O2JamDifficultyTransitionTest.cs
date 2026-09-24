using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Linq;
using osu.Game.Utils;
using osu.Game.Rulesets;
using HarmonyLib;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Shapes;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.SongSelect;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamDifficultyTransitionTest
{
    [Test]
    public void ManiaScoreToggleFollowsNativeNumericTransition()
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = new BeatmapInfo(ruleset.RulesetInfo)
        {
            DifficultyName = "EX Lv.75",
            Metadata = new BeatmapMetadata { Tags = O2JamStarRatingMetadata.CreateO2JamTag(75) },
        };
        using var display = new StarRatingDisplay(new StarDifficulty(4.875, 0));
        typeof(StarRatingDisplay).GetProperty("colours", BindingFlags.Instance | BindingFlags.NonPublic)!
                                 .SetValue(display, new OsuColour());
        var text = (OsuSpriteText)typeof(StarRatingDisplay).GetField("starsText", BindingFlags.Instance | BindingFlags.NonPublic)!
                                                   .GetValue(display)!;
        var background = (Box)typeof(StarRatingDisplay).GetField("background", BindingFlags.Instance | BindingFlags.NonPublic)!
                                                  .GetValue(display)!;
        var callback = AccessTools.GetDeclaredMethods(typeof(StarRatingDisplay))
                                  .Single(method => method.Name.StartsWith("<LoadComplete>b__", StringComparison.Ordinal)
                                                    && method.GetParameters().Single().ParameterType == typeof(ValueChangedEvent<double>));

        O2JamStarRatingPresentationPatch.Configure(display, beatmap, [new O2JamModManiaScore()]);
        update(4.875);
        Assert.That(text.Text.ToString(), Is.EqualTo(4.875.FormatStarRating().ToString()));

        O2JamStarRatingPresentationPatch.Configure(display, beatmap, []);
        update(4.875);
        Assert.That(text.Text.ToString(), Is.EqualTo(O2LazerStrings.LevelBadge(49).ToString()));
        update(6.25);
        Assert.That(text.Text.ToString(), Is.EqualTo(O2LazerStrings.LevelBadge(62).ToString()));
        update(7.5);
        Assert.That(text.Text.ToString(), Is.EqualTo(O2LazerStrings.LevelBadge(75).ToString()));

        O2JamStarRatingPresentationPatch.Configure(display, beatmap, [new O2JamModManiaScore()]);
        update(7.5);
        Assert.That(text.Text.ToString(), Is.EqualTo(7.5.FormatStarRating().ToString()));
        update(4.875);
        Assert.That(text.Text.ToString(), Is.EqualTo(4.875.FormatStarRating().ToString()));
        Assert.That(background.Colour.AverageColour.SRGB, Is.EqualTo(new OsuColour().ForStarDifficulty(4.875)));

        void update(double stars) => callback.Invoke(display, [new ValueChangedEvent<double>(0, stars)]);
    }

    [Test]
    public void ReplacedColourBindingStopsFollowingOldNativeResults()
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = new BeatmapInfo(ruleset.RulesetInfo)
        {
            DifficultyName = "EX Lv.75",
            Metadata = new BeatmapMetadata { Tags = O2JamStarRatingMetadata.CreateO2JamTag(75) },
        };
        var native = new Bindable<StarDifficulty>(new StarDifficulty(3.25, 0));
        var binding = new O2JamDifficultyColourBinding(native, beatmap, ruleset.RulesetInfo, [new O2JamModManiaScore()]);
        native.Value = new StarDifficulty(4.875, 0);
        Assert.That(binding.ColourDifficulty.Value.Stars, Is.EqualTo(4.875));
        binding.Dispose();
        native.Value = new StarDifficulty(6.25, 0);
        Assert.That(binding.ColourDifficulty.Value.Stars, Is.EqualTo(4.875));
    }

    [Test]
    public void MissingNativeStarsDoNotAnimateThroughTheSentinel()
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = new BeatmapInfo(ruleset.RulesetInfo)
        {
            Metadata = new BeatmapMetadata { Tags = O2JamStarRatingMetadata.CreateO2JamTag(75) },
        };
        var native = new Bindable<StarDifficulty>(new StarDifficulty(-1, 0));
        using var binding = new O2JamDifficultyColourBinding(native, beatmap, ruleset.RulesetInfo,
            [new O2JamModManiaScore()], previousDisplayedStars: 4.875);
        Assert.That(binding.ColourDifficulty.Value.Stars, Is.EqualTo(4.875));

        native.Value = new StarDifficulty(3.25, 0);
        Assert.That(binding.ColourDifficulty.Value.Stars, Is.EqualTo(3.25));
        native.Value = new StarDifficulty(-1, 0);
        Assert.That(binding.ColourDifficulty.Value.Stars, Is.EqualTo(3.25));
    }

    [Test]
    public void CrossModeManiaScoreUsesKnownTargetImmediately()
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = new BeatmapInfo(ruleset.RulesetInfo)
        {
            DifficultyName = "EX Lv.75",
            Metadata = new BeatmapMetadata { Tags = O2JamStarRatingMetadata.CreateO2JamTag(75) },
        };
        var native = new Bindable<StarDifficulty>(new StarDifficulty(3.25, 0));
        using var binding = new O2JamDifficultyColourBinding(native, beatmap, ruleset.RulesetInfo,
            [new O2JamModManiaScore()], previousDisplayedStars: 4.875);

        Assert.That(binding.ColourDifficulty.Value.Stars, Is.EqualTo(3.25));
        native.Value = new StarDifficulty(3.5, 0);
        Assert.That(binding.ColourDifficulty.Value.Stars, Is.EqualTo(3.5));
    }

    [Test]
    public void ManiaScoreMetadataRefreshDoesNotRestartColourAnimation()
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = new BeatmapInfo(ruleset.RulesetInfo)
        {
            DifficultyName = "EX Lv.75",
            Metadata = new BeatmapMetadata { Tags = O2JamStarRatingMetadata.CreateO2JamTag(75) },
        };
        var native = new Bindable<StarDifficulty>(new StarDifficulty(7.9, 0));
        using var binding = new O2JamDifficultyColourBinding(native, beatmap, ruleset.RulesetInfo,
            [new O2JamModManiaScore()]);
        var colourUpdates = 0;
        binding.ColourDifficulty.BindValueChanged(_ => colourUpdates++);

        native.Value = new StarDifficulty(7.9, 1234);
        Assert.Multiple(() =>
        {
            Assert.That(binding.Native.Value.MaxCombo, Is.EqualTo(1234));
            Assert.That(binding.ColourDifficulty.Value.Stars, Is.EqualTo(7.9));
            Assert.That(binding.ColourDifficulty.Value.MaxCombo, Is.Zero);
            Assert.That(colourUpdates, Is.Zero);
        });

        native.Value = new StarDifficulty(8.1, 1234);
        Assert.Multiple(() =>
        {
            Assert.That(binding.ColourDifficulty.Value.Stars, Is.EqualTo(8.1));
            Assert.That(colourUpdates, Is.EqualTo(1));
        });
    }
    [Test]
    public void NativeCacheDoesNotRetainAbandonedColourBinding()
    {
        var native = new Bindable<StarDifficulty>(new StarDifficulty(3.25, 0));
        var reference = abandonBinding(native);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(reference.TryGetTarget(out _), Is.False);
        Assert.DoesNotThrow(() => native.Value = new StarDifficulty(4.875, 0));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<O2JamDifficultyColourBinding> abandonBinding(IBindable<StarDifficulty> native)
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = new BeatmapInfo(ruleset.RulesetInfo)
        {
            Metadata = new BeatmapMetadata { Tags = O2JamStarRatingMetadata.CreateO2JamTag(75) },
        };
        var binding = new O2JamDifficultyColourBinding(native, beatmap, ruleset.RulesetInfo, []);
        return new WeakReference<O2JamDifficultyColourBinding>(binding);
    }

}
