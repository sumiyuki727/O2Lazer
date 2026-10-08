using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Game.Overlays;
using osu.Game.Overlays.Dialog;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Screens.Menu;
using NativeSongSelect = osu.Game.Screens.Select.SongSelect;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.O2Lazer.TestSupport;

/// <summary>Local test instrumentation, excluded from normal compilation.</summary>
internal static class O2LazerTestBridge
{
    private const string owner = "osu.Game.Rulesets.O2Lazer.TestBridge";
    private static readonly object sync = new();
    private static WeakReference<NativeSongSelect>? selection;
    private static WeakReference<OsuGame>? gameReference;
    private static WeakReference<IDialogOverlay>? dialogs;
    private static WeakReference<INotificationOverlay>? notifications;
    private static bool started;
    private static readonly JsonSerializerOptions jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal static void StartOnce()
    {
        lock (sync)
        {
            if (started)
                return;
            started = true;
            try
            {
                // Use a distinct lifecycle target to avoid replacing the editor bridge's entry hooks.
                var target = AccessTools.Method(typeof(NativeSongSelect), "LoadComplete")
                             ?? throw new MissingMethodException("SongSelect.LoadComplete");
                new Harmony(owner).Patch(target, postfix: new HarmonyMethod(typeof(O2LazerTestBridge), nameof(capture)));
                var menuTarget = AccessTools.Method(typeof(MainMenu), "LoadComplete")
                                 ?? throw new MissingMethodException("MainMenu.LoadComplete");
                new Harmony(owner).Patch(menuTarget, postfix: new HarmonyMethod(typeof(O2LazerTestBridge), nameof(captureMenu)));
                _ = Task.Run(listen);
                Logger.Log("O2Lazer test bridge enabled: o2lazer-test-" + Environment.ProcessId, LoggingTarget.Runtime, LogLevel.Important);
            }
            catch (Exception exception)
            {
                O2JamPatchCoordinator.Rollback(owner);
                Logger.Error(exception, "O2Lazer test bridge could not start.");
            }
        }
    }

    private static void captureMenu(MainMenu __instance)
    {
        lock (sync)
        {
            gameReference = new WeakReference<OsuGame>(__instance.Dependencies.Get<OsuGame>());
            captureExitDependencies(__instance.Dependencies);
        }
    }

    private static void capture(NativeSongSelect __instance)
    {
        if (__instance is not SoloSongSelect)
            return;
        lock (sync)
        {
            selection = new WeakReference<NativeSongSelect>(__instance);
            gameReference = new WeakReference<OsuGame>(__instance.Dependencies.Get<OsuGame>());
            captureExitDependencies(__instance.Dependencies);
        }
    }

    private static void captureExitDependencies(IReadOnlyDependencyContainer dependencies)
    {
        var dialogOverlay = dependencies.Get<IDialogOverlay>();
        var notificationOverlay = dependencies.Get<INotificationOverlay>();
        if (dialogOverlay != null)
            dialogs = new WeakReference<IDialogOverlay>(dialogOverlay);
        if (notificationOverlay != null)
            notifications = new WeakReference<INotificationOverlay>(notificationOverlay);
    }

