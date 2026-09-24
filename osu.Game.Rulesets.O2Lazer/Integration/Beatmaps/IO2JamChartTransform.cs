namespace osu.Game.Rulesets.O2Lazer.Beatmaps;

/// <summary>
/// Transforms an immutable chart before gameplay. Mirror and Random can implement this contract
/// without introducing mod branches into judgement or drawable code.
/// </summary>
public interface IO2JamChartTransform<TChart>
{
    TChart Transform(TChart chart);
}
