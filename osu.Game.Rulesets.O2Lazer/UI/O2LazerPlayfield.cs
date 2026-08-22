using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.IO.Input;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.O2Lazer.Scoring.Judgements;
using osu.Game.Rulesets.O2Lazer.Skinning.Embedded;
using osu.Game.Rulesets.O2Lazer.Skinning.Runtime;
using osu.Game.Rulesets.O2Lazer.UI.Components;
using osu.Game.Rulesets.O2Lazer.UI.Gameplay;
using osu.Game.Rulesets.O2Lazer.UI.Objects;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.O2Lazer.UI;

/// <inheritdoc cref="Playfield" />
/// <summary>
///     Native O2LAZER playfield.  Manages the stage, hit-object container, key-sound playback,
///     judgement display, scroll-speed HUD, and input routing for all O2LAZER layout variants.
/// </summary>
[Cached]
[Cached(typeof(IO2LazerLnScoring))]
public sealed partial class O2LazerPlayfield : Playfield, IKeyBindingHandler<O2LazerAction>, IO2LazerLnScoring
{

    #region Constants

    #endregion

    #region Construction

    public O2LazerPlayfield(O2LazerBeatmap beatmap)
    {
        Beatmap = beatmap;
        activeSkin = new O2LazerEmbeddedSkinSource();
        skinCache = new O2LazerGameplaySkinCache(activeSkin);

        TotalColumns = Math.Max(1, beatmap.TotalColumns);
        LayoutVariant = beatmap.LayoutVariant;
        TimingMap = beatmap.TimingMap;
        ScrollController = new O2LazerGameplayScrollController(TimingMap);
        ScrollController.ScrollSpeedChanged += onScrollSpeedChanged;

        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        RelativeSizeAxes = Axes.Both;

        Stage = new O2LazerStage(this);
        Stage.SkinHitTargetPositionChanged += onSkinHitTargetPositionChanged;

        InternalChildren =
        [
            Stage,
        ];
    }

