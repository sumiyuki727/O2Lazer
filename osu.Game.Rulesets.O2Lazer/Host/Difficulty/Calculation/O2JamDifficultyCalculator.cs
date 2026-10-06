using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using O2Jam.Formats.Ojn;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mania.Difficulty;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Host.Library;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Mods;

namespace osu.Game.Rulesets.O2Lazer.Difficulty;

public sealed class O2JamDifficultyCalculator : DifficultyCalculator
{
    // Native reprocessing must persist mania stars regardless of the selected display mode.
    public override int Version => O2JamManiaStarRating.CacheVersion;

    public O2JamDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
        : base(ruleset, new MetadataWorkingBeatmap(beatmap))
    {
    }

    protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills)
    {
        var maxCombo = beatmap.BeatmapInfo.TotalObjectCount < 0 || beatmap.BeatmapInfo.EndTimeObjectCount < 0
            ? 0
            : Math.Max(0, beatmap.BeatmapInfo.TotalObjectCount + beatmap.BeatmapInfo.EndTimeObjectCount - 1);

        if (O2JamGameplayProfile.UsesManiaScore(mods))
        {
            return new ManiaDifficultyAttributes
            {
                StarRating = beatmap.BeatmapInfo.StarRating,
                Mods = mods,
                MaxCombo = maxCombo,
            };
        }

        return new DifficultyAttributes(mods, beatmap.BeatmapInfo.StarRating)
        {
            MaxCombo = maxCombo,
        };
    }

    protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, Mod[] mods) => [];

    protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods) => [];

    protected override Mod[] DifficultyAdjustmentMods => [];

    private sealed class MetadataWorkingBeatmap(IWorkingBeatmap source) : FlatWorkingBeatmap(new Beatmap())
    {
        private double? baselineStars;
        private int baselineMaxCombo;
        private O2JamNativeDifficultyResult? baselineResult;

        public override IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            O2JamNativeDifficultyHandoff.Publish(null);
            var metadata = Beatmap;

            if (O2JamGameplayProfile.RequiresStarCalculation(mods))
            {
                var playable = source.GetPlayableBeatmap(ruleset, mods, token);
                var attributes = O2JamManiaStarRating.CalculateAttributes(playable, mods, true, token);
                metadata.BeatmapInfo.StarRating = attributes.StarRating;
                metadata.BeatmapInfo.TotalObjectCount = attributes.MaxCombo + 1;
                metadata.BeatmapInfo.EndTimeObjectCount = 0;
                return metadata;
            }

            // Complete baseline caches serve metadata-only mods. Missing attributes are
            // calculated once per calculator without loading gameplay audio when possible.
            if (!baselineStars.HasValue)
            {
                var storedStars = O2JamStarRatingMetadata.ReadMania(source.BeatmapInfo);
                if (storedStars.HasValue && O2JamStarRatingMetadata.CanReuseManiaDifficulty(source.BeatmapInfo)
                    && source.BeatmapInfo.TotalObjectCount >= 0 && source.BeatmapInfo.EndTimeObjectCount >= 0)
                {
                    baselineStars = storedStars.Value;
                    baselineMaxCombo = O2JamStarRatingMetadata.ResolveManiaMaxCombo(source.BeatmapInfo);
                }
                else
                {
                    var committed = readCommittedProjection(token, out var sourceHash, out var projection);
                    var attributes = committed != null
                        ? O2JamManiaStarRating.CalculateBaselineAttributes(committed, token)
                        : O2JamManiaStarRating.CalculateAttributes(source.GetPlayableBeatmap(ruleset, [], token), [], false, token);
                    baselineStars = attributes.StarRating;
                    baselineMaxCombo = attributes.MaxCombo;
                    if (sourceHash != null && projection != null && source.BeatmapInfo is BeatmapInfo info)
                    {
                        baselineResult = new O2JamNativeDifficultyResult(info.ID, info.BeatmapSet!.ID, info.Hash, info.MD5Hash,
                            sourceHash, projection.Difficulty, attributes.StarRating, attributes.MaxCombo);
                    }
                }
            }

            if (mods.Count == 0)
                O2JamNativeDifficultyHandoff.Publish(baselineResult);
            metadata.BeatmapInfo.StarRating = baselineStars.Value;
            metadata.BeatmapInfo.TotalObjectCount = baselineMaxCombo + 1;
            metadata.BeatmapInfo.EndTimeObjectCount = 0;
            return metadata;
        }

        private ManiaBeatmap? readCommittedProjection(CancellationToken token, out string? sourceHash, out O2JamStoredImportMetadata? projection)
        {
            sourceHash = null;
            projection = null;
            if (source.BeatmapInfo is not BeatmapInfo info || info.Ruleset.ShortName != O2LazerIdentity.ShortName
                || O2JamImportMetadata.Read(info.Metadata.Tags, out projection) != O2JamImportMetadataStatus.Valid
                || projection!.ProjectionVersion != O2JamImportMetadata.ProjectionVersion)
                return null;
            var storagePath = info.BeatmapSet?.GetPathForFile(info.Metadata.AudioFile);
            if (storagePath == null)
                return null;

            // Difficulty needs the committed OJN only. Gameplay decoding also indexes OJM
            // and follows the external path, neither of which belongs in startup calculation.
            using var stream = source.GetStream(storagePath);
            using var data = new MemoryStream();
            stream.CopyTo(data);
            token.ThrowIfCancellationRequested();
            var bytes = data.ToArray();
            sourceHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(info.Hash, O2JamBeatmapIdentity.FromSource(sourceHash, projection.Difficulty), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(info.MD5Hash, O2JamBeatmapIdentity.Md5FromSource(bytes, projection.Difficulty), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The committed OJN does not match its difficulty identity.");
            var document = new OjnReader(OjnMetadataEncoding.Automatic,
                projection.EncodingFallback is { } encoding ? () => encoding : null).Read(bytes);
            var beatmap = new OjnBeatmapFactory().CreateDifficultyProjection(document, projection.Difficulty);
            beatmap.BeatmapInfo = info.Clone();
            return beatmap;
        }
    }
}
