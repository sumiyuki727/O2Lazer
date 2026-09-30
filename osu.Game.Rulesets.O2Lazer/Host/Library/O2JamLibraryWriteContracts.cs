using System;
using System.Collections.Generic;
using O2Jam.Core;
using O2Jam.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Import;

public enum O2JamLibraryWriteResult
{
    Imported,
    Updated,
    AlreadyPresent,
    RulesetUnavailable,
}

public sealed record O2JamImportedSource(
    Guid SetId,
    DateTimeOffset? LastLocalUpdate,
    long? SourceLength,
    bool HasCurrentMetadata,
    bool HasCurrentEncoding,
    string? SourceHash = null,
    OjnMetadataEncoding? EncodingFallback = null,
    IReadOnlyList<O2JamImportDifficultyCache>? ManiaCache = null,
    IReadOnlyList<O2JamStoredDifficultyIdentity>? DifficultyIdentities = null);

public sealed record O2JamStoredDifficultyIdentity(O2JamDifficulty Difficulty, string Md5Hash);

public sealed record O2JamImportDifficultyCache(O2JamDifficulty Difficulty, double StarRating, int MaxCombo, int Version);

public sealed record O2JamLibraryWriteRequest(
    O2JamImportPlan Plan,
    Guid? KnownSourceSetId = null,
    bool SourceIndexWasLoaded = false);

public interface IO2JamLibraryWriter
{
    O2JamLibraryWriteResult Write(O2JamImportPlan plan);
    IReadOnlyList<O2JamLibraryWriteResult> WriteBatch(IReadOnlyList<O2JamLibraryWriteRequest> requests);
    int MarkDeleted(IEnumerable<Guid> setIds);
}