    private static async Task listen()
    {
        while (true)
        {
            try
            {
                // Separate process-scoped pipe names allow other bridges and clients to coexist.
                await using var pipe = new NamedPipeServerStream("o2lazer-test-" + Environment.ProcessId,
                    PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync().ConfigureAwait(false);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                object response;
                try
                {
                    // Bound input before JSON parsing, rather than after allocating an arbitrary line.
                    var buffer = new char[4096];
                    var length = 0;
                    var one = new char[1];
                    while (await reader.ReadAsync(one.AsMemory(), timeout.Token).ConfigureAwait(false) != 0 && one[0] != '\n')
                    {
                        if (length == buffer.Length)
                            throw new InvalidDataException("Request exceeds 4096 characters.");
                        buffer[length++] = one[0];
                    }
                    using var request = JsonDocument.Parse(new string(buffer, 0, length));
                    var result = await dispatch(request.RootElement.Clone(), timeout.Token).ConfigureAwait(false);
                    response = new { success = true, protocol = 1, data = result };
                }
                catch (Exception exception)
                {
                    response = new { success = false, protocol = 1, error = exception.Message };
                }
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, jsonOptions).AsMemory(), timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (Exception exception)
            {
                Logger.Error(exception, "O2Lazer test bridge stopped.");
                return;
            }
        }
    }

    private static async Task<object> dispatch(JsonElement request, CancellationToken cancellation)
    {
        var command = request.GetProperty("command").GetString();
        if (command == "capabilities")
            return new { commands = new[] { "status", "difficulties", "select", "play", "pause", "seek", "rulesets", "set-ruleset", "open-song-select", "exit", "exit-state", "confirm-exit" }, processId = Environment.ProcessId };
        if (command is "exit" or "open-song-select" or "exit-state" or "confirm-exit")
            return await dispatchNavigation(command, cancellation).ConfigureAwait(false);
        NativeSongSelect screen;
        lock (sync)
        {
            if (selection == null || !selection.TryGetTarget(out screen!))
                throw new InvalidOperationException("Song select is not loaded.");
        }
        var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var schedule = AccessTools.Method(typeof(Drawable), "Schedule", [typeof(Action)])
                       ?? throw new MissingMethodException("Drawable.Schedule(Action)");
        schedule.Invoke(screen, [(Action)(() =>
        {
            // A timed-out request must never mutate the client when its update thread resumes.
            if (cancellation.IsCancellationRequested)
                return;
            try
            {
                if (!screen.IsCurrentScreen())
                    throw new InvalidOperationException("Commands require current song select.");
                var working = screen.Beatmap.Value;
                var music = screen.Dependencies.Get<MusicController>();
                if (command == "rulesets")
                {
                    completion.TrySetResult(screen.Dependencies.Get<RulesetStore>().AvailableRulesets
                        .Select(r => new { shortName = r.ShortName, name = r.Name }).ToArray());
                    return;
                }
                if (command == "difficulties")
                {
                    completion.TrySetResult(working.BeatmapSetInfo.Beatmaps.Select(b => new
                    {
                        id = b.ID, difficulty = b.DifficultyName, ruleset = b.Ruleset.ShortName, hidden = b.Hidden,
                    }).ToArray());
                    return;
                }
                if (command is "select" or "play" or "pause" or "seek")
                {
                    // Expected identity prevents controlling audio after an unrelated user selection.
                    var expected = request.GetProperty("expectedBeatmapId").GetGuid();
                    if (expected != working.BeatmapInfo.ID)
                        throw new InvalidOperationException("Selected beatmap changed.");
                }
                switch (command)
                {
                    case "select":
                        var id = request.GetProperty("beatmapId").GetGuid();
                        var target = screen.Dependencies.Get<BeatmapManager>().QueryBeatmap(b => b.ID == id)
                                     ?? throw new InvalidOperationException("Target difficulty is not in the local library.");
                        if (target.Hidden || target.BeatmapSet == null || target.BeatmapSet.DeletePending || target.BeatmapSet.Protected)
                            throw new InvalidOperationException("Target difficulty is unavailable for selection.");
                        if (target.Ruleset.ShortName != working.BeatmapInfo.Ruleset.ShortName)
                            throw new InvalidOperationException("Cross-ruleset selection is not supported.");
                        var applied = false;
                        AccessTools.Method(typeof(NativeSongSelect), "SelectAndRun").Invoke(screen, [target, (Action)(() => applied = true)]);
                        if (!applied || screen.Beatmap.Value.BeatmapInfo.ID != id)
                            throw new InvalidOperationException("Native selection rejected the target.");
                        break;
                    case "play":
                        if (!music.Play(requestedByUser: true))
                            throw new InvalidOperationException("Native music controls are disabled.");
                        break;
                    case "pause":
                        if (!music.AllowTrackControl.Value)
                            throw new InvalidOperationException("Native music controls are disabled.");
                        music.Stop(requestedByUser: true);
                        break;
                    case "seek":
                        var time = request.GetProperty("timeMs").GetDouble();
                        if (!double.IsFinite(time) || time < 0 || time > music.CurrentTrack.Length || !music.AllowTrackControl.Value)
                            throw new InvalidOperationException("Seek is outside the loaded track or controls are disabled.");
                        music.SeekTo(time);
                        break;
                    case "set-ruleset":
                        if (screen.Ruleset.Value.ShortName != request.GetProperty("expectedRuleset").GetString())
                            throw new InvalidOperationException("Selected ruleset changed.");
                        var shortName = request.GetProperty("ruleset").GetString()
                                        ?? throw new InvalidDataException("Ruleset short name is required.");
                        var ruleset = screen.Dependencies.Get<RulesetStore>().GetRuleset(shortName)
                                      ?? throw new InvalidOperationException("Ruleset is not installed.");
                        // The same global bindable as the native mode selector runs conversion and filtering.
                        screen.Ruleset.Value = ruleset;
                        break;
                    case "status": break;
                    default: throw new InvalidOperationException("Unknown command.");
                }
                completion.TrySetResult(new
                {
                    beatmapId = screen.Beatmap.Value.BeatmapInfo.ID,
                    ruleset = screen.Ruleset.Value.ShortName,
                    timeMs = music.CurrentTrack.CurrentTime, lengthMs = music.CurrentTrack.Length,
                    running = music.IsPlaying, trackLoaded = music.TrackLoaded,
                    // Native audio operations are queued; acknowledgement is not audio completion.
                    queued = command is "play" or "pause" or "seek" or "set-ruleset",
                });
            }
            catch (Exception exception) { completion.TrySetException(exception); }
        })]);
        return await completion.Task.WaitAsync(cancellation).ConfigureAwait(false);
    }

    private static async Task<object> dispatchNavigation(string command, CancellationToken cancellation)
    {
        OsuGame game;
        lock (sync)
        {
            if (gameReference == null || !gameReference.TryGetTarget(out game!))
                throw new InvalidOperationException("Main menu or song select must be loaded.");
        }
        var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Suspended screen schedulers do not run; lifecycle navigation belongs on the game scheduler.
        AccessTools.Method(typeof(Drawable), "Schedule", [typeof(Action)]).Invoke(game, [(Action)(() =>
        {
            if (cancellation.IsCancellationRequested)
                return;
            try
            {
                // Exit-state polling must remain available while native shutdown unwinds the screen stack.
                if (command != "exit-state" && game.ScreenStack.CurrentScreen is not (MainMenu or SoloSongSelect))
                    throw new InvalidOperationException("Navigation requires main menu or solo song select; leave gameplay/editor first.");
                if (command is "exit-state" or "confirm-exit")
                {
                    IDialogOverlay dialogOverlay;
                    INotificationOverlay notificationOverlay;
                    lock (sync)
                    {
                        if (dialogs == null || !dialogs.TryGetTarget(out dialogOverlay!)
                            || notifications == null || !notifications.TryGetTarget(out notificationOverlay!))
                            throw new InvalidOperationException("Native exit overlays are not ready.");
                    }
                    var dialog = dialogOverlay.CurrentDialog;
                    var ongoing = notificationOverlay.HasOngoingOperations;
                    var canConfirm = dialog is ConfirmExitDialog && !ongoing
                                     && dialog.Buttons.OfType<PopupDialogOkButton>().Any();
                    if (command == "confirm-exit")
                    {
                        if (!canConfirm)
                            throw new InvalidOperationException("No ready ordinary exit confirmation, or background operations are running.");
                        // Click only the native ordinary OK button; never accept a dangerous task-abort dialog.
                        ((ConfirmExitDialog)dialog!).PerformOkAction();
                    }
                    completion.TrySetResult(new { confirmationVisible = dialog is ConfirmExitDialog, ongoingOperations = ongoing, canConfirm, queued = command == "confirm-exit" });
                    return;
                }
                if (command == "exit")
                    game.AttemptExit();
                else if (game.ScreenStack.CurrentScreen is MainMenu currentMenu)
                    AccessTools.Method(typeof(MainMenu), "loadSongSelect").Invoke(currentMenu, null);
                completion.TrySetResult(new { queued = true, command });
            }
            catch (Exception exception) { completion.TrySetException(exception); }
        })]);
        return await completion.Task.WaitAsync(cancellation).ConfigureAwait(false);
    }
}
