using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Replays;

namespace osu.Game.Rulesets.O2Lazer.Mods;

/// <summary>
/// Supplies the native autoplay contract used by song select, the editor and the skin editor.
/// </summary>
public sealed class O2JamModAutoplay : ModAutoplay
{
    public override ModReplayData CreateReplayData(IBeatmap beatmap, IReadOnlyList<Mod> mods) =>
        new(new O2JamAutoGenerator((O2JamBeatmap)beatmap).Generate(), new ModCreatedUser { Username = "osu!topus" });
}
