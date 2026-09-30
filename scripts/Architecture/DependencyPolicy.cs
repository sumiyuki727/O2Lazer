using System;
using System.Collections.Generic;
using System.Linq;

namespace O2Lazer.Architecture;

public sealed record DependencyException(string Source, string Target, string Type, string Reason);
public sealed record SharedTypeException(string Type, string[] Sources, string Reason);
public sealed record DependencyPolicy(Dictionary<string, string[]> Allowed, DependencyException[] Exceptions, SharedTypeException[] SharedTypes);
public sealed record PolicyResult(string[] Violations, string[] UnusedExceptions);

public static class PolicyValidation
{
    public static PolicyResult Validate(AnalysisResult analysis, DependencyPolicy policy)
    {
        var violations = new List<string>();
        var used = new HashSet<DependencyException>();
        foreach (var edge in analysis.Dependencies)
        {
            if (policy.Allowed.TryGetValue(edge.From, out var allowed) && allowed.Contains(edge.To, StringComparer.Ordinal)) continue;
            var exception = policy.Exceptions.SingleOrDefault(item => item.Source == edge.Source && item.Target == edge.Target && item.Type == edge.Type);
            if (exception != null && !string.IsNullOrWhiteSpace(exception.Reason)) used.Add(exception);
            else violations.Add($"{edge.Source}:{edge.Line}: {edge.From} -> {edge.To}: {edge.Type}");
        }
        var sharedUsed = new HashSet<SharedTypeException>();
        foreach (var type in analysis.SharedTypes)
        {
            var exception = policy.SharedTypes.SingleOrDefault(item => item.Type == type.Type);
            if (exception != null && !string.IsNullOrWhiteSpace(exception.Reason)
                && exception.Sources.Order(StringComparer.Ordinal).SequenceEqual(type.Sources, StringComparer.Ordinal))
                sharedUsed.Add(exception);
            else violations.Add($"Unapproved cross-layer partial: {type.Type} ({string.Join(", ", type.Sources)})");
        }
        var unused = policy.Exceptions.Except(used).Select(item => $"Unused dependency exception: {item.Source} -> {item.Type}")
                           .Concat(policy.SharedTypes.Except(sharedUsed).Select(item => $"Unused partial exception: {item.Type}")).ToArray();
        return new PolicyResult(violations.ToArray(), unused);
    }
}
