using System;
using System.Linq;
using osu.Framework.Extensions.IEnumerableExtensions;
using osu.Framework.Localisation;
using osu.Framework.Utils;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.O2Lazer.Mods;

public class O2LazerModRandom : ModRandom, IApplicableToBeatmap
{
    public override LocalisableString Description => "Shuffle around the keys!";

    public override Type[] IncompatibleMods => [.. base.IncompatibleMods, typeof(O2LazerModMirror)];

    public void ApplyToBeatmap(IBeatmap beatmap)
    {
        if (beatmap is not O2LazerBeatmap o2lazerBeatmap)
            return;

        Seed.Value ??= RNG.Next();
        var rng = new Random(Seed.Value.Value);
        var shuffledColumns = Enumerable.Range(0, o2lazerBeatmap.TotalColumns).OrderBy(_ => rng.Next()).ToArray();

        o2lazerBeatmap.HitObjects.OfType<O2LazerHitObject>().ForEach(hitObject => hitObject.Column = shuffledColumns[hitObject.Column]);
    }
}
