#nullable disable

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Screens;
using osuTK;

namespace osu.Game.Rulesets.O2Lazer.IO.Import;

public partial class O2LazerFileImportScreen(O2LazerRulesetConfigManager config = null) : OsuScreen
{
    public override bool HideOverlaysOnEnter => true;

    private const float duration = 300;
    private const float button_height = 50;
    private const float button_vertical_margin = 10;

    private readonly RoundedButton[] buttons = [null!, null!, null!];

    private OsuFileSelector fileSelector = null!;
    private Container contentContainer = null!;
    private TextFlowContainer currentFileText = null!;

    [Cached]
    private OverlayColourProvider colourProvider = new(OverlayColourScheme.Purple);

    private FillFlowContainer buttonGroup;
    private Bindable<string> lastImportPath;

    private O2LazerFileImporter importer;
    private LoadingLayer loadingLayer = null!;
    private bool isImporting;

    [Resolved(CanBeNull = true)]
    private O2LazerRulesetConfigManager resolvedConfig { get; set; }

    [Resolved(CanBeNull = true)]
    private RealmAccess realm { get; set; }

    [Resolved(CanBeNull = true)]
    private Storage storage { get; set; }

    [Resolved(CanBeNull = true)]
    private INotificationOverlay notifications { get; set; }

    public override void OnEntering(ScreenTransitionEvent e)
    {
        base.OnEntering(e);

        contentContainer.ScaleTo(0.95f).ScaleTo(1, duration, Easing.OutQuint);
        this.FadeInFromZero(duration);
    }

    public override bool OnExiting(ScreenExitEvent e)
    {
        contentContainer.ScaleTo(0.95f, duration, Easing.OutQuint);
        this.FadeOut(duration, Easing.OutQuint);

        return base.OnExiting(e);
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        lastImportPath = (config ?? resolvedConfig)?.GetBindable<string>(O2LazerRulesetSetting.LastImportPath);
        var lastPath = lastImportPath?.Value;

        importer = realm != null && storage != null
            ? new O2LazerFileImporter(realm, storage, notifications)
            : null!;

        buttonGroup = new FillFlowContainer
        {
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.BottomCentre,
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, button_vertical_margin),
            Width = 0.9f,
            Padding = new MarginPadding { Bottom = 2 * button_vertical_margin },
            Children =
            [
                buttons[0] = new RoundedButton
                {
                    Text = O2LazerStrings.ImportSelectedFile,
                    RelativeSizeAxes = Axes.X,
                    Height = button_height,
                    Action = () => startImport(fileSelector.CurrentFile.Value?.FullName),
                },
                buttons[1] = new RoundedButton
                {
                    Text = O2LazerStrings.ImportCurrentFolder,
                    RelativeSizeAxes = Axes.X,
                    Height = button_height,
                    Action = () => Task.Run(() => startDirectoryImport(false)),
                },
                buttons[2] = new RoundedButton
                {
                    Text = O2LazerStrings.ImportDirectoryRecursive,
                    TooltipText = O2LazerStrings.ImportDirectoryRecursiveTooltip,
                    RelativeSizeAxes = Axes.X,
                    Height = button_height,
                    Action = () => Task.Run(() => startDirectoryImport(true)),
                },
            ],
        };

        InternalChild = contentContainer = new Container
        {
            Masking = true,
            CornerRadius = 10,
            RelativeSizeAxes = Axes.Both,
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(0.9f, 0.8f),
            Children =
            [
                fileSelector = new OsuFileSelector(
                    !string.IsNullOrEmpty(lastPath) && Directory.Exists(lastPath) ? lastPath : null,
                    Constant.O2LAZER_EXTENSIONS)
                {
                    RelativeSizeAxes = Axes.Both,
                    Width = 0.65f,
                },
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Width = 0.35f,
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Children =
                    [
                        new Box
                        {
                            Colour = colourProvider.Background4,
                            RelativeSizeAxes = Axes.Both,
                        },
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Child = new OsuScrollContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Child = currentFileText = new TextFlowContainer(t => t.Font = OsuFont.Default.With(size: 30))
                                {
                                    AutoSizeAxes = Axes.Y,
                                    RelativeSizeAxes = Axes.X,
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    TextAnchor = Anchor.Centre,
                                },
                                ScrollContent =
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                },
                            },
                        },
                        buttonGroup,
                    ],
                },
                loadingLayer = new LoadingLayer(dimBackground: true)
                {
                    RelativeSizeAxes = Axes.Both,
                    Alpha = 0f,
                },
            ],
        };

        fileSelector.CurrentFile.BindValueChanged(fileChanged, true);
        fileSelector.CurrentPath.BindValueChanged(directoryChanged);
    }

    private void directoryChanged(ValueChangedEvent<DirectoryInfo> directoryChangedEvent)
    {
        fileSelector.CurrentFile.Value = null;

        var newDirectory = directoryChangedEvent.NewValue;
        var hasO2LazerFiles = newDirectory != null
                          && newDirectory.Exists
                          && newDirectory.EnumerateFiles().Any(f => Constant.IsChartFile(f.FullName));

        buttons[1].Enabled.Value = hasO2LazerFiles;

        if (newDirectory != null)
        {
            lastImportPath?.Value = newDirectory.FullName;
        }
    }

    private void fileChanged(ValueChangedEvent<FileInfo> selectedFile)
    {
        buttons[0].Enabled.Value = selectedFile.NewValue != null;
        currentFileText.Text = selectedFile.NewValue?.Name ?? O2LazerStrings.SelectFileOrFolder;
    }

    private void startImport(params string[] paths)
    {
        if (paths.Length == 0)
            return;

        // Schedule the UI setup (FadeIn + flag) on the update thread so this method is safe to
        // call from button actions, directory-change handlers, or thread-pool continuations.
        Task.Run(async () =>
        {
            await importer.Import(paths).ConfigureAwait(false);
            Schedule(() =>
            {
                loadingLayer.FadeOut(duration);
                fileSelector.CurrentPath.TriggerChange();
                isImporting = false;
            });
        });
    }

    private void startDirectoryImport(bool recursive)
    {
        if (isImporting) return;

        Schedule(() =>
        {
            if (isImporting) return;

            isImporting = true;
            loadingLayer.FadeIn(duration);
        });

        var path = fileSelector.CurrentPath.Value;
        if (path == null || !path.Exists)
            return;

        var files = Directory.GetFiles(
            path.ToString(), "*.*",
            recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

        var filesToImport = files
            .Where(Constant.IsChartFile)
            .ToArray();

        if (filesToImport.Length == 0)
            return;

        startImport(filesToImport);

        Schedule(() =>
        {
            loadingLayer.FadeOut(duration);
            fileSelector.CurrentPath.TriggerChange();
            isImporting = false;
        });
    }
}
