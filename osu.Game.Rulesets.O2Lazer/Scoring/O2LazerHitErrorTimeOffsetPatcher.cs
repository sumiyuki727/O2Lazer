using System;
using System.Linq;
using HarmonyLib;
using osu.Framework.Logging;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Scoring.Judgements;
using osu.Game.Rulesets.Judgements;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

/// <summary>
/// Keeps the skin's own UR bars untouched while presenting O2Jam long-note head timing to them.
/// </summary>
/// <remarks>
/// Generic hit error meters read <see cref="JudgementResult.TimeOffset"/>, which for an O2Jam
/// long note is stamped at the tail. Patching the getter lets every existing skin meter plot the
/// head judgement without replacing its drawable or layout.
/// </remarks>
internal static class O2LazerHitErrorTimeOffsetPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.HitErrorTimeOffset";

    private static readonly object install_lock = new();

    internal static bool IsInstalled { get; private set; }

    internal static void InstallOnce()
    {
        lock (install_lock)
        {
            if (IsInstalled)
                return;

            var target = AccessTools.PropertyGetter(typeof(JudgementResult), nameof(JudgementResult.TimeOffset));
            var prefixMethod = AccessTools.Method(typeof(O2LazerHitErrorTimeOffsetPatcher), nameof(timeOffsetPrefix));
            var missingMembers = new[]
            {
                (name: "JudgementResult.TimeOffset getter", member: target),
                (name: "O2LazerHitErrorTimeOffsetPatcher.timeOffsetPrefix", member: prefixMethod),
            }.Where(member => member.member == null).Select(member => member.name).ToArray();

            if (missingMembers.Length > 0)
            {
                O2LazerLogger.Log(
                    "O2Jam HitErrorTimeOffsetPatcher: Cannot install Harmony patch. Missing: "
                    + $"{string.Join(", ", missingMembers)}. Long-note UR bars may display at the tail.",
                    level: LogLevel.Error);
                return;
            }

            try
            {
                new Harmony(harmony_id).Patch(target, prefix: new HarmonyMethod(prefixMethod));
                IsInstalled = true;
            }
            catch (Exception exception)
            {
                O2LazerLogger.Error(exception,
                    "O2Jam HitErrorTimeOffsetPatcher: Failed to install Harmony patch. Long-note UR bars may display at the tail.");
            }
        }
    }

    // ReSharper disable once InconsistentNaming
    private static bool timeOffsetPrefix(JudgementResult __instance, ref double __result)
    {
        var headOffset = GetO2JamLongNoteHeadOffset(__instance);
        if (headOffset == null)
            return true;

        __result = headOffset.Value;
        return false;
    }

    internal static double? GetO2JamLongNoteHeadOffset(JudgementResult result)
    {
        if (result is not O2LazerLongNoteJudgementResult longNoteResult)
            return null;

        var beatmap = (longNoteResult.HitObject as O2LazerLongNote)?.Beatmap;
        if (beatmap?.LayoutVariant != O2LazerLayoutVariant.O2Jam7K)
            return null;

        var head = longNoteResult.EndpointResults.FirstOrDefault(endpoint => endpoint.Kind == O2LazerLongNoteEndpointKind.Head);
        if (head.Source == null)
            return null;

        return head.TimeOffset;
    }
}
