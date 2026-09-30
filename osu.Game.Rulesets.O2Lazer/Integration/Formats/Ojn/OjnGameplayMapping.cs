using System;
using O2Jam.Core;
using O2Jam.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;

internal static class OjnGameplayMapping
{
    internal static O2JamDifficulty ToGameplay(this OjnDifficulty difficulty) => difficulty switch
    {
        OjnDifficulty.EX => O2JamDifficulty.EX,
        OjnDifficulty.NX => O2JamDifficulty.NX,
        OjnDifficulty.HX => O2JamDifficulty.HX,
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty)),
    };

    internal static OjnDifficulty ToFormat(this O2JamDifficulty difficulty) => difficulty switch
    {
        O2JamDifficulty.EX => OjnDifficulty.EX,
        O2JamDifficulty.NX => OjnDifficulty.NX,
        O2JamDifficulty.HX => OjnDifficulty.HX,
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty)),
    };

    internal static O2JamBpmEvent ToGameplay(this OjnBpmEvent change) => new(change.Position, change.Bpm);
}
