using System;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
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
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Screens;
using osuTK;

namespace osu.Game.Rulesets.O2Lazer.Configuration;

public partial class O2JamSettingsSubsection : RulesetSettingsSubsection
{

    [Cached]
    private readonly OverlayColourProvider colourProvider = new(OverlayColourScheme.Purple);

    private O2JamLibrarySettingsSession library = null!;
    private O2JamRulesetConfigManager config = null!;
    private Bindable<string> importPath = null!;
    private Bindable<bool> syncSourceFolderCollections = null!;
    private RoundedButton refreshButton = null!;
    private DangerousRoundedButton deleteButton = null!;
    private FillFlowContainer libraryActions = null!;
    private bool disposed;

    [Resolved(CanBeNull = true)]
    private IPerformFromScreenRunner? performer { get; set; }

    [Resolved(CanBeNull = true)]
    private IDialogOverlay? dialogOverlay { get; set; }

    public O2JamSettingsSubsection(O2LazerRuleset ruleset)
        : base(ruleset)
    {
    }

    private void initialiseLibrarySettings(O2JamRulesetConfigManager rulesetConfig, O2JamLibrarySettingsSession session)
    {
        config = rulesetConfig;
        library = session;
        importPath = config.GetBindable<string>(O2JamRulesetSetting.LastImportPath);
        syncSourceFolderCollections = config.GetBindable<bool>(O2JamRulesetSetting.SyncSourceFolderCollections);
        refreshButton = new RoundedButton
        {
            Text = O2LazerStrings.RefreshBeatmaps,
            TooltipText = O2LazerStrings.RefreshBeatmapsTooltip,
            RelativeSizeAxes = Axes.X,
            Height = 36,
            Action = () => _ = library.RefreshAsync(),
            Padding = SettingsPanel.CONTENT_PADDING,
        };

        Drawable[] children =
        [
            new ClickableImportPathField
            {
                Current = importPath,
                Clicked = () => performer?.PerformFromScreen(menu => menu.Push(new O2JamDirectorySelectScreen(config))),
            },
            libraryActions = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, SettingsSection.ITEM_SPACING_V2),
                Children =
                [
                    refreshButton,
                    deleteButton = new DangerousRoundedButton
                    {
                        Text = O2LazerStrings.DeleteAllImportedFiles,
                        RelativeSizeAxes = Axes.X,
                        Height = 36,
                        Action = confirmDeleteAll,
                        Padding = SettingsPanel.CONTENT_PADDING,
                    },
                ],
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
        ];

        Children = children;
        library.Application.ActivityChanged += onLibraryActivityChanged;
        importPath.ValueChanged += onImportPathChanged;
        updateButtons();
    }

    private void onLibraryActivityChanged()
    {
        if (!disposed)
            Schedule(updateButtons);
    }

    private void onImportPathChanged(ValueChangedEvent<string> path) => onLibraryActivityChanged();

    private void updateButtons()
    {
        if (disposed)
            return;
        // Hide the group so settings search cannot reveal path-dependent actions by changing
        // each filterable button's alpha. Native flow layout collapses an absent group.
        libraryActions.Alpha = string.IsNullOrWhiteSpace(importPath.Value) ? 0 : 1;
        refreshButton.Enabled.Value = library.Application.CanRefresh;
        deleteButton.Enabled.Value = !library.Application.IsBusy;
    }

    private void confirmDeleteAll()
    {
        var dialog = new MassDeleteConfirmationDialog(
            () => _ = library.DeleteAllAsync(),
            O2LazerStrings.DeleteAllConfirmation);

        if (dialogOverlay != null)
            dialogOverlay.Push(dialog);
        else
            _ = library.DeleteAllAsync();
    }

    protected override void Dispose(bool isDisposing)
    {
        disposed = true;
        if (library != null)
            library.Application.ActivityChanged -= onLibraryActivityChanged;
        if (importPath != null)
            importPath.ValueChanged -= onImportPathChanged;
        importPath?.UnbindAll();
        syncSourceFolderCollections?.UnbindAll();
        base.Dispose(isDisposing);
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
            Current.BindValueChanged(onPathChanged, true);
        }

        private void onPathChanged(ValueChangedEvent<string> path) =>
            pathText.Text = string.IsNullOrWhiteSpace(path.NewValue) ? O2LazerStrings.ImportPathPlaceholder : path.NewValue;

        protected override void Dispose(bool isDisposing)
        {
            if (Current != null)
                Current.ValueChanged -= onPathChanged;
            base.Dispose(isDisposing);
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
