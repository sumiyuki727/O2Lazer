using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Scoring.Judgements;

public static class O2LazerJudgementSelector
{
    private static readonly CandidateComparer candidate_comparer = new();

    public static O2LazerJudgementSelection SelectPress(
        O2LazerLayoutVariant layout,
        int column,
        IEnumerable<O2LazerJudgementCandidate> candidates,
        double inputTime)
    {
        var initialCapacity = candidates.TryGetNonEnumeratedCount(out var candidateCount)
            ? Math.Max(1, candidateCount)
            : 16;
        var sortedCandidates = ArrayPool<O2LazerJudgementCandidate>.Shared.Rent(initialCapacity);
        var count = 0;

        try
        {
            foreach (var candidate in candidates)
            {
                if (count == sortedCandidates.Length)
                {
                    var expanded = ArrayPool<O2LazerJudgementCandidate>.Shared.Rent(sortedCandidates.Length * 2);
                    Array.Copy(sortedCandidates, expanded, count);
                    ArrayPool<O2LazerJudgementCandidate>.Shared.Return(sortedCandidates);
                    sortedCandidates = expanded;
                }

                sortedCandidates[count++] = candidate;
            }

            Array.Sort(sortedCandidates, 0, count, candidate_comparer);
            return selectSorted(layout, column, sortedCandidates, count, inputTime);
        }
        finally
        {
            ArrayPool<O2LazerJudgementCandidate>.Shared.Return(sortedCandidates);
        }
    }

    private static O2LazerJudgementSelection selectSorted(
        O2LazerLayoutVariant layout,
        int column,
        O2LazerJudgementCandidate[] candidates,
        int count,
        double inputTime)
    {
        O2LazerJudgementCandidate? selected = null;
        HitResult selectedResult = HitResult.None;
        O2LazerJudgementCandidate? emptyPoorCandidate = null;

        for (var i = 0; i < count; i++)
        {
            var candidate = candidates[i];
            var table = O2LazerJudgementProfileProvider.GetTable(layout, candidate.Column, candidate.JudgementRate, tail: false);
            var offset = inputTime - candidate.StartTime;
            var result = table.ResultForOffset(offset);

            if (result != HitResult.None)
            {
                if (selected == null || shouldReplaceSelected(selected.Value, candidate, inputTime, selectedResult, result, table.GoodFastDTime))
                {
                    selected = candidate;
                    selectedResult = result;
                }

                continue;
            }

            if (candidate.Column == column && table.IsEmptyPoorOffset(offset))
                emptyPoorCandidate ??= candidate;
        }

        if (selected != null)
            return new O2LazerJudgementSelection(selected, selectedResult, false);

        return emptyPoorCandidate != null
            ? new O2LazerJudgementSelection(emptyPoorCandidate, HitResult.Miss, true)
            : new O2LazerJudgementSelection(null, HitResult.None, false);
    }

    private static bool shouldReplaceSelected(
        O2LazerJudgementCandidate current,
        O2LazerJudgementCandidate next,
        double inputTime,
        HitResult currentResult,
        HitResult nextResult,
        double goodFastDTime)
    {
        var nextOffset = inputTime - next.StartTime;
        var currentDTime = current.StartTime - inputTime;

        if (currentDTime < -goodFastDTime && nextResult is HitResult.Perfect or HitResult.Great or HitResult.Good)
            return true;

        if (currentResult is HitResult.Ok or HitResult.Meh && nextResult is HitResult.Perfect or HitResult.Great or HitResult.Good)
            return true;

        return currentResult is HitResult.Ok or HitResult.Meh
               && nextResult is HitResult.Ok or HitResult.Meh
               && System.Math.Abs(nextOffset) < System.Math.Abs(inputTime - current.StartTime);
    }

    private sealed class CandidateComparer : IComparer<O2LazerJudgementCandidate>
    {
        public int Compare(O2LazerJudgementCandidate x, O2LazerJudgementCandidate y)
        {
            var startTimeComparison = x.StartTime.CompareTo(y.StartTime);
            return startTimeComparison != 0 ? startTimeComparison : x.Column.CompareTo(y.Column);
        }
    }
}
