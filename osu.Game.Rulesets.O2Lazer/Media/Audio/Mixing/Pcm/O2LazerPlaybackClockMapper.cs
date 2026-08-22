using System;
using osu.Game.Rulesets.O2Lazer.Media.Audio.Processing;

namespace osu.Game.Rulesets.O2Lazer.Media.Audio.Mixing.Pcm;

internal sealed class O2LazerPlaybackClockMapper
{
    private readonly double rate;
    private double originChartTime;
    private long originOutputFrame;

    internal O2LazerPlaybackClockMapper(double rate)
    {
        if (!double.IsFinite(rate) || rate < 0.05 || rate > 2)
            throw new ArgumentOutOfRangeException(nameof(rate));

        this.rate = rate;
    }

    internal void Rebase(double chartTime, long outputFrame)
    {
        if (!double.IsFinite(chartTime))
            throw new ArgumentOutOfRangeException(nameof(chartTime));

        originChartTime = chartTime;
        originOutputFrame = Math.Max(0, outputFrame);
    }

    internal long Map(double chartTime, long renderedFrame)
    {
        if (!double.IsFinite(chartTime))
            return Math.Max(0, renderedFrame);

        var exactFrame = originOutputFrame + (chartTime - originChartTime) * O2LazerFixedRatePcmProcessor.OUTPUT_SAMPLE_RATE / (1000 * rate);
        var mappedFrame = (long)Math.Round(exactFrame, MidpointRounding.AwayFromZero);
        return Math.Max(Math.Max(0, mappedFrame), renderedFrame);
    }

    internal long MapSourceOffset(double chartOffsetMilliseconds)
    {
        if (!double.IsFinite(chartOffsetMilliseconds) || chartOffsetMilliseconds <= 0)
            return 0;

        return Math.Max(0, (long)Math.Round(chartOffsetMilliseconds * O2LazerFixedRatePcmProcessor.OUTPUT_SAMPLE_RATE / (1000 * rate), MidpointRounding.AwayFromZero));
    }
}
