using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace O2Lazer.Architecture;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length is < 4 or > 5 || args.Length == 5 && args[4] != "--report-only")
            throw new ArgumentException("Expected repository root, input, policy, report path and optional --report-only.");
        var options = new JsonSerializerOptions { WriteIndented = true };
        var input = JsonSerializer.Deserialize<AnalysisInput>(File.ReadAllText(args[1]))
                    ?? throw new InvalidDataException("Missing analysis input.");
        var policy = JsonSerializer.Deserialize<DependencyPolicy>(File.ReadAllText(args[2]))
                     ?? throw new InvalidDataException("Missing dependency policy.");
        var normal = DependencyAnalysis.Analyse(Path.GetFullPath(args[0]), input);
        // Optional diagnostics are production sources too; a disabled block must not hide a new dependency.
        var diagnosticInput = input with
        {
            Sources = input.Sources.Select(source => source with
            {
                Defines = source.Defines.Append("O2JAM_SYNC_DIAGNOSTICS").Distinct(StringComparer.Ordinal).ToArray(),
            }).ToArray(),
        };
        var diagnostics = DependencyAnalysis.Analyse(Path.GetFullPath(args[0]), diagnosticInput);
        var analysis = new AnalysisResult(normal.Dependencies.Concat(diagnostics.Dependencies)
                                                .DistinctBy(edge => (edge.Source, edge.Target, edge.Type)).ToArray(),
            normal.SharedTypes.Concat(diagnostics.SharedTypes).GroupBy(type => type.Type, StringComparer.Ordinal)
                  .Select(group => new SharedType(group.Key, group.SelectMany(type => type.Sources).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray())).ToArray(),
            normal.Errors.Select(error => $"Production: {error}").Concat(diagnostics.Errors.Select(error => $"Diagnostics: {error}")).ToArray());
        var validation = PolicyValidation.Validate(analysis, policy);
        File.WriteAllText(args[3], JsonSerializer.Serialize(new { SourceFiles = input.Sources.Length, Analysis = analysis, Validation = validation }, options));
        foreach (var group in analysis.Dependencies.GroupBy(edge => (edge.From, edge.To)).OrderBy(group => group.Key.From).ThenBy(group => group.Key.To))
            Console.WriteLine($"{group.Key.From} -> {group.Key.To}: {group.Count()} file/type edges");
        Console.WriteLine($"Cross-layer partials: {analysis.SharedTypes.Length}; compilation errors: {analysis.Errors.Length}; violations: {validation.Violations.Length}; stale exceptions: {validation.UnusedExceptions.Length}");
        foreach (var error in analysis.Errors.Take(10).Concat(validation.Violations).Concat(validation.UnusedExceptions)) Console.Error.WriteLine(error);
        return analysis.Errors.Length > 0 || args.Length != 5 && (validation.Violations.Length > 0 || validation.UnusedExceptions.Length > 0) ? 1 : 0;
    }
}
