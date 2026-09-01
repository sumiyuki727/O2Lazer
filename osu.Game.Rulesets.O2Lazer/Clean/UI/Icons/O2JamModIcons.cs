using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Framework.Text;
using osu.Game.Graphics;

namespace osu.Game.Rulesets.O2Lazer.UI.Icons;

public static class O2JamModIcons
{
    private const string font_name = "O2LazerModIcons";
    private const char mania_score = '\ue000';

    private static readonly ConditionalWeakTable<FontStore, IconStore> registeredStores = new();
    private static readonly object registrationLock = new();

    public static IconUsage ManiaScore => new(mania_score, font_name);

    internal static void Register(FontStore fonts, IRenderer renderer)
    {
        lock (registrationLock)
        {
            if (registeredStores.TryGetValue(fonts, out _))
                return;

            // SpriteIcon resolves a glyph once, so install the PNG-backed lookup before mod UI
            // loads. Native ModIcon and ModSwitch retain ownership of sizing, colour and state.
            var store = new IconStore(renderer);
            fonts.AddStore(store);
            registeredStores.Add(fonts, store);
        }
    }

    private sealed class IconStore(IRenderer renderer) : TextureStore(renderer,
        new TextureLoaderStore(new NamespacedResourceStore<byte[]>(
            new DllResourceStore(typeof(O2LazerRuleset).Assembly), "Resources")), false), ITexturedGlyphLookupStore
    {
        public ITexturedCharacterGlyph? Get(string? fontName, char character)
        {
            if (fontName != font_name || character != mania_score)
                return null;

            var texture = Get("Textures/Icons/Mods/mod-mania-score");
            return texture == null ? null : new OsuIcon.OsuIconStore.Glyph(texture);
        }

        public Task<ITexturedCharacterGlyph?> GetAsync(string fontName, char character) =>
            Task.Run(() => Get(fontName, character));
    }
}
