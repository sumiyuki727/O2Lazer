using System;
using System.Collections.Generic;

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
    string? SourceHash = null);

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
