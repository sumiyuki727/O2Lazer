using System;
using System.Linq;
using System.Globalization;
using NUnit.Framework;
using O2Jam.Formats.Ojn;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.O2Lazer.Configuration;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamLegacyLibraryMigrationTest
{
    [TestCase("Automatic", OjnMetadataEncoding.Automatic)]
    [TestCase("0", OjnMetadataEncoding.Automatic)]
    [TestCase("Gbk", OjnMetadataEncoding.Gbk)]
    [TestCase("1", OjnMetadataEncoding.Gbk)]
    [TestCase("Cp949", OjnMetadataEncoding.Cp949)]
    [TestCase("2", OjnMetadataEncoding.Cp949)]
    [TestCase("Utf8", OjnMetadataEncoding.Utf8)]
    [TestCase("3", OjnMetadataEncoding.Utf8)]
    [Category("Isolated")]
    public void LegacySettingsRemainReadableWithoutRevivingManualEncoding(string encoding, OjnMetadataEncoding expected) =>
        runProjectionTest((realm, storage) =>
        {
            var info = new O2LazerRuleset().RulesetInfo;
            // Literal keys model an existing installation, so a rename cannot silently update the fixture.
            (string Key, string Value)[] original =
            [
                ("LastImportPath", storage.GetFullPath("library")),
                ("ScrollSpeed", "16"),
                ("ScrollDirection", "Up"),
                ("SyncSourceFolderCollections", "True"),
                ("O2JamStyleDroppedHold", "False"),
                ("PercyLongNoteBodyRepeat", "False"),
                ("MetadataEncoding", encoding),
            ];
            realm.Write(database =>
            {
                foreach (var entry in original)
                    database.Add(new RealmRulesetSetting
                    {
                        RulesetName = info.ShortName,
                        Key = entry.Key,
                        Value = entry.Value,
                    });
                database.Add(new RealmRulesetSetting
                {
                    RulesetName = info.ShortName,
                    Variant = 1,
                    Key = "MetadataEncoding",
                    Value = "Utf8",
                });
                database.Add(new RealmRulesetSetting
                {
                    RulesetName = "other",
                    Key = "MetadataEncoding",
                    Value = "Gbk",
                });
            });

            using (var config = new O2JamRulesetConfigManager(new SettingsStore(realm), info))
            {
                Assert.Multiple(() =>
                {
                    Assert.That(config.Get<string>(O2JamRulesetSetting.LastImportPath), Is.EqualTo(original[0].Value));
                    Assert.That(config.Get<double>(O2JamRulesetSetting.ScrollSpeed), Is.EqualTo(16));
                    Assert.That(config.Get<ManiaScrollingDirection>(O2JamRulesetSetting.ScrollDirection), Is.EqualTo(ManiaScrollingDirection.Up));
                    Assert.That(config.Get<bool>(O2JamRulesetSetting.SyncSourceFolderCollections), Is.True);
                    Assert.That(config.Get<bool>(O2JamRulesetSetting.O2JamStyleDroppedHold), Is.False);
                    Assert.That(config.Get<bool>(O2JamRulesetSetting.PercyLongNoteBodyRepeat), Is.False);
                    Assert.That(config.Get<OjnMetadataEncoding>(O2JamRulesetSetting.MetadataEncoding), Is.EqualTo(expected));
                    Assert.That(Enum.GetValues<O2JamRulesetSetting>().Select(value => (int)value), Is.EqualTo(Enumerable.Range(0, original.Length)));
                    Assert.That(Enum.GetNames<O2JamRulesetSetting>(), Is.EqualTo(original.Select(entry => entry.Key)));
                });
                config.SetValue(O2JamRulesetSetting.ScrollSpeed, 23d);
                Assert.That(config.Save(), Is.True);
            }

            realm.Run(database =>
            {
                var settings = database.All<RealmRulesetSetting>().Where(setting => setting.RulesetName == info.ShortName && setting.Variant == 0).ToArray();
                Assert.That(settings.Select(setting => setting.Key), Is.EquivalentTo(original.Select(entry => entry.Key)));
                foreach (var entry in original.Where(entry => entry.Key != "ScrollSpeed"))
                    Assert.That(settings.Single(setting => setting.Key == entry.Key).Value, Is.EqualTo(entry.Value));
                Assert.That(double.Parse(settings.Single(setting => setting.Key == "ScrollSpeed").Value, CultureInfo.InvariantCulture), Is.EqualTo(23));
                Assert.That(database.All<RealmRulesetSetting>().Count(), Is.EqualTo(9));
            });
            realm.Dispose();

            using var restarted = new RealmAccess(storage, "client.realm");
            using var restored = new O2JamRulesetConfigManager(new SettingsStore(restarted), info);
            using var variant = new O2JamRulesetConfigManager(new SettingsStore(restarted), info, 1);
            Assert.Multiple(() =>
            {
                Assert.That(restored.Get<OjnMetadataEncoding>(O2JamRulesetSetting.MetadataEncoding), Is.EqualTo(expected));
                Assert.That(restored.Get<double>(O2JamRulesetSetting.ScrollSpeed), Is.EqualTo(23));
                Assert.That(restored.Get<string>(O2JamRulesetSetting.LastImportPath), Is.EqualTo(original[0].Value));
                Assert.That(variant.Get<OjnMetadataEncoding>(O2JamRulesetSetting.MetadataEncoding), Is.EqualTo(OjnMetadataEncoding.Utf8));
            });
            restarted.Run(database =>
                Assert.That(database.All<RealmRulesetSetting>().Single(setting => setting.RulesetName == "other").Value, Is.EqualTo("Gbk")));
        });
}
