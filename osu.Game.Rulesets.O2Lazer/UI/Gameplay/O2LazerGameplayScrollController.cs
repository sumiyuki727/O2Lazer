using System;
using System.Linq;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.UI.Scrolling;

namespace osu.Game.Rulesets.O2Lazer.UI.Gameplay;

internal sealed class O2LazerGameplayScrollController(O2LazerTimingMap? timingMap)
{
    public const double MAX_TIME_RANGE = 11485;

    public O2LazerTimingMap? TimingMap { get; } = timingMap;

    public double ScrollRange => ComputeScrollTime(default_scroll_speed) * ScrollRangeScale * PlaybackRate;

    public double ScrollSpeedMultiplier => ScrollSpeed / default_scroll_speed * ChartSpeedFactor;

    public double MeasureLineFutureWindow
    {
        get
        {
            var multiplier = Math.Abs(ScrollSpeedMultiplier);

            if (!double.IsFinite(multiplier) || multiplier < 0.001)
                return 30000;

            return Math.Max(500, ScrollRange / multiplier);
        }
    }

    public bool ConstantScrollActive { get; set; }

    public double ScrollSpeed { get; private set; } = default_scroll_speed;

    public double CurrentScrollPosition { get; private set; }

    public double ChartSpeedFactor { get; private set; } = 1.0;

    public double ScrollRangeScale { get; private set; } = 1.0;

    public double PlaybackRate { get; private set; } = 1.0;

    public ScrollingDirection Direction { get; set; } = ScrollingDirection.Down;

    private const double default_scroll_speed = O2LazerRulesetConfigManager.DEFAULT_SCROLL_SPEED;
    private double configuredScrollSpeed = default_scroll_speed;

    public static double ComputeScrollTime(double scrollSpeed) => MAX_TIME_RANGE / Math.Max(1, scrollSpeed);

    // Classic O2Jam hi-speed options. X1 is the reference speed, so the ruleset's default scroll speed maps to X1.
    private static readonly (double multiplier, string label)[] o2jam_speed_grades =
    [
        (0.5, "X0.5"),
        (1.0, "X1"),
        (1.5, "X1.5"),
        (2.0, "X2"),
        (2.5, "X2.5"),
        (3.0, "X3"),
        (4.0, "X4"),
        (5.0, "X5"),
        (6.0, "X6"),
        (8.0, "X8"),
    ];

    public static string GetO2JamSpeedGrade(double scrollSpeed)
    {
        var multiplier = scrollSpeed / default_scroll_speed;
        return o2jam_speed_grades.MinBy(grade => Math.Abs(grade.multiplier - multiplier)).label;
    }

    public void SetHitTargetPosition(float hitTargetPosition)
    {
        const float reference_scroll_distance = 768f - 124.8f;
        ScrollRangeScale = (768f - hitTargetPosition) / reference_scroll_distance;
    }

    public void SetConfiguredScrollSpeed(double speed)
    {
        configuredScrollSpeed = speed;
        ScrollSpeed = configuredScrollSpeed;
        ScrollSpeedChanged?.Invoke(configuredScrollSpeed / default_scroll_speed);
    }

    public void SetPlaybackRate(double rate)
    {
        PlaybackRate = double.IsFinite(rate) && Math.Abs(rate) >= 0.001 ? Math.Abs(rate) : 1.0;
    }

    public void Update(double currentTime)
    {
        CurrentScrollPosition = ConstantScrollActive
            ? currentTime
            : TimingMap?.GetScrollPositionAtTime(currentTime) ?? currentTime;

        ChartSpeedFactor = ConstantScrollActive
            ? 1.0
            : TimingMap?.GetSpeedFactorAtTime(currentTime) ?? 1.0;
    }

    public double GetVisualScrollPosition(double time, double mappedScrollPosition) =>
        ConstantScrollActive ? time : mappedScrollPosition;

    public float YForScrollProgress(double progress, double parentHeight, double hitTargetPosition, double noteHeight = 0)
        => Direction == ScrollingDirection.Up
            ? (float)(hitTargetPosition + progress * ScrollCoordinateScale(parentHeight, hitTargetPosition) - noteHeight)
            : (float)(parentHeight - hitTargetPosition - progress * ScrollCoordinateScale(parentHeight, hitTargetPosition) - noteHeight);

    public double ScrollCoordinateScale(double parentHeight, double hitTargetPosition)
    {
        var range = Math.Max(1.0, ScrollRange);
        var travelDistance = Math.Max(1f, (float)(parentHeight - hitTargetPosition));
        return ScrollSpeedMultiplier / range * travelDistance;
    }

    public event Action<double>? ScrollSpeedChanged;
}
