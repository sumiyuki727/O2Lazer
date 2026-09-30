using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Textures;
using osu.Framework.Testing;
using osu.Game.Audio;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Skinning.Legacy;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Skinning;
using osu.Game.Rulesets.UI.Scrolling;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamHoldVisualProbeTest
{
    [Test]
    public void PooledLegacySkinDoesNotMultiplyMissTint(
        [Values(false, true)] bool o2Visual,
        [Values(false, true)] bool missHead,
        [Values(ScrollingDirection.Down, ScrollingDirection.Up)] ScrollingDirection direction)
    {
        var previousVisual = O2JamRuntimeOptions.UseO2JamLongNoteMissVisual;
        O2JamRuntimeOptions.UseO2JamLongNoteMissVisual = o2Visual;
        try
        {
            using var host = new TestRunHeadlessGameHost($"O2JamLegacyMissVisual-{Guid.NewGuid():N}");
            var game = new ProbeGame(missHead ? 50 : 500, o2Visual, direction, missHead ? O2JamAccuracy.Miss : null)
            {
                UseLegacySkin = true,
            };
            host.Run(game);
            if (game.Failure != null)
                throw game.Failure;
            Assert.That(game.Completed, Is.True);
        }
        finally
        {
            O2JamRuntimeOptions.UseO2JamLongNoteMissVisual = previousVisual;
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LegacySkinReloadPreservesHoldStateBindings(bool o2Visual)
    {
        var previousVisual = O2JamRuntimeOptions.UseO2JamLongNoteMissVisual;
        O2JamRuntimeOptions.UseO2JamLongNoteMissVisual = o2Visual;
        try
        {
            using var host = new TestRunHeadlessGameHost($"O2JamLegacySkinReload-{Guid.NewGuid():N}");
            var game = new ProbeGame(500, o2Visual)
            {
                UseLegacySkin = true,
                ReloadLegacySkin = true,
            };
            host.Run(game);
            if (game.Failure != null)
                throw game.Failure;
            Assert.That(game.Completed, Is.True);
        }
        finally
        {
            O2JamRuntimeOptions.UseO2JamLongNoteMissVisual = previousVisual;
        }
    }

    private partial class ProbeGame
    {
        public bool UseLegacySkin { get; init; }
        public bool ReloadLegacySkin { get; init; }
        private ReloadableProbeSkinProvider skinProvider = null!;

        private ISkin createLegacySkin()
        {
            var beatmap = new ManiaBeatmap(new StageDefinition(7));
            beatmap.BeatmapInfo.Ruleset = new ManiaRuleset().RulesetInfo;
            return O2JamSkinTransformer.WrapIfNeeded(new ManiaLegacySkinTransformer(new LegacyProbeSkin(Host.Renderer.WhitePixel), beatmap));
        }

        private void verifyLegacyColours()
        {
            // Use the native skin pieces loaded by the pools, rather than substitute boxes which
            // cannot expose the native MISS tint applied after the parent's colour update.
            Drawable[] pieces =
            [
                hold.ChildrenOfType<LegacyBodyPiece>().Single(),
                hold.Head.ChildrenOfType<LegacyHoldNoteHeadPiece>().Single(),
                hold.Tail.ChildrenOfType<LegacyHoldNoteTailPiece>().Single(),
            ];
            var expectedColour = (ColourInfo)(verifyO2Visual == true ? Colour4.White : Colour4.DarkGray);
            Assert.That(hold.MissingStartTime.Value, Is.Not.Null);
            Assert.That(hold.Head.MissingStartTime.Value, Is.EqualTo(hold.MissingStartTime.Value), "Skin disposal must not unbind the head from its hold.");
            Assert.That(hold.Tail.MissingStartTime.Value, Is.EqualTo(hold.MissingStartTime.Value), "Skin disposal must not unbind the tail from its hold.");
            foreach (var piece in pieces)
            {
                Assert.That(piece.Colour, Is.EqualTo((ColourInfo)Colour4.White), $"{piece.GetType().Name} must leave the MISS tint to the parent.");
                Assert.That(piece.DrawColourInfo.Colour, Is.EqualTo(expectedColour), $"{piece.GetType().Name} must follow the selected visual policy.");
            }
        }
    }

    private partial class ReloadableProbeSkinProvider(ISkin skin) : SkinProvidingContainer(skin)
    {
        public void Reload() => TriggerSourceChanged();
    }

    private sealed class LegacyProbeSkin(Texture texture) : ISkin
    {
        public Drawable? GetDrawableComponent(ISkinComponentLookup lookup) => null;

        public Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT)
            => componentName is "mania-key1" or "mania-noteS" or "mania-noteSH" or "mania-noteST" or "mania-noteSL" ? texture : null;

        public ISample? GetSample(ISampleInfo sample) => null;

        public IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup) where TLookup : notnull where TValue : notnull
            => lookup is SkinConfiguration.LegacySetting.Version && typeof(TValue) == typeof(decimal)
                ? (IBindable<TValue>)(object)new Bindable<decimal>(2.7m)
                : null;
    }
}
