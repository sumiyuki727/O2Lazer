using System;
using System.Collections.Generic;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Overlays.Settings;
using osu.Game.Overlays.Settings.Sections.Maintenance;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.IO.Import;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Settings.Components;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Rulesets.O2Lazer.UI.Gameplay;
using osu.Game.Screens;
using osuTK;

namespace osu.Game.Rulesets.O2Lazer.Settings;

public partial class O2LazerSettingsSubsection(O2LazerRuleset ruleset) : RulesetSettingsSubsection(ruleset)
{
    // Hidden until the visual-offset workflow is ready for general users; the config values
    // and gameplay application code below stay intact so re-enabling is a one-line change.
    private static readonly bool enable_visual_offset_settings = false;

    protected override LocalisableString Header => O2LazerStrings.RulesetName;

    [Cached]
    private OverlayColourProvider colourProvider = new(OverlayColourScheme.Purple);

    private O2LazerFileImporter? o2lazerImporter;
    private RoundedButton refreshButton = null!;
    private Bindable<bool> syncSourceFolderCollections = null!;
    private Bindable<string> importPath = null!;

    [Resolved(CanBeNull = true)]
    private RealmAccess? realm { get; set; }

    [Resolved(CanBeNull = true)]
    private Storage? storage { get; set; }

    [Resolved(CanBeNull = true)]
    private INotificationOverlay? notifications { get; set; }

    [Resolved(CanBeNull = true)]
    private IDialogOverlay? dialogOverlay { get; set; }

    [Resolved(CanBeNull = true)]
    private OsuGameBase? game { get; set; }

    [Resolved(CanBeNull = true)]
    private IPerformFromScreenRunner? performer { get; set; }

