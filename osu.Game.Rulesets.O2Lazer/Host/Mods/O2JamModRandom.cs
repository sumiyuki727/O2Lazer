using System;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Localisation;
using osu.Framework.Utils;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Integration.Beatmaps.Transforms;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.UI;

namespace osu.Game.Rulesets.O2Lazer.Mods;

public sealed class O2JamModRandom : ManiaModRandom, IApplicableToBeatmap
{
    public override LocalisableString Description => O2LazerStrings.ModRandomDescription;

    [SettingSource(typeof(O2LazerStrings), nameof(O2LazerStrings.RandomAlgorithm), nameof(O2LazerStrings.RandomAlgorithmDescription), SettingControlType = typeof(O2JamRandomAlgorithmDropdown))]
    public Bindable<O2JamRandomAlgorithm> Algorithm { get; } = new();

    protected override LocalisableString GetSettingTooltipText(IBindable bindable) => ReferenceEquals(bindable, Algorithm)
        ? Algorithm.Value.GetLocalisableDescription()
        : base.GetSettingTooltipText(bindable);

    // Mania's method is non-virtual; reimplement the interface and direct entry consistently.
    public new void ApplyToBeatmap(IBeatmap beatmap)
    {
        // The absent setting in existing replays must retain mania's original seed/layout behaviour.
        if (Algorithm.Value == O2JamRandomAlgorithm.Native)
        {
            base.ApplyToBeatmap(beatmap);
            return;
        }

        Seed.Value ??= RNG.Next();
        var o2JamBeatmap = (O2JamBeatmap)beatmap;
        switch (Algorithm.Value)
        {
            case O2JamRandomAlgorithm.O2Jam:
                O2JamRandomBeatmapTransform.Shuffle(o2JamBeatmap, Seed.Value.Value);
                break;

            case O2JamRandomAlgorithm.Panic:
                O2JamRandomBeatmapTransform.Panic(o2JamBeatmap, Seed.Value.Value);
                break;

            case O2JamRandomAlgorithm.RRandom:
                O2JamRandomBeatmapTransform.Rotate(o2JamBeatmap, Seed.Value.Value);
                break;

            case O2JamRandomAlgorithm.SRandom:
                O2JamRandomBeatmapTransform.ShuffleNotes(o2JamBeatmap, Seed.Value.Value);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(Algorithm));
        }
    }
}

public enum O2JamRandomAlgorithm
{
    [LocalisableDescription(typeof(O2LazerStrings), nameof(O2LazerStrings.RandomAlgorithmNative))]
    Native = 0,

    [LocalisableDescription(typeof(O2LazerStrings), nameof(O2LazerStrings.RandomAlgorithmO2Jam))]
    O2Jam = 1,

    [LocalisableDescription(typeof(O2LazerStrings), nameof(O2LazerStrings.RandomAlgorithmPanic))]
    Panic = 2,

    [LocalisableDescription(typeof(O2LazerStrings), nameof(O2LazerStrings.RandomAlgorithmRRandom))]
    RRandom = 3,

    [LocalisableDescription(typeof(O2LazerStrings), nameof(O2LazerStrings.RandomAlgorithmSRandom))]
    SRandom = 4,
}
