using System.IO;
using Newtonsoft.Json;

namespace osu.Game.Rulesets.O2Lazer.Replays;

internal readonly record struct O2JamReplayResourceLimits(
    long EncodedBytes, long DecodedBytes, int CollectionEntries, long Tokens, int TextCharacters)
{
    // One archive support policy applies to reading and writing, not to live recording or v5 itself.
    internal static O2JamReplayResourceLimits Default => new(64 * 1024 * 1024, 128 * 1024 * 1024, 1_000_000, 16_000_000, 1024 * 1024);
}

internal sealed class O2JamReplayResourceBudget
{
    internal bool Exceeded { get; private set; }

    internal void Reject()
    {
        Exceeded = true;
        throw new InvalidDataException("O2Lazer replay exceeds the archive resource budget.");
    }
}

internal sealed class O2JamReplayJsonBudget(O2JamReplayResourceLimits limits, O2JamReplayResourceBudget budget)
{
    // Match the native reader's default depth; the v5 payload itself only needs four levels.
    private readonly int[] entries = new int[65];
    private readonly bool[] arrays = new bool[65];
    private long tokens;

    internal void Check(JsonToken token, int depth, string? text = null)
    {
        if (++tokens > limits.Tokens || text?.Length > limits.TextCharacters)
            budget.Reject();

        if (token == JsonToken.PropertyName)
            countEntry(depth - 1);
        else if (token != JsonToken.Comment)
        {
            if (depth > 0 && arrays[depth - 1] && token is not (JsonToken.EndArray or JsonToken.EndObject))
                countEntry(depth - 1);

            if (token is JsonToken.StartArray or JsonToken.StartObject)
            {
                if (depth >= 64)
                    budget.Reject();
                entries[depth] = 0;
                arrays[depth] = token == JsonToken.StartArray;
            }
        }
    }

    private void countEntry(int depth)
    {
        if (++entries[depth] > limits.CollectionEntries)
            budget.Reject();
    }
}
