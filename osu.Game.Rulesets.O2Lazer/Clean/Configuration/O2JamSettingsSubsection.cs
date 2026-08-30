using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.Database;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Overlays.Settings;
using osu.Game.Overlays.Settings.Sections.Maintenance;
using osu.Game.Rulesets.Mania.Configuration;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Screens;
using osuTK;

namespace osu.Game.Rulesets.O2Lazer.Configuration;

public partial class O2JamSettingsSubsection : RulesetSettingsSubsection
{
    protected override LocalisableString Header => O2LazerStrings.RulesetName;

    [Cached]
    private readonly OverlayColourProvider colourProvider = new(OverlayColourScheme.Purple);

    private O2JamLibraryWriter? libraryWriter;
    private O2JamSourceFolderCollectionService? collectionService;
    private O2JamRulesetConfigManager config = null!;
    private INotificationOverlay? notifications;
    private Bindable<string> importPath = null!;
    private Bindable<bool> syncSourceFolderCollections = null!;
    private RoundedButton refreshButton = null!;
    private bool refreshRunning;
    private readonly object collectionUpdateLock = new();
    private Task collectionUpdateTask = Task.CompletedTask;

    [Resolved(CanBeNull = true)]
    private IPerformFromScreenRunner? performer { get; set; }

    [Resolved(CanBeNull = true)]
    private IDialogOverlay? dialogOverlay { get; set; }

    public O2JamSettingsSubsection(O2LazerRuleset ruleset)
        : base(ruleset)
    {
    }

    [BackgroundDependencyLoader]
    private void load(GameHost host, RealmAccess realm, INotificationOverlay? notifications = null)
    {
        config = (O2JamRulesetConfigManager)Config;
        this.notifications = notifications;
        libraryWriter = new O2JamLibraryWriter(realm, host.Storage);
        collectionService = new O2JamSourceFolderCollectionService(realm);
        importPath = config.GetBindable<string>(O2JamRulesetSetting.LastImportPath);
        syncSourceFolderCollections = config.GetBindable<bool>(O2JamRulesetSetting.SyncSourceFolderCollections);
        refreshButton = new RoundedButton
        {
            Text = O2LazerStrings.RefreshBeatmaps,
            TooltipText = O2LazerStrings.RefreshBeatmapsTooltip,
            RelativeSizeAxes = Axes.X,
            Height = 36,
            Action = refreshBeatmaps,
            Padding = SettingsPanel.CONTENT_PADDING,
        };

        var children = new List<Drawable>
        {
            new ClickableImportPathField
            {
                Current = importPath,
                Clicked = () => performer?.PerformFromScreen(menu => menu.Push(new O2JamDirectorySelectScreen(config))),
            },
            refreshButton,
            new DangerousRoundedButton
            {
                Text = O2LazerStrings.DeleteAllImportedFiles,
                RelativeSizeAxes = Axes.X,
                Height = 36,
                Action = confirmDeleteAll,
                Padding = SettingsPanel.CONTENT_PADDING,
            },
            new SettingsItemV2(new FormSliderBar<double>
            {
                Caption = RulesetSettingsStrings.ScrollSpeed,
                Current = config.GetBindable<double>(O2JamRulesetSetting.ScrollSpeed),
                KeyboardStep = 1,
                LabelFormat = speed => O2LazerStrings.ScrollSpeedTooltipWithO2JamGrade(
                    RulesetSettingsStrings.ScrollSpeedTooltip((int)O2JamDrawableRuleset.ComputeScrollTime(speed), speed),
                    O2JamDrawableRuleset.GetO2JamSpeedMultiplier(speed)),
            }),
            new SettingsItemV2(new FormEnumDropdown<ManiaScrollingDirection>
            {
                Caption = RulesetSettingsStrings.ScrollingDirection,
                Current = config.GetBindable<ManiaScrollingDirection>(O2JamRulesetSetting.ScrollDirection),
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = O2LazerStrings.FixedScrollSpeed,
                Current = config.GetBindable<bool>(O2JamRulesetSetting.ConstantScrollSpeed),
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = O2LazerStrings.SyncSourceFolderCollections,
                Current = syncSourceFolderCollections,
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = O2LazerStrings.O2JamLongNoteVisual,
                HintText = O2LazerStrings.O2JamLongNoteVisualDescription,
                Current = config.GetBindable<bool>(O2JamRulesetSetting.O2JamStyleDroppedHold),
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = O2LazerStrings.PercyLongNoteBodyRepeat,
                HintText = O2LazerStrings.PercyLongNoteBodyRepeatDescription,
                Current = config.GetBindable<bool>(O2JamRulesetSetting.PercyLongNoteBodyRepeat),
            }),
        };

        Children = children;
        importPath.BindValueChanged(path =>
        {
            updateRefreshButtonState(path);
            if (syncSourceFolderCollections.Value)
                _ = queueCollectionUpdate(true);
        }, true);
        syncSourceFolderCollections.BindValueChanged(enabled => _ = queueCollectionUpdate(enabled.NewValue), true);
    }

