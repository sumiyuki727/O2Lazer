using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using O2Lazer.Architecture;

namespace O2Lazer.Architecture.Tests.Normal;

[TestFixture]
public class DependencyAnalysisTest
{
    private string root = null!;
    private readonly List<SourceInput> sources = [];

    [SetUp]
    public void SetUp()
    {
        root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "o2lazer-architecture-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        sources.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        var prefix = Path.GetFullPath(Path.GetTempPath()) + "o2lazer-architecture-";
        if (!Path.GetFullPath(root).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unsafe fixture cleanup target.");
        Directory.Delete(root, true);
    }

    [Test]
    public void SharedNamespaceDoesNotHideAnUpwardReference()
    {
        add("osu.Game.Rulesets.O2Lazer/Host/Service.cs", "namespace Shared; public class Service { public static int Read() => 1; }");
        add("osu.Game.Rulesets.O2Lazer/Integration/Consumer.cs", "namespace Shared; public class Consumer { public int Value => Service.Read(); }");

        var result = analyse();
        Assert.That(result.Dependencies, Has.One.Matches<Dependency>(edge => edge.From == "Integration" && edge.To == "Host" && edge.Type == "Shared.Service"));
        Assert.That(PolicyValidation.Validate(result, policy()).Violations, Has.Length.EqualTo(1));
    }

    [Test]
    public void NamespaceImportsCommentsAndShadowedNamesDoNotCreateEdges()
    {
        add("osu.Game.Rulesets.O2Lazer/Host/Service.cs", "namespace Shared; public class Service { }");
        add("osu.Game.Rulesets.O2Lazer/Integration/Consumer.cs", """
            using Shared;
            namespace Other;
            // Service belongs to Host, but this local does not.
            public class Consumer { public int Read() { var Service = 1; return Service; } }
            """);

        Assert.That(analyse().Dependencies, Is.Empty);
    }

    [TestCase("using S = Shared.Service;", "S.Read()")]
    [TestCase("using static Shared.Service;", "Read()")]
    [TestCase("", "global::Shared.Service.Read()")]
    public void AliasesStaticImportsAndQualifiedNamesResolveToTheSourceOwner(string import, string expression)
    {
        add("osu.Game.Rulesets.O2Lazer/Host/Service.cs", "namespace Shared; public class Service { public static int Read() => 1; }");
        add("osu.Game.Rulesets.O2Lazer/Integration/Consumer.cs", import + " namespace Other; public class Consumer { public int Value => " + expression + "; }");

        Assert.That(analyse().Dependencies, Has.One.Matches<Dependency>(edge => edge.Type == "Shared.Service" && edge.To == "Host"));
    }

    [Test]
    public void GenericInheritanceAndInferredReturnTypesAreIncluded()
    {
        add("O2Jam.Core/Data.cs", "namespace Shared; public class Data { }");
        add("osu.Game.Rulesets.O2Lazer/Host/Service.cs", """
            namespace Shared;
            public class Base<T> { }
            public class Service { public static Data Create() => new(); }
            """);
        add("osu.Game.Rulesets.O2Lazer/Integration/Consumer.cs", """
            namespace Shared;
            public class Consumer : Base<Data> { public object Read() { var value = Service.Create(); return value; } }
            """);

        var result = analyse();
        Assert.Multiple(() =>
        {
            Assert.That(result.Dependencies, Has.Some.Matches<Dependency>(edge => edge.Source.EndsWith("/Consumer.cs") && edge.Type == "Shared.Base<T>" && edge.To == "Host"));
            Assert.That(result.Dependencies, Has.Some.Matches<Dependency>(edge => edge.Source.EndsWith("/Consumer.cs") && edge.Type == "Shared.Data" && edge.To == "Core"));
        });
    }

    [Test]
    public void ConditionalCompilationUsesTheSuppliedProfile()
    {
        add("osu.Game.Rulesets.O2Lazer/Host/Service.cs", "namespace Shared; public class Service { }");
        var code = """
            namespace Shared;
            public class Consumer {
            #if FEATURE
                public Service? Value;
            #endif
            }
            """;
        add("osu.Game.Rulesets.O2Lazer/Integration/Consumer.cs", code);
        Assert.That(analyse().Dependencies, Is.Empty);
        sources[1] = sources[1] with { Defines = ["FEATURE"] };
        Assert.That(analyse().Dependencies, Has.Length.EqualTo(1));
    }

    [Test]
    public void PartialMembersUseTheirDeclarationFileAndRequireAnExplicitSplit()
    {
        add("osu.Game.Rulesets.O2Lazer/Host/Part.cs", "namespace Shared; public partial class Part { public int Value = 1; }");
        add("osu.Game.Rulesets.O2Lazer/Presentation/Part.cs", "namespace Shared; public partial class Part { public int Read() => Value; }");

        var result = analyse();
        Assert.Multiple(() =>
        {
            Assert.That(result.SharedTypes, Has.One.Matches<SharedType>(type => type.Type == "Shared.Part" && type.Sources.Length == 2));
            Assert.That(result.Dependencies, Has.One.Matches<Dependency>(edge => edge.From == "Presentation" && edge.To == "Host"));
            Assert.That(PolicyValidation.Validate(result, policy()).Violations, Has.Some.Contains("Unapproved cross-layer partial"));
        });
    }

    [Test]
    public void PartialMethodCallsPointToTheImplementation()
    {
        add("osu.Game.Rulesets.O2Lazer/Host/Part.cs", "namespace Shared; public partial class Part { public partial int Read(); public int Call() => Read(); }");
        add("osu.Game.Rulesets.O2Lazer/Presentation/Part.cs", "namespace Shared; public partial class Part { public partial int Read() => 1; }");

        Assert.That(analyse().Dependencies, Has.Some.Matches<Dependency>(edge => edge.From == "Host" && edge.To == "Presentation"));
    }

    [Test]
    public void UnresolvedSymbolsAreReportedAsErrors()
    {
        add("O2Jam.Core/Broken.cs", "public class Broken { public Missing Value; }");
        var result = DependencyAnalysis.Analyse(root, input());
        Assert.That(result.Errors, Is.Not.Empty);
    }

    [Test]
    public void ExceptionsCannotCoverAnotherTypeOrOutliveTheDependency()
    {
        add("osu.Game.Rulesets.O2Lazer/Host/Service.cs", "namespace Shared; public class Service { public static int Read() => 1; }");
        add("osu.Game.Rulesets.O2Lazer/Integration/Consumer.cs", "namespace Shared; public class Consumer { public int Value => Service.Read(); }");
        var result = analyse();
        var edge = result.Dependencies.Single();
        var exception = new DependencyException(edge.Source, edge.Target, edge.Type, "Native API seam.");
        var allowed = policy() with { Exceptions = [exception] };

        Assert.Multiple(() =>
        {
            Assert.That(PolicyValidation.Validate(result, allowed).Violations, Is.Empty);
            Assert.That(PolicyValidation.Validate(result, allowed).UnusedExceptions, Is.Empty);
            Assert.That(PolicyValidation.Validate(result, allowed with { Exceptions = [exception with { Type = "Another.Type" }] }).Violations, Is.Not.Empty);
            Assert.That(PolicyValidation.Validate(result with { Dependencies = [] }, allowed).UnusedExceptions, Has.Length.EqualTo(1));
        });
    }

    [Test]
    public void PartialExceptionsRejectAnAdditionalDeclaration()
    {
        add("osu.Game.Rulesets.O2Lazer/Host/Part.cs", "namespace Shared; public partial class Part { }");
        add("osu.Game.Rulesets.O2Lazer/Presentation/Part.cs", "namespace Shared; public partial class Part { }");
        var result = analyse();
        var split = result.SharedTypes.Single();
        var allowed = policy() with { SharedTypes = [new SharedTypeException(split.Type, split.Sources, "Native composition seam.")] };
        Assert.That(PolicyValidation.Validate(result, allowed).Violations, Is.Empty);

        add("osu.Game.Rulesets.O2Lazer/Host/Persistence/Realm/Part.cs", "namespace Shared; public partial class Part { }");
        Assert.That(PolicyValidation.Validate(analyse(), allowed).Violations, Is.Not.Empty);
    }

    [TestCase("osu.Game.Rulesets.O2Lazer/Unknown/Type.cs")]
    [TestCase("Unknown/Type.cs")]
    [TestCase("osu.Game.Rulesets.O2Lazer/Unowned.cs")]
    public void UnclassifiedSourcesFailTheInspection(string path) =>
        Assert.Throws<InvalidDataException>(() => DependencyAnalysis.LayerFor(path));

    private void add(string path, string code)
    {
        var absolute = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllText(absolute, code);
        sources.Add(new SourceInput(absolute, []));
    }

    private AnalysisInput input() => new(sources.ToArray(), [typeof(object).Assembly.Location]);

    private AnalysisResult analyse()
    {
        var result = DependencyAnalysis.Analyse(root, input());
        Assert.That(result.Errors, Is.Empty, string.Join(Environment.NewLine, result.Errors));
        return result;
    }

    private static DependencyPolicy policy() => new(new Dictionary<string, string[]>
    {
        ["Core"] = [],
        ["Integration"] = ["Core"],
        ["Host"] = ["Core", "Integration"],
        ["Presentation"] = ["Core", "Integration", "Host"],
    }, [], []);
}
