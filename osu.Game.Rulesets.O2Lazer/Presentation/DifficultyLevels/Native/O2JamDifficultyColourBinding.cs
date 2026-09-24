using System;
using System.Collections.Generic;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Mods;

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

/// <summary>
/// Keeps native mania difficulty separate from the star-only value used by osu!'s
/// difficulty-colour animation. The projected value is never used for scoring or persisted.
/// </summary>
internal sealed class O2JamDifficultyColourBinding : IDisposable
{
    private readonly IBeatmapInfo beatmap;
    private IRulesetInfo? ruleset;
    private IEnumerable<Mod>? mods;
    private readonly Action<ValueChangedEvent<StarDifficulty>> onNativeDifficultyChanged;

    internal IBindable<StarDifficulty> Native { get; }

    internal Bindable<StarDifficulty> ColourDifficulty { get; } = new();

    internal IBeatmapInfo Beatmap => beatmap;

    internal bool UsesLevelColour => UsesLevel(beatmap, ruleset, mods);

    internal event Action<bool>? NativeDifficultyUpdated;

    internal O2JamDifficultyColourBinding(IBindable<StarDifficulty> native, IBeatmapInfo beatmap,
                                          IRulesetInfo? ruleset, IEnumerable<Mod>? mods,
                                          double? previousDisplayedStars = null)
    {
        Native = native;
        this.beatmap = beatmap;
        this.ruleset = ruleset;
        this.mods = mods;

        // Set the colour target before the native control binds to it. This avoids a mania-colour
        // frame followed by a level-colour correction when carousel panels are reused.
        var initialDifficulty = GetColourDifficulty(beatmap, ruleset, mods, native.Value);
        ColourDifficulty.Value = !isValid(initialDifficulty.Stars)
                                 && previousDisplayedStars is double fallback && isValid(fallback)
            ? new StarDifficulty(fallback, 0)
            : initialDifficulty;
        // The native cache can outlive a recycled drawable. A weak event target lets the
        // owner and its presentation binding be collected without waiting for another panel use.
        var weakBinding = new WeakReference<O2JamDifficultyColourBinding>(this);
        Action<ValueChangedEvent<StarDifficulty>>? handler = null;
        handler = change =>
        {
            if (!weakBinding.TryGetTarget(out var binding))
            {
                native.ValueChanged -= handler;
                return;
            }

            if (!isValid(change.NewValue.Stars))
                return;

            binding.updateColourDifficulty(change.NewValue);
            binding.NativeDifficultyUpdated?.Invoke(!change.NewValue.Stars.Equals(change.OldValue.Stars));
        };
        onNativeDifficultyChanged = handler;
        native.BindValueChanged(onNativeDifficultyChanged);
    }

    internal void UpdateProfile(IRulesetInfo? newRuleset, IEnumerable<Mod>? newMods)
    {
        ruleset = newRuleset;
        mods = newMods;
        updateColourDifficulty(Native.Value);
    }

    private void updateColourDifficulty(StarDifficulty nativeDifficulty)
    {
        var projected = GetColourDifficulty(beatmap, ruleset, mods, nativeDifficulty);
        // A pending native difficulty must not animate the visible pill through -1.
        if (isValid(projected.Stars) || !isValid(ColourDifficulty.Value.Stars))
            ColourDifficulty.Value = projected;
    }

    private static bool isValid(double stars) => double.IsFinite(stars) && stars >= 0;

    public void Dispose()
    {
        Native.ValueChanged -= onNativeDifficultyChanged;
        NativeDifficultyUpdated = null;
    }

    internal static bool UsesLevel(IBeatmapInfo? beatmap, IRulesetInfo? ruleset, IEnumerable<Mod>? mods) =>
        ruleset?.ShortName == O2LazerIdentity.ShortName
        && O2JamGameplayProfile.UsesLevelPresentation(beatmap, mods);

    internal static StarDifficulty GetColourDifficulty(IBeatmapInfo? beatmap, IRulesetInfo? ruleset,
                                                       IEnumerable<Mod>? mods, StarDifficulty native)
    {
        if (beatmap?.Ruleset.ShortName != O2LazerIdentity.ShortName)
            return native;

        var stars = UsesLevel(beatmap, ruleset, mods)
            ? O2JamStarRatingMetadata.ResolveLevel(beatmap) / 10d
            : native.Stars;
        // The native pill only uses Stars. Metadata-only refreshes must not restart its
        // in-progress colour animation with a zero-star-delta 100 ms transform.
        return new StarDifficulty(stars, 0);
    }
}
