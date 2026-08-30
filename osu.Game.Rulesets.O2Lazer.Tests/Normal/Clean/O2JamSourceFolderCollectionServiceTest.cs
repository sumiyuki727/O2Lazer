using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Rulesets.O2Lazer.Localisation;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamSourceFolderCollectionServiceTest
{
    [Test]
    public void BuildsDistinctCollectionsFromFoldersRelativeToLibraryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "o2jam-library");
        var plans = O2JamSourceFolderCollectionService.BuildPlans(root,
        [
            new O2JamSourceFolderBeatmap(Path.Combine(root, "Pack A", "Song"), "ex"),
            new O2JamSourceFolderBeatmap(Path.Combine(root, "Pack A", "Song"), "nx"),
            new O2JamSourceFolderBeatmap(Path.Combine(root, "Pack B", "Song"), "hx"),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(plans.Select(plan => plan.Name), Is.EqualTo(new[]
            {
                O2LazerStrings.SourceFolderCollectionName("Pack A/Song").ToString(),
                O2LazerStrings.SourceFolderCollectionName("Pack B/Song").ToString(),
            }));
            Assert.That(plans[0].Hashes, Is.EquivalentTo(new[] { "ex", "nx" }));
            Assert.That(plans[1].Hashes, Is.EquivalentTo(new[] { "hx" }));
        });
    }

    [Test]
    public void NewCollectionAndLongNoteOptionsDefaultOff()
    {
        using var config = new O2JamRulesetConfigManager(null, new RulesetInfo { ShortName = "o2lazer" });

        Assert.Multiple(() =>
        {
            Assert.That(config.Get<bool>(O2JamRulesetSetting.SyncSourceFolderCollections), Is.False);
            Assert.That(config.Get<bool>(O2JamRulesetSetting.O2JamStyleDroppedHold), Is.False);
        });
    }
}
