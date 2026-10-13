using System;
using System.IO;
using System.Threading.Tasks;
using osu.Framework.Extensions;
using osu.Framework.Logging;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.O2Lazer.Replays;

internal static partial class O2JamReplayPersistencePatch
{
    internal sealed record PreparedReplay(byte[]? Bytes, O2JamReplayWriteFailure Failure);

    // Carry either the exact pre-clone hash bytes or a replay-only failure. The native
    // Player import still runs in both cases and remains responsible for saving the score.
    private static void prepareReplayArchive(Player __instance, Score score, out PreparedReplay? __state)
    {
        __state = null;
        if (!isO2JamScore(score.ScoreInfo)
            || playerDrawableRulesetProperty!.GetValue(__instance) is DrawableRuleset { ReplayScore: not null })
            return;

        __state = PrepareForImport(score);
    }

    internal static PreparedReplay PrepareForImport(Score score, O2JamReplayResourceLimits? limits = null)
    {
        // Native ScoreManager resolves detached offline scores by Hash, not by Guid alone.
        // An unavailable replay still needs a stable, independent local score identity.
        if (string.IsNullOrEmpty(score.ScoreInfo.Hash))
        {
            if (score.ScoreInfo.ID == Guid.Empty)
                score.ScoreInfo.ID = Guid.NewGuid();
            score.ScoreInfo.Hash = $"o2lazer-score-{score.ScoreInfo.ID:N}";
        }

        if (!O2JamReplayArchive.TryCreate(score, out var bytes, out var failure, limits))
            return new PreparedReplay(null, failure);

        try
        {
            using var stream = new MemoryStream(bytes!, writable: false);
            score.ScoreInfo.Hash = stream.ComputeSHA2Hash();
            return new PreparedReplay(bytes, default);
        }
        catch (Exception exception)
        {
            return new PreparedReplay(null, new O2JamReplayWriteFailure(false, exception));
        }
    }

    private static void attachReplayArchive(Player __instance, Score score, PreparedReplay? __state, ref Task __result)
    {
        if (__state != null)
            __result = attachAfterImport(__instance, score, __state, __result);
    }

    private static async Task attachAfterImport(Player player, Score score, PreparedReplay prepared, Task originalImport)
    {
        // A database import failure is still a score failure. Do not mask it or announce
        // that a score was retained before the original import has succeeded.
        await originalImport.ConfigureAwait(false);
        try
        {
            var manager = playerScoreManagerProperty!.GetValue(player) as ScoreManager;
            if (manager == null)
                throw new InvalidOperationException("Native score manager is unavailable after score import.");

            await CompleteImport(score, prepared, Task.CompletedTask, manager,
                action => drawableScheduleMethod!.Invoke(player, [action])).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // A changed private host seam must not invalidate a score already saved by Player.
            Logger.Error(exception, "O2Lazer could not access replay attachment services after native score import.");
        }
    }

    internal static async Task CompleteImport(Score score, PreparedReplay prepared, Task originalImport,
                                             ScoreManager manager, Action<Action> schedule)
    {
        await originalImport.ConfigureAwait(false);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            schedule(() =>
            {
                try
                {
                    if (prepared.Bytes != null)
                    {
                        using var stream = new MemoryStream(prepared.Bytes, writable: false);
                        manager.AddFile(score.ScoreInfo, stream, nativeReplayFilename(score.ScoreInfo));
                    }
                    else
                        reportReplayFailure(manager, prepared.Failure);
                }
                catch (Exception exception)
                {
                    reportReplayFailure(manager, new O2JamReplayWriteFailure(false, exception));
                }
                finally
                {
                    completion.TrySetResult();
                }
            });
        }
        catch (Exception exception)
        {
            reportReplayFailure(manager, new O2JamReplayWriteFailure(false, exception));
            completion.TrySetResult();
        }

        await completion.Task.ConfigureAwait(false);
    }

    private static void reportReplayFailure(ScoreManager manager, O2JamReplayWriteFailure failure)
    {
        if (failure.Error != null)
            Logger.Error(failure.Error, "O2Lazer could not save the replay attachment; the native score import already succeeded.");
        try
        {
            manager.PostNotification?.Invoke(new SimpleNotification
            {
                Text = failure.ResourceLimitExceeded ? O2LazerStrings.ReplaySaveLimitExceeded : O2LazerStrings.ReplaySaveFailed,
            });
        }
        catch (Exception exception)
        {
            // Notification consumers are outside the attachment operation. Their failure
            // must not turn an already committed score back into a failed import task.
            Logger.Error(exception, "O2Lazer could not report a replay attachment failure.");
        }
    }
}
