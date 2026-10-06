namespace O2Jam.Core;

/// <summary>
/// Continuous tick bounds around the charted endpoint; timing precision is never reduced.
/// </summary>
public readonly record struct O2JamJudgementWindow(double EarlyTicks, double LateTicks)
{
    private const double boundary_epsilon = 1e-7;

    public bool Contains(double offsetTicks) =>
        // TimingMap inversion may return an exact chart boundary a few ulps to either side.
        // The same tolerance includes the early edge and excludes the new late edge.
        offsetTicks >= -EarlyTicks - boundary_epsilon
        && offsetTicks < LateTicks - boundary_epsilon;
}