    #endregion

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        ScrollController.ScrollSpeedChanged -= onScrollSpeedChanged;
        Stage.SkinHitTargetPositionChanged -= onSkinHitTargetPositionChanged;
        NewResult -= onNewResult;
        parentSkin.SourceChanged -= updateEmbeddedSkinFallback;
        base.Dispose(isDisposing);
        skinCache.Dispose();
        activeSkin.DisposeEmbeddedSkins();
    }

    #endregion

    #region HitObject routing

    public override void Add(HitObject hitObject)
    {
        if (hitObject is O2LazerHitObject o2lazerHo)
        {
            var col = o2lazerHo.Column;
            if (col >= 0 && col < Stage.Columns.Length)
            {
                ((Playfield)Stage.Columns[col]).Add(hitObject);
                return;
            }
        }

        base.Add(hitObject);
    }

    #endregion

    #region Skin

    private void updateEmbeddedSkinFallback()
    {
        activeSkin.SetSources(parentSkin, O2LazerEmbeddedSkinFallbackFactory.Create(parentSkin.AllSources, Beatmap, host.Renderer));
    }

    #endregion

    #region O2LazerEvents

    #endregion

    #region Public properties

    public int TotalColumns { get; }

    public O2LazerLayoutVariant LayoutVariant { get; }

    public O2LazerStage Stage { get; }

    public override Quad SkinnableComponentScreenSpaceDrawQuad => Stage.ScreenSpaceDrawQuad;

    internal void AddBehindStage(Drawable drawable) => AddInternal(drawable);

    public O2LazerTimingMap? TimingMap { get; }

    internal O2LazerGameplayScrollController ScrollController { get; }

    /// <summary>
    /// Applied only to predictable scrolling visuals so judgements remain based on <c>Time.Current</c>.
    /// </summary>
    internal BindableDouble VisualOffset { get; } = new();

    #endregion

    #region Skin / DI

    [Cached(typeof(ISkinSource))]
    private readonly O2LazerEmbeddedSkinSource activeSkin;

    [Cached]
    private readonly O2LazerGameplaySkinCache skinCache;

    internal readonly O2LazerBeatmap Beatmap;

    private O2LazerHealthProcessor? healthProcessor => resolvedHealthProcessor as O2LazerHealthProcessor;

    private O2LazerScoreProcessor? scoreProcessor => resolvedScoreProcessor as O2LazerScoreProcessor;

    [Resolved(CanBeNull = true)]
    private HealthProcessor? resolvedHealthProcessor { get; set; }

    [Resolved(CanBeNull = true)]
    private ScoreProcessor? resolvedScoreProcessor { get; set; }

    [Resolved]
    private GameHost host { get; set; } = null!;

    [Resolved]
    private ISkinSource parentSkin { get; set; } = null!;

    [Resolved]
    private IO2LazerGameplayEvents gameplayEvents { get; set; } = null!;

    #endregion

    #region Input

    public bool OnPressed(KeyBindingPressEvent<O2LazerAction> e)
    {
        switch (e.Action)
        {
            case O2LazerAction.IncreaseScrollSpeed:
                ScrollController.AdjustScrollSpeed(1);
                return true;

            case O2LazerAction.DecreaseScrollSpeed:
                ScrollController.AdjustScrollSpeed(-1);
                return true;
        }

        var column = O2LazerKeyBindingConfiguration.ActionToColumn(e.Action, LayoutVariant);

        if (column == null || column.Value >= TotalColumns)
            return false;

        var outcome = Stage.Columns[column.Value].HandlePress(Time.Current);

        if (outcome is { Kind: PressOutcomeKind.EmptyPoor, ExpectedTime: { } expectedTime })
            registerEmptyPoor(expectedTime, outcome.Column);

        return outcome.Kind == PressOutcomeKind.Hit;
    }

    public void OnReleased(KeyBindingReleaseEvent<O2LazerAction> e)
    {
        switch (e.Action)
        {
            case O2LazerAction.IncreaseScrollSpeed:
            case O2LazerAction.DecreaseScrollSpeed:
                return;
        }

        var column = O2LazerKeyBindingConfiguration.ActionToColumn(e.Action, LayoutVariant);

        if (column == null || column.Value >= TotalColumns)
            return;

        Stage.Columns[column.Value].HandleRelease(Time.Current);
    }

    #endregion

    #region Scroll Speed

    public double ScrollSpeed => ScrollController.ScrollSpeed;

    private void onScrollSpeedChanged(double multiplier)
    {
        gameplayEvents.RaiseScrollSpeedChanged(multiplier);
        // The framework's default lifetime starts at hit time; O2LAZER scroll needs notes alive
        // before then so near-future objects can be positioned and judged.
        RefreshAllLifetimes();
    }

    private void onSkinHitTargetPositionChanged(float position)
    {
        ScrollController.SetHitTargetPosition(position);

        if (IsLoaded)
            RefreshAllLifetimes();
    }

    internal void RefreshAllLifetimes()
    {
        var currentTime = IsLoaded ? Time.Current : (double?)null;

        foreach (var column in Stage.Columns)
        {
            if (column.HitObjectContainer is O2LazerColumnHitObjectContainer container)
                container.RefreshAllEntries(currentTime);
        }
    }

    internal void ApplyVisualOffsetToAllLifetimes()
    {
        foreach (var column in Stage.Columns)
        {
            if (column.HitObjectContainer is O2LazerColumnHitObjectContainer container)
                container.ApplyVisualOffsetToAllEntries();
        }
    }

    #endregion

    #region Lifecycle

    internal double DisplayTime { get; private set; }

    [BackgroundDependencyLoader(true)]
    private void load()
    {
        ScrollController.SetHitTargetPosition(Stage.SkinHitTargetPosition);

        parentSkin.SourceChanged += updateEmbeddedSkinFallback;
        updateEmbeddedSkinFallback();
    }

    protected override HitObjectLifetimeEntry CreateLifetimeEntry(HitObject hitObject) =>
        new O2LazerHitObjectLifetimeEntry(hitObject, ScrollController, () => VisualOffset.Value);

    protected override void LoadComplete()
    {
        base.LoadComplete();

        populateMeasureLines();

        // Subscribe to per-column NewResult events so O2LazerPlayfield aggregates all results.
        foreach (var column in Stage.Columns)
        {
            if (column is Playfield pf)
            {
                pf.NewResult += onNewResult;
                AddNested(pf);
            }
        }

        VisualOffset.BindValueChanged(_ => ApplyVisualOffsetToAllLifetimes());
        RefreshAllLifetimes();
    }

    protected override void Update()
    {
        DisplayTime = Time.Current + VisualOffset.Value * ScrollController.PlaybackRate;
        ScrollController.Update(DisplayTime);
        base.Update();

        updateStageScale();
    }

    #endregion

    #region Layout

    private void populateMeasureLines()
    {
        if (TimingMap == null)
            return;

        Stage.MeasureLineArea.SetTimingMap(TimingMap, ScrollController, Stage);
    }

    private void updateStageScale()
    {
        if (Stage.HasHudTransform)
            return;

        if (!Stage.IsLoaded || Stage.DrawWidth <= 0 || DrawWidth <= 0)
            return;

        // O2Jam uses the current osu! skin's mania-style HUD, so the stage must not reserve
        // side space for a legacy O2LAZER health bar; otherwise notes are narrower than mania.
        var availableWidth = Math.Max(1, DrawWidth);
        var scale = Math.Min(1, availableWidth / Stage.DrawWidth);

        if (float.IsFinite(scale) && scale > 0)
            Stage.Scale = new Vector2(scale, 1);
    }

    #endregion

    #region Judgements

    private void onNewResult(DrawableHitObject drawableHitObject, JudgementResult result)
    {
        if (drawableHitObject is not DrawableO2LazerHitObject o2lazerHitObject)
            return;

        requestJudgementDisplay(result.Type);
    }

    private void registerEmptyPoor(double expectedTime, int column)
    {
        scoreProcessor?.RegisterEmptyPoor(Time.Current, expectedTime, column);
        healthProcessor?.RegisterEmptyPoor(Time.Current);
        requestJudgementDisplay(HitResult.Miss);
    }

    private void requestJudgementDisplay(HitResult result)
    {
        gameplayEvents.RaiseJudgementDisplayed(result);
    }

    /// <summary>
    ///     Registers a separate CN/HCN endpoint through the score and health processors.
    /// </summary>
    public void ApplySyntheticLongNoteEndpoint(DrawableO2LazerHitObject drawable, O2LazerLongNoteEndpointResult endpoint)
    {
        if (drawable.HitObject is not O2LazerLongNote)
            return;

        var scoreResult = scoreProcessor?.ApplySyntheticLongNoteEndpoint(endpoint);

        if (scoreResult != null)
            healthProcessor?.ApplySyntheticLongNoteEndpoint(scoreResult);

        if (endpoint.Kind == O2LazerLongNoteEndpointKind.Tail && endpoint.Result.IsHit())
        {
            var column = Math.Clamp(drawable.HitObject.Column, 0, Stage.Columns.Length - 1);
            Stage.Columns[column].TriggerHitExplosion(drawable.HitObject is O2LazerLongNote);
        }

        requestJudgementDisplay(endpoint.Result);
    }

    /// <summary>
    ///     Applies a HellChargeNote body gauge tick for the currently pressed column.
    /// </summary>
    public void ApplyHellChargeTick(bool holding, double scale = 0.5) => healthProcessor?.ApplyHellChargeTick(holding, scale, Time.Current);

    #endregion

}
