using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Rendering.Dummy;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.Skinning;
using osuTK;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
public class O2JamHoldBodyExtensionTest
{
    [TestCase(32801f)]
    [TestCase(65601f)]
    public void ExtendedBodyCoversOnlyItsRequiredLengthAndRestoresNativeSizing(float height)
    {
        var previous = O2JamRuntimeOptions.UsePercyLongNoteBodyRepeat;
        try
        {
            var body = new Sprite { RelativeSizeAxes = Axes.Both, Size = Vector2.One };
            var native = new Container { RelativeSizeAxes = Axes.Both, Child = body };
            using var piece = new O2JamLegacyHoldBodyPiece(native) { RelativeSizeAxes = Axes.None, Size = new Vector2(60, height) };

            var renderer = new DummyRenderer();
            using var texture = renderer.CreateTexture(270, 40000);
            field("extensionTexture").SetValue(piece, texture);
            field("canExtend").SetValue(piece, true);
            field("nativeBodyDrawable").SetValue(piece, body);

            O2JamRuntimeOptions.UsePercyLongNoteBodyRepeat = false;
            update(piece);
            Assert.That(body.RelativeSizeAxes, Is.EqualTo(Axes.Both), "Disabled repair must preserve native body sizing.");
            Assert.That(((System.Collections.Generic.List<Sprite>)field("extensionSegments").GetValue(piece)!).Count, Is.Zero);

            O2JamRuntimeOptions.UsePercyLongNoteBodyRepeat = true;
            update(piece);
            var extensions = ((System.Collections.Generic.List<Sprite>)field("extensionSegments").GetValue(piece)!).ToArray();
            Assert.That(body.DrawHeight * Math.Abs(body.Scale.Y), Is.EqualTo(32800));
            Assert.That(extensions.Sum(sprite => sprite.DrawHeight * Math.Abs(sprite.Scale.Y)) + 32800,
                Is.EqualTo(height), "The final remainder must neither stretch nor overlap the native span.");
            foreach (var segment in extensions)
            {
                Assert.That(segment.Width, Is.EqualTo(1), "Texture pixel width must not become a multiple of the column width.");
                Assert.That(segment.DrawTextureRectangle.Height, Is.EqualTo(16400),
                    "The source sampling scale must remain constant when the final remainder shrinks.");
            }

            O2JamRuntimeOptions.UsePercyLongNoteBodyRepeat = false;
            update(piece);
            Assert.That(body.RelativeSizeAxes, Is.EqualTo(Axes.Both));
            Assert.That(body.Height, Is.EqualTo(1));
            Assert.That(((System.Collections.Generic.List<Sprite>)field("extensionSegments").GetValue(piece)!).Count, Is.Zero);
        }
        finally { O2JamRuntimeOptions.UsePercyLongNoteBodyRepeat = previous; }
    }

    private static FieldInfo field(string name) => typeof(O2JamLegacyHoldBodyPiece).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static void update(O2JamLegacyHoldBodyPiece piece)
    {
        foreach (var name in new[] { "updateNativeSpan", "updateExtensions" })
            typeof(O2JamLegacyHoldBodyPiece).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(piece, null);
    }
}