using System;
using System.Linq;
using NUnit.Framework;
using O2Jam.Core;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Core;

[TestFixture]
public class CoreBoundaryTest
{
    [Test]
    public void GameplayAssemblyDoesNotReferenceHostOrFormats()
    {
        var assembly = typeof(O2JamGameplayState).Assembly;

        Assert.Multiple(() =>
        {
            Assert.That(assembly.GetName().Name, Is.EqualTo("O2Jam.Core"));
            Assert.That(assembly.GetReferencedAssemblies().Select(reference => reference.Name),
                Has.All.Matches<string>(name => name == "netstandard" || name.StartsWith("System", StringComparison.Ordinal)));
            Assert.That(assembly.GetExportedTypes().Select(type => type.Namespace), Has.All.EqualTo("O2Jam.Core"));
        });
    }
}
