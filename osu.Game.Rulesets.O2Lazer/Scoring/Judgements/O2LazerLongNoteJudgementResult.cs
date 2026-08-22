using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.O2Lazer.Scoring.Judgements;

public sealed class O2LazerLongNoteJudgementResult : JudgementResult
{
    public IReadOnlyList<O2LazerLongNoteEndpointResult> EndpointResults { get; private set; } = Array.Empty<O2LazerLongNoteEndpointResult>();

    internal O2LazerLongNoteJudgementResult(HitObject hitObject, Judgement judgement)
        : base(hitObject, judgement)
    {
    }

    public O2LazerLongNoteJudgementResult(
        HitObject hitObject,
        Judgement judgement,
        IReadOnlyList<O2LazerLongNoteEndpointResult> endpointResults)
        : base(hitObject, judgement)
    {
        setEndpointResults(endpointResults);
    }

    internal void SetEndpointResults(IReadOnlyList<O2LazerLongNoteEndpointResult> endpointResults)
    {
        if (HasResult)
            throw new InvalidOperationException("Long-note endpoints cannot change after the result is applied.");

        // The framework reuses this result after Reset() when gameplay rewinds and replays the drawable.
        setEndpointResults(endpointResults);
    }

    private void setEndpointResults(IReadOnlyList<O2LazerLongNoteEndpointResult> endpointResults)
    {
        // Committed replay data must not change when the controller reuses its working collection.
        var endpoints = endpointResults.ToArray();

        if (endpoints.Length == 0)
            throw new ArgumentException("At least one long-note endpoint is required.", nameof(endpointResults));

        var source = endpoints[0].Source;

        if (endpoints.Any(e => !ReferenceEquals(e.Source, source)))
            throw new ArgumentException("All endpoints must belong to the same long note.", nameof(endpointResults));

        if (endpoints.Select(e => e.Kind).Distinct().Count() != endpoints.Length)
            throw new ArgumentException("Endpoint kinds cannot be duplicated.", nameof(endpointResults));

        EndpointResults = endpoints;
    }
}