    [Resolved(CanBeNull = true)]
    private BeatmapManager? beatmapManager { get; set; }

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);

        if (o2lazerImporter != null && game != null)
            game.UnregisterImportHandler(o2lazerImporter);
    }

    #endregion

    [BackgroundDependencyLoader]
    private void load()
    {
        if (o2lazerImporter == null && realm != null && storage != null && game != null)
        {
            o2lazerImporter = new O2LazerFileImporter(realm, storage, notifications, beatmapManager);
            game.RegisterImportHandler(o2lazerImporter);
        }

        if (Config is not O2LazerRulesetConfigManager manager)
            return;

        importPath = manager.GetBindable<string>(O2LazerRulesetSetting.LastImportPath);
        syncSourceFolderCollections = manager.GetBindable<bool>(O2LazerRulesetSetting.SyncSourceFolderCollections);

        var children = new List<Drawable>
        {
            new OsuSpriteText
            {
                Text = O2LazerStrings.SettingsGroupImport,
                Font = OsuFont.GetFont(size: 16, weight: FontWeight.Bold),
                Margin = new MarginPadding { Top = SettingsSection.ITEM_SPACING_V2 * 2 },
                Padding = SettingsPanel.CONTENT_PADDING,
            },
            new ClickableImportPathField
            {
                Current = importPath,
                Clicked = () => performer?.PerformFromScreen(menu => menu.Push(new O2LazerFileImportScreen(manager))),
            },
        };

        children.Add(refreshButton = new RoundedButton
        {
            Text = O2LazerStrings.RefreshBeatmaps,
            TooltipText = O2LazerStrings.RefreshBeatmapsTooltip,
            RelativeSizeAxes = Axes.X,
            Height = 36,
            Action = refreshImportedCharts,
            Padding = SettingsPanel.CONTENT_PADDING,
        });
        children.Add(new DangerousRoundedButton
        {
            Text = O2LazerStrings.DeleteAllImportedFiles,
            RelativeSizeAxes = Axes.X,
            Height = 36,
            Action = confirmDeleteAllO2LazerFiles,
            Padding = SettingsPanel.CONTENT_PADDING,
        });
        children.Add(new OsuSpriteText
        {
            Text = O2LazerStrings.SettingsGroupScrollSpeed,
            Font = OsuFont.GetFont(size: 16, weight: FontWeight.Bold),
            Margin = new MarginPadding { Top = SettingsSection.ITEM_SPACING_V2 * 2 },
            Padding = SettingsPanel.CONTENT_PADDING,
        });
        children.Add(new SettingsItemV2(new FormSliderBar<double>
        {
            Caption = RulesetSettingsStrings.ScrollSpeed,
            Current = manager.GetBindable<double>(O2LazerRulesetSetting.ScrollSpeed),
            KeyboardStep = 1,
            LabelFormat = v => O2LazerStrings.ScrollSpeedTooltipWithO2JamGrade(
                RulesetSettingsStrings.ScrollSpeedTooltip((int)O2LazerDrawableRuleset.ComputeScrollTime(v), v),
                O2LazerGameplayScrollController.GetO2JamSpeedGrade(v)),
        }));
        children.Add(new SettingsItemV2(new FormCheckBox
        {
            Caption = O2LazerStrings.FixedScrollSpeed,
            Current = manager.GetBindable<bool>(O2LazerRulesetSetting.ConstantScrollSpeed),
        }));

        if (enable_visual_offset_settings)
        {
            children.Add(new OsuSpriteText
            {
                Text = O2LazerStrings.SettingsGroupVisual,
                Font = OsuFont.GetFont(size: 16, weight: FontWeight.Bold),
                Margin = new MarginPadding { Top = SettingsSection.ITEM_SPACING_V2 * 2 },
                Padding = SettingsPanel.CONTENT_PADDING,
            });
            children.Add(new VisualOffsetAdjustControl
            {
                Current = manager.GetBindable<double>(O2LazerRulesetSetting.VisualOffset),
                Margin = new MarginPadding { Bottom = 5 },
            });
            children.Add(new SettingsItemV2(new FormCheckBox
            {
                Caption = O2LazerStrings.AdjustVisualOffsetAutomatically,
                HintText = O2LazerStrings.AdjustVisualOffsetAutomaticallyTooltip,
                Current = manager.GetBindable<bool>(O2LazerRulesetSetting.AutomaticallyAdjustVisualOffset),
            }));
        }

        children.AddRange(
        [
            new OsuSpriteText
            {
                Text = O2LazerStrings.SettingsGroupOther,
                Font = OsuFont.GetFont(size: 16, weight: FontWeight.Bold),
                Margin = new MarginPadding { Top = SettingsSection.ITEM_SPACING_V2 * 2 },
                Padding = SettingsPanel.CONTENT_PADDING,
            },
            new SettingsItemV2(new FormCheckBox
            {
                Caption = O2LazerStrings.SyncSourceFolderCollections,
                Current = syncSourceFolderCollections,
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = O2LazerStrings.AutoPlayKeysounds,
                Current = manager.GetBindable<bool>(O2LazerRulesetSetting.AutoPlayKeysounds),
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = O2LazerStrings.PreviewPlayKeysounds,
                Current = manager.GetBindable<bool>(O2LazerRulesetSetting.PreviewPlayKeysounds),
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = O2LazerStrings.UnlockFrameRateLimit,
                HintText = O2LazerStrings.UnlockFrameRateLimitHint,
                Current = manager.GetBindable<bool>(O2LazerRulesetSetting.UnlockFrameRateLimit),
            }),
        ]);

        Children = children;

        importPath.BindValueChanged(updateRefreshButtonState, true);
        syncSourceFolderCollections.BindValueChanged(e =>
        {
            if (o2lazerImporter == null)
                return;

            if (e.NewValue)
                o2lazerImporter.UpdateSourceFolderCollections();
            else
                o2lazerImporter.DeleteSourceFolderCollections();
        }, true);
    }

    private void updateRefreshButtonState(ValueChangedEvent<string> path)
    {
        refreshButton.Enabled.Value = !string.IsNullOrEmpty(path.NewValue) && Directory.Exists(path.NewValue);
    }

    private async void refreshImportedCharts()
    {
        var path = importPath.Value;

        if (o2lazerImporter == null || string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return;

        await o2lazerImporter.Refresh(path);
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

            Current.BindValueChanged(path =>
            {
                pathText.Text = string.IsNullOrEmpty(path.NewValue) ? O2LazerStrings.ImportPathPlaceholder : path.NewValue;
            }, true);
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

    private void confirmDeleteAllO2LazerFiles()
    {
        if (o2lazerImporter == null)
            return;

        // Mirror osu!'s maintenance mass-delete flow: require an explicit (hold-to-)confirm before
        // irreversibly removing every imported O2LAZER beatmap.
        var dialog = new MassDeleteConfirmationDialog(
            () => o2lazerImporter.DeleteAllO2LazerFilesAsync(),
            O2LazerStrings.DeleteAllConfirmation);

        if (dialogOverlay != null)
            dialogOverlay.Push(dialog);
        else
            // No dialog overlay available (e.g. isolated test harness): fall back to direct deletion.
            o2lazerImporter.DeleteAllO2LazerFilesAsync();
    }

}



