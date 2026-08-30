using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using Realms;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
[Category("LocalDiagnostics")]
public class O2JamHoldReplayDiagnosticTest
{
    [Test]
    [NUnit.Framework.Explicit("Compares a received replay with the live library using read-only Realm access.")]
    public void CompareStoredReplayAndJudgementAssets()
    {
        var replayPath = Environment.GetEnvironmentVariable("O2JAM_REPLAY_DIAGNOSTIC_PATH");
        var realmPath = Environment.GetEnvironmentVariable("O2JAM_DIAGNOSTIC_REALM");
        if (string.IsNullOrEmpty(replayPath) || string.IsNullOrEmpty(realmPath))
            Assert.Ignore("Set O2JAM_REPLAY_DIAGNOSTIC_PATH and O2JAM_DIAGNOSTIC_REALM.");

        var bytes = File.ReadAllBytes(replayPath!);
        Assert.That(O2JamReplayArchive.TryReadMetadata(bytes, out var metadata), Is.True);
        TestContext.Progress.WriteLine($"Received SHA256={Convert.ToHexString(SHA256.HashData(bytes))}");
        using var realm = Realm.GetInstance(new RealmConfiguration(realmPath!) { IsReadOnly = true, IsDynamic = true });
        var matched = false;
        foreach (dynamic stored in realm.DynamicApi.All("Score").Filter("BeatmapHash == $0 AND Ruleset.ShortName == $1", metadata.BeatmapHash, "o2lazer"))
        {
            foreach (dynamic file in stored.Files)
            {
                string filename = file.Filename;
                if (!filename.EndsWith(".osr", StringComparison.OrdinalIgnoreCase))
                    continue;
                string hash = file.File.Hash;
                var path = Path.Combine(Path.GetDirectoryName(realmPath!)!, "files", hash[..1], hash[..2], hash);
                var storedBytes = File.ReadAllBytes(path);
                var equal = bytes.SequenceEqual(storedBytes);
                matched |= equal;
                TestContext.Progress.WriteLine($"Stored {stored.Date}: identical={equal}, filename={filename}, path={path}, statistics={stored.Statistics}");
            }
        }
        Assert.That(matched, Is.True, "The inspected replay must exist byte-for-byte in the user's library.");

        if (Guid.TryParse(Environment.GetEnvironmentVariable("O2JAM_DIAGNOSTIC_SKIN"), out var skinId))
        {
            foreach (dynamic skin in realm.DynamicApi.All("Skin").Filter("ID == $0", skinId))
            {
                foreach (dynamic file in skin.Files)
                {
                    string filename = file.Filename;
                    string hash = file.File.Hash;
                    var path = Path.Combine(Path.GetDirectoryName(realmPath!)!, "files", hash[..1], hash[..2], hash);
                    if (filename.Equals("skin.ini", StringComparison.OrdinalIgnoreCase))
                        foreach (var line in File.ReadAllLines(path).Where(line => line.StartsWith("Hit", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Keys", StringComparison.OrdinalIgnoreCase)))
                            TestContext.Progress.WriteLine($"Skin config: {line}");
                    else if (filename.Contains("hit300g", StringComparison.OrdinalIgnoreCase) || filename.Contains("hit0", StringComparison.OrdinalIgnoreCase))
                        TestContext.Progress.WriteLine($"Judgement asset {filename}: {path}");
                }
            }
        }
    }

    [Test]
    [NUnit.Framework.Explicit("Reads the reported replay/chart and visual settings without changing the library or playing audio.")]
    public void InspectReportedReplayAndChart()
    {
        var replayPath = Environment.GetEnvironmentVariable("O2JAM_REPLAY_DIAGNOSTIC_PATH");
        var realmPath = Environment.GetEnvironmentVariable("O2JAM_DIAGNOSTIC_REALM");
        if (string.IsNullOrEmpty(replayPath) || string.IsNullOrEmpty(realmPath))
            Assert.Ignore("Set O2JAM_REPLAY_DIAGNOSTIC_PATH and O2JAM_DIAGNOSTIC_REALM.");

        var bytes = File.ReadAllBytes(replayPath!);
        Assert.That(O2JamReplayArchive.TryReadMetadata(bytes, out var metadata), Is.True);
        Assert.That(O2JamReplayArchive.TryReadScore(new ScoreInfo(), bytes, out var score), Is.True);
        TestContext.Progress.WriteLine($"Replay frames={score.Replay.Frames.Count}; hash={metadata.BeatmapHash}; mods={metadata.ModsJson}; complete={score.Replay.HasReceivedAllFrames}");

        using var realm = Realm.GetInstance(new RealmConfiguration(realmPath!) { IsReadOnly = true, IsDynamic = true });
        var skinId = Environment.GetEnvironmentVariable("O2JAM_DIAGNOSTIC_SKIN");
        if (Guid.TryParse(skinId, out var skinGuid))
        {
            foreach (dynamic skin in realm.DynamicApi.All("Skin").Filter("ID == $0", skinGuid))
            {
                TestContext.Progress.WriteLine($"Skin={skin.Name}, type={skin.InstantiationInfo}");
                foreach (dynamic file in skin.Files)
                {
                    string name = file.Filename;
                    string hash = file.File.Hash;
                    var path = Path.Combine(Path.GetDirectoryName(realmPath!)!, "files", hash[..1], hash[..2], hash);
                    if (name.Equals("skin.ini", StringComparison.OrdinalIgnoreCase))
                        TestContext.Progress.WriteLine(File.ReadAllText(path));
                    else if (name.Contains("note", StringComparison.OrdinalIgnoreCase))
                        TestContext.Progress.WriteLine($"Skin asset {name}: {path}");
                }
            }
        }
        foreach (dynamic setting in realm.DynamicApi.All("RulesetSetting").Filter("RulesetName == $0", "o2lazer"))
        {
            string key = setting.Key;
            if (key is "O2JamStyleDroppedHold" or "ScrollSpeed" or "ScrollDirection" or "ConstantScrollSpeed" or "PercyLongNoteBodyRepeat")
                TestContext.Progress.WriteLine($"Setting variant={setting.Variant}: {key}={setting.Value}");
        }

        foreach (dynamic beatmap in realm.DynamicApi.All("Beatmap").Filter("Hash == $0 AND Ruleset.ShortName == $1", metadata.BeatmapHash, "o2lazer"))
        {
            var source = Path.Combine((string)beatmap.Metadata.Source, (string)beatmap.Metadata.AudioFile);
            TestContext.Progress.WriteLine($"Chart={source}; difficulty={beatmap.DifficultyName}");
            using var stream = File.OpenRead(source);
            var document = new OjnReader().ReadChart(stream, O2JamDifficulty.HX);
            var chart = new OjnBeatmapFactory().Create(document, O2JamDifficulty.HX);
            var holds = chart.HitObjects.OfType<O2JamHoldNote>().ToArray();
            TestContext.Progress.WriteLine($"Title={chart.Metadata.Title}; holds={holds.Length}; shortest={holds.Min(hold => hold.Duration):F3}; longest={holds.Max(hold => hold.Duration):F3}; BPM={document.Metadata.InitialBpm}");

            var frames = score.Replay.Frames.Cast<O2JamReplayFrame>().ToArray();
            var suspectCount = 0;
            foreach (var hold in holds)
            {
                hold.ApplyDefaults(chart.ControlPointInfo, chart.Difficulty);
                var action = (osu.Game.Rulesets.Mania.ManiaAction)hold.Column;
                var head = (O2JamHoldHead)hold.Head;
                var candidate = frames.Where((frame, index) => index > 0 && frame.Actions.Contains(action) && !frames[index - 1].Actions.Contains(action)
                                                               && Math.Abs(frame.Time - hold.StartTime) <= head.MaximumJudgementOffset)
                                       .MinBy(frame => Math.Abs(frame.Time - hold.StartTime));
                if (candidate == null)
                {
                    TestContext.Progress.WriteLine($"No nearby head press: column={hold.Column + 1}, start={hold.StartTime:F3}, end={hold.EndTime:F3}, duration={hold.Duration:F3}, head late window={head.MaximumJudgementOffset:F3}");
                    suspectCount++;
                    continue;
                }

                var release = frames.FirstOrDefault(frame => frame.Time > candidate.Time && !frame.Actions.Contains(action));
                if (release != null && release.Time < hold.EndTime - 100)
                {
                    TestContext.Progress.WriteLine($"Early release candidate: column={hold.Column + 1}, start={hold.StartTime:F3}, end={hold.EndTime:F3}, press={candidate.Time:F3}, release={release.Time:F3}, remaining={hold.EndTime - release.Time:F3}");
                    suspectCount++;
                }
            }
            TestContext.Progress.WriteLine($"Candidate count={suspectCount}; nearest-press scan is not a complete judgement replay.");
        }
    }
}