    private async void refreshBeatmaps()
    {
        var activeWriter = libraryWriter;
        var path = importPath.Value;
        if (refreshRunning || activeWriter == null || string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        refreshRunning = true;
        refreshButton.Enabled.Value = false;
        var notification = new ProgressNotification
        {
            Text = O2LazerStrings.RefreshingProgress(0, 0),
            Progress = 0,
            State = ProgressNotificationState.Active,
        };
        notifications?.Post(notification);
        var cancellationToken = notification.CancellationToken;

        try
        {
            var result = await Task.Run(() =>
            {
                var paths = enumerateCharts(path, cancellationToken);
                var sources = activeWriter.GetImportedSources();
                var importer = new O2JamImportService(new O2JamImportPlanner(), activeWriter);
                return importer.Refresh(
                    paths,
                    sources,
                    (processed, total) =>
                    {
                        notification.Text = O2LazerStrings.RefreshingProgress(processed, total);
                        notification.Progress = total == 0 ? 1 : (float)processed / total;
                    },
                    (exception, sourcePath) => Logger.Error(exception, $"O2Jam refresh failed for '{sourcePath}'."),
                    cancellationToken);
            }, cancellationToken);

            if (syncSourceFolderCollections.Value)
                await queueCollectionUpdate(true);

            if (result.RulesetUnavailable)
            {
                notification.CompletionText = O2LazerStrings.RulesetUnavailable;
                notification.State = ProgressNotificationState.Cancelled;
            }
            else
            {
                notification.CompletionText = O2LazerStrings.RefreshComplete;
                notification.State = ProgressNotificationState.Completed;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            notification.State = ProgressNotificationState.Cancelled;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "O2Jam refresh failed before the source directory could be processed.");
            notification.CompletionText = O2LazerStrings.ImportFailed;
            notification.State = ProgressNotificationState.Cancelled;
        }
        finally
        {
            refreshRunning = false;
            Schedule(() => updateRefreshButtonState(new ValueChangedEvent<string>(path, importPath.Value)));
        }
    }

    private static string[] enumerateCharts(string path, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
            BufferSize = 64 * 1024,
        };

        var charts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(path, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(Path.GetExtension(file), ".ojn", StringComparison.OrdinalIgnoreCase))
                charts.Add(file);
        }

        return charts.ToArray();
    }

    private void updateRefreshButtonState(ValueChangedEvent<string> path) =>
        refreshButton.Enabled.Value = !refreshRunning
                                      && !string.IsNullOrWhiteSpace(path.NewValue)
                                      && Directory.Exists(path.NewValue);

    private void confirmDeleteAll()
    {
        var activeWriter = libraryWriter;
        if (activeWriter == null)
            return;

        var dialog = new MassDeleteConfirmationDialog(
            () => _ = deleteAllImportedBeatmaps(),
            O2LazerStrings.DeleteAllConfirmation);

        if (dialogOverlay != null)
            dialogOverlay.Push(dialog);
        else
            _ = deleteAllImportedBeatmaps();
    }

    private async Task deleteAllImportedBeatmaps()
    {
        var activeWriter = libraryWriter;
        if (activeWriter == null)
            return;

        await Task.Run(activeWriter.DeleteAll);
        if (syncSourceFolderCollections.Value)
            await queueCollectionUpdate(true);
    }

    private Task queueCollectionUpdate(bool enabled)
    {
        var service = collectionService;
        if (service == null)
            return Task.CompletedTask;

        var libraryRoot = importPath.Value;
        lock (collectionUpdateLock)
        {
            // Settings changes and refresh completion are serialised so rapid toggles always leave
            // Realm in the state represented by the last switch value without blocking the UI thread.
            collectionUpdateTask = collectionUpdateTask.ContinueWith(_ =>
            {
                try
                {
                    if (enabled)
                        service.Synchronise(libraryRoot);
                    else
                        service.DeleteFeatureCollections();
                }
                catch (Exception exception)
                {
                    Logger.Error(exception, "O2Jam source-folder collection synchronisation failed.");
                }
            }, TaskScheduler.Default);

            return collectionUpdateTask;
        }
    }

    private sealed partial class ClickableImportPathField : CompositeDrawable
    {
        public Bindable<string> Current { get; init; } = null!;
        public Action Clicked { get; init; } = () => { };

        private FormControlBackground background = null!;
        private TruncatingSpriteText pathText = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Padding = SettingsPanel.CONTENT_PADDING;

            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Children =
                [
                    background = new FormControlBackground(),
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Padding = new MarginPadding(9),
                        Spacing = new Vector2(0, 4),
                        Direction = FillDirection.Vertical,
                        Children =
                        [
                            new FormFieldCaption
                            {
                                Caption = O2LazerStrings.ImportPath,
                                TooltipText = O2LazerStrings.ImportPathHint,
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 16,
                                Child = pathText = new TruncatingSpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    RelativeSizeAxes = Axes.X,
                                    Colour = colourProvider.Content1,
                                },
                            },
                        ],
                    },
                ],
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Current.BindValueChanged(path => pathText.Text = string.IsNullOrWhiteSpace(path.NewValue)
                ? O2LazerStrings.ImportPathPlaceholder
                : path.NewValue, true);
        }

        protected override bool OnHover(HoverEvent e)
        {
            background.VisualStyle = VisualStyle.Hovered;
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            background.VisualStyle = VisualStyle.Normal;
            base.OnHoverLost(e);
        }

        protected override bool OnClick(ClickEvent e)
        {
            Clicked();
            return true;
        }
    }

    private sealed partial class O2JamDirectorySelectScreen(O2JamRulesetConfigManager config) : DirectorySelectScreen
    {
        public override LocalisableString HeaderText => O2LazerStrings.ImportPath;

        protected override DirectoryInfo InitialPath
        {
            get
            {
                var path = config.Get<string>(O2JamRulesetSetting.LastImportPath);
                return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) ? new DirectoryInfo(path) : null!;
            }
        }

        protected override void OnSelection(DirectoryInfo directory)
        {
            config.SetValue(O2JamRulesetSetting.LastImportPath, directory.FullName);
            this.Exit();
        }
    }
}
