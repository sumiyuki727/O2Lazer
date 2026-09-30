using System;
using System.Linq;
using NUnit.Framework;
using O2Jam.Core;
using O2Jam.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Host.Library;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Presentation.DifficultyLevels.Policy;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamModuleIdentityTest
{
    [Test]
    public void SingleDllDeploymentRetainsStandalonePublicApis()
    {
        var assembly = typeof(O2LazerRuleset).Assembly;
        var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        var coreTypes = assembly.GetExportedTypes().Where(type => type.Namespace == "O2Jam.Core").ToArray();
        var formatTypes = assembly.GetExportedTypes().Where(type => type.Namespace?.StartsWith("O2Jam.Formats.", StringComparison.Ordinal) == true).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(typeof(O2JamGameplayState).Assembly, Is.SameAs(assembly));
            Assert.That(typeof(OjnReader).Assembly, Is.SameAs(assembly));
            Assert.That(coreTypes, Does.Contain(typeof(O2JamGameplayState)));
            Assert.That(formatTypes, Does.Contain(typeof(OjnReader)));
            Assert.That(references, Does.Not.Contain("O2Jam.Core"));
            Assert.That(references, Does.Not.Contain("O2Jam.Formats"));
            Assert.That(assembly.GetTypes().Any(type => type.Namespace?.StartsWith("osu.Game.Rulesets.O2Lazer.Core", StringComparison.Ordinal) == true
                                                      || type.Namespace?.StartsWith("osu.Game.Rulesets.O2Lazer.Formats", StringComparison.Ordinal) == true), Is.False);
        });
    }

    [Test]
    public void HostPoliciesAndFormatBridgeHaveExplicitOwners()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(O2JamBeatmapIdentity).Namespace, Is.EqualTo("osu.Game.Rulesets.O2Lazer.Host.Library"));
            Assert.That(typeof(O2JamDifficultyRating).Namespace, Is.EqualTo("osu.Game.Rulesets.O2Lazer.Presentation.DifficultyLevels.Policy"));
            Assert.That(typeof(OjnBeatmapFactory).Namespace, Is.EqualTo("osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn"));
        });
    }
}
