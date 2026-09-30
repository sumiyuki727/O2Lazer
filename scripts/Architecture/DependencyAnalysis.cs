using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace O2Lazer.Architecture;

public sealed record SourceInput(string Path, string[] Defines);
public sealed record AnalysisInput(SourceInput[] Sources, string[] References);
public sealed record Dependency(string Source, string From, string Target, string To, string Type, int Line);
public sealed record SharedType(string Type, string[] Sources);
public sealed record AnalysisResult(Dependency[] Dependencies, SharedType[] SharedTypes, string[] Errors);

public static class DependencyAnalysis
{
    public static string LayerFor(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        return parts[0] switch
        {
            "O2Jam.Core" => "Core",
            "O2Jam.Formats" => "Formats",
            "osu.Game.Rulesets.O2Lazer" when parts.Length == 2 && parts[1] == "O2LazerRuleset.cs" => "Composition",
            "osu.Game.Rulesets.O2Lazer" when parts.Length > 2 => parts[1] switch
            {
                "Integration" => "Integration",
                "Presentation" => "Presentation",
                "ManiaScore" => "ManiaScore",
                "Host" when path.Replace('\\', '/').Contains("/Host/Persistence/", StringComparison.Ordinal) => "Persistence",
                "Host" => "Host",
                "Composition" => "Composition",
                _ => throw new InvalidDataException($"Unclassified source: {path}"),
            },
            _ => throw new InvalidDataException($"Unclassified source: {path}"),
        };
    }

    public static AnalysisResult Analyse(string repositoryRoot, AnalysisInput input)
    {
        var trees = input.Sources.Select(source => CSharpSyntaxTree.ParseText(
            File.ReadAllText(source.Path),
            new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: source.Defines),
            source.Path)).ToArray();
        var references = input.References.Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        var compilation = CSharpCompilation.Create("osu.Game.Rulesets.O2Lazer", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable));
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                                .Select(diagnostic => diagnostic.ToString()).ToArray();
        var dependencies = new Dictionary<(string Source, string Target, string Type), Dependency>();
        var sharedTypes = new Dictionary<string, SharedType>();
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol type) continue;
                var paths = type.DeclaringSyntaxReferences.Select(reference => relative(reference.SyntaxTree.FilePath))
                                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                if (paths.Select(LayerFor).Distinct(StringComparer.Ordinal).Count() > 1)
                    sharedTypes[type.ToDisplayString()] = new SharedType(type.ToDisplayString(), paths);
            }
        }
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            var source = relative(tree.FilePath);
            var from = LayerFor(source);
            foreach (var node in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
            {
                // Namespace imports do not imply a dependency; aliases and static imports do.
                if (node.Ancestors().OfType<UsingDirectiveSyntax>().FirstOrDefault() is { Alias: null, StaticKeyword.RawKind: 0 }) continue;
                var symbol = model.GetSymbolInfo(node).Symbol;
                if (symbol is IAliasSymbol alias) symbol = alias.Target;
                if (symbol is IMethodSymbol method) symbol = method.PartialImplementationPart ?? method.ReducedFrom ?? method;
                var type = symbol as INamedTypeSymbol ?? symbol?.ContainingType;
                if (type != null) add(type, symbol, node);
                // Inferred locals and member return types still create a dependency on their source type.
                if (model.GetTypeInfo(node).Type is INamedTypeSymbol inferredType) add(inferredType, null, node);
            }

            void add(INamedTypeSymbol type, ISymbol? symbol, SyntaxNode node)
            {
                type = type.OriginalDefinition;
                var name = type.ToDisplayString();
                // A partial member's source is more precise than the containing type's multiple declarations.
                var declarations = symbol?.DeclaringSyntaxReferences is { Length: > 0 } memberDeclarations
                                   && symbol is not INamedTypeSymbol
                    ? memberDeclarations
                    : type.DeclaringSyntaxReferences;
                foreach (var reference in declarations)
                {
                    var target = relative(reference.SyntaxTree.FilePath);
                    var to = LayerFor(target);
                    if (from == to) continue;
                    // Partial declarations are checked explicitly; member calls across them remain visible.
                    if ((symbol == null || symbol is INamedTypeSymbol)
                        && SymbolEqualityComparer.Default.Equals(model.GetEnclosingSymbol(node.SpanStart)?.ContainingType?.OriginalDefinition, type)
                        && sharedTypes.ContainsKey(name)) continue;
                    var key = (source, target, name);
                    dependencies.TryAdd(key, new Dependency(source, from, target, to, name,
                        tree.GetLineSpan(node.Span).StartLinePosition.Line + 1));
                }
            }
        }
        return new AnalysisResult(dependencies.Values.OrderBy(edge => edge.Source, StringComparer.Ordinal)
                                              .ThenBy(edge => edge.Target, StringComparer.Ordinal)
                                              .ThenBy(edge => edge.Type, StringComparer.Ordinal).ToArray(),
            sharedTypes.Values.OrderBy(type => type.Type, StringComparer.Ordinal).ToArray(), errors);

        string relative(string path) => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
    }
}
