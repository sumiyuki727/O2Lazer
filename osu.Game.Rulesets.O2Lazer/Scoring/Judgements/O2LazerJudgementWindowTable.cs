using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Scoring.Judgements;

public sealed class O2LazerJudgementWindowTable
{
    private readonly O2LazerJudgementWindow[] hitWindows;
    private readonly O2LazerJudgementWindow? missWindow;
    private readonly double? goodFastDTime;
    private readonly double? passivePoorOffset;

    public O2LazerJudgementWindowTable(IEnumerable<O2LazerJudgementWindow> rows)
    {
        var allRows = rows as O2LazerJudgementWindow[] ?? rows.ToArray();
        var hitCount = 0;
        O2LazerJudgementWindow? miss = null;

        foreach (var row in allRows)
        {
            if (row.Result == HitResult.Miss)
                miss ??= row;
            else
                hitCount++;
        }

        var hits = new O2LazerJudgementWindow[hitCount];
        var hitIndex = 0;

        foreach (var row in allRows)
        {
            if (row.Result != HitResult.Miss)
            {
                hits[hitIndex++] = row;

                if (row.Result == HitResult.Good)
                    goodFastDTime ??= row.FastDTime;

                if (row.Result == HitResult.Ok)
                    passivePoorOffset ??= row.SlowOffset;
            }
        }

        hitWindows = hits;
        missWindow = miss;
    }

    public double GoodFastDTime => goodFastDTime ?? throw new InvalidOperationException("Sequence contains no matching element");

    public HitResult ResultForOffset(double timeOffset)
    {
        foreach (var row in hitWindows)
        {
            if (row.ContainsOffset(timeOffset))
                return row.Result;
        }

        return HitResult.None;
    }

    public bool IsEmptyPoorOffset(double timeOffset)
    {
        if (missWindow == null || !missWindow.Value.ContainsOffset(timeOffset))
            return false;

        return ResultForOffset(timeOffset) == HitResult.None;
    }

    public bool IsPastPassivePoorOffset(double timeOffset)
    {
        return timeOffset > (passivePoorOffset ?? throw new InvalidOperationException("Sequence contains no matching element"));
    }

    public double FrameworkWindowFor(HitResult result)
    {
        var row = rowFor(result);

        if (row == null)
            return 0;

        return Math.Min(Math.Abs(row.Value.SlowOffset), Math.Abs(row.Value.FastOffset));
    }

    public double SlowWindowFor(HitResult result)
    {
        var row = rowFor(result);
        return row?.SlowOffset ?? 0;
    }

    public double FastWindowFor(HitResult result)
    {
        var row = rowFor(result);
        return row?.FastOffset ?? 0;
    }

    private O2LazerJudgementWindow? rowFor(HitResult result)
    {
        if (result == HitResult.Miss)
            return missWindow;

        foreach (var row in hitWindows)
        {
            if (row.Result == result)
                return row;
        }

        return null;
    }
}
