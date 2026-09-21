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
/// Keeps native mania difficulty separate from the synthetic value used to drive osu!'s
/// difficulty-colour animation. The synthetic value is never used as visible text or persisted.
/// </summary>
internal sealed class O2JamDifficultyColourBinding
{
    private readonly IBeatmapInfo beatmap;
    private IRulesetInfo? ruleset;
    private IEnumerable<Mod>? mods;

    internal IBindable<StarDifficulty> Native { get; }

    internal Bindable<StarDifficulty> ColourDifficulty { get; } = new();

    internal IBeatmapInfo Beatmap => beatmap;

    internal bool UsesLevelColour => UsesLevel(beatmap, ruleset, mods);

    internal event Action<bool>? NativeDifficultyUpdated;

    internal O2JamDifficultyColourBinding(IBindable<StarDifficulty> native, IBeatmapInfo beatmap,
                                          IRulesetInfo? ruleset, IEnumerable<Mod>? mods)
    {
        Native = native;
        this.beatmap = beatmap;
        this.ruleset = ruleset;
        this.mods = mods;

        // Set the colour target before the native control binds to it. This avoids a mania-colour
        // frame followed by a level-colour correction when carousel panels are reused.
        ColourDifficulty.Value = GetColourDifficulty(beatmap, ruleset, mods, native.Value);
        native.BindValueChanged(change =>
        {
            ColourDifficulty.Value = GetColourDifficulty(beatmap, this.ruleset, this.mods, change.NewValue);
            NativeDifficultyUpdated?.Invoke(!change.NewValue.Stars.Equals(change.OldValue.Stars));
        });
    }

    internal void UpdateProfile(IRulesetInfo? newRuleset, IEnumerable<Mod>? newMods)
    {
        ruleset = newRuleset;
        mods = newMods;
        ColourDifficulty.Value = GetColourDifficulty(beatmap, ruleset, mods, Native.Value);
    }

    internal static bool UsesLevel(IBeatmapInfo? beatmap, IRulesetInfo? ruleset, IEnumerable<Mod>? mods) =>
        ruleset?.ShortName == O2LazerIdentity.ShortName
        && O2JamGameplayProfile.UsesLevelPresentation(beatmap, mods);

    internal static StarDifficulty GetColourDifficulty(IBeatmapInfo? beatmap, IRulesetInfo? ruleset,
                                                       IEnumerable<Mod>? mods, StarDifficulty native) =>
        UsesLevel(beatmap, ruleset, mods)
            // Max combo is deliberately excluded so an asynchronous native metadata refresh with
            // unchanged colour cannot restart an in-progress colour transition.
            ? new StarDifficulty(O2JamStarRatingMetadata.ResolveLevel(beatmap!) / 10d, 0)
            : native;
}
