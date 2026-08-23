using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Pooling;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Scoring.Judgements;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Configuration;
using osu.Game.Rulesets.O2Lazer.UI.Gameplay;
using osu.Game.Rulesets.O2Lazer.UI.Objects;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.UI.Components;

[Cached]
public partial class O2LazerColumn : Playfield, IO2LazerColumn
{
    public const float COLUMN_WIDTH = 42;
    public const float SCRATCH_COLUMN_WIDTH = 50;

    public readonly int Index;

    public int ColumnIndex => Index;

    public bool IsScratch { get; }

    public readonly Bindable<Color4> AccentColour = new(Color4.Black);

    // Owned by the column but parented to a stage-level layer (above the judgement line) by O2LazerStage,
    // so hit explosions render on top of the stage hitTarget instead of behind it.
    public Container HitExplosionArea { get; } = new() { RelativeSizeAxes = Axes.Y };

    public Drawable KeyArea { get; }

    public Container KeyAreaUnderNotesLayer { get; } = new() { RelativeSizeAxes = Axes.Both };

    /// <summary>
    ///     When <c>true</c>, this column is hidden from layout — zero width, zero
    ///     alpha, and <see cref="updateFromSkin"/> will not restore visual properties.
    /// </summary>
    public bool Hidden
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;

            if (value)
            {
                Width = 0;
                Alpha = 0;
                Margin = new MarginPadding();
            }
        }
    }

    internal const float HIT_OBJECT_DEPTH = 0;

    internal O2LazerLayoutVariant LayoutVariant { get; }

    internal float HitTargetPosition => ParentPlayfield.Stage.HitTargetPosition;

    internal double ScrollSpeedMultiplier => ParentPlayfield.ScrollController.ScrollSpeedMultiplier;

    internal double VisualOffset => ParentPlayfield.VisualOffset.Value;

    protected O2LazerPlayfield ParentPlayfield { get; }

    private O2LazerColumnKeySound? keySound;
    private readonly O2LazerHitExplosionPool normalHitExplosionPool;
    private readonly O2LazerHitExplosionPool longNoteHitExplosionPool;
    private readonly List<(DrawableO2LazerHitObject Drawable, O2LazerJudgementCandidate Candidate)> pressCandidates = [];
    private readonly List<O2LazerJudgementCandidate> pressJudgementCandidates = [];

    [Resolved]
    private ISkinSource skin { get; set; } = null!;

    public O2LazerColumn(int index, O2LazerPlayfield playfield)
    {
        ParentPlayfield = playfield;
        Index = index;
        LayoutVariant = playfield.LayoutVariant;
        IsScratch = O2LazerLayout.IsScratchColumn(index, LayoutVariant);

        RelativeSizeAxes = Axes.Y;
        Width = DefaultColumnWidth(index, LayoutVariant);
        HitObjectContainer.Depth = HIT_OBJECT_DEPTH;

        normalHitExplosionPool = new O2LazerHitExplosionPool(
            new O2LazerSkinComponentLookup(O2LazerSkinComponents.HitExplosion, LayoutVariant, Index), 2);
        longNoteHitExplosionPool = new O2LazerHitExplosionPool(
            // Head, first hold pulse, and tail can overlap within the explosion fade lifetime.
            new O2LazerSkinComponentLookup(O2LazerSkinComponents.HitExplosion, LayoutVariant, Index, true), 3);

        InternalChildren =
        [
            normalHitExplosionPool,
            longNoteHitExplosionPool,
            KeyAreaUnderNotesLayer,
        ];

        KeyArea = new SkinnableDrawable(new O2LazerSkinComponentLookup(O2LazerSkinComponents.KeyArea, LayoutVariant, index))
        {
            RelativeSizeAxes = Axes.Y,
            CentreComponent = false,
        };
    }

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        NewResult -= onColumnNewResult;
        base.Dispose(isDisposing);

        if (skin.IsNotNull())
            skin.SourceChanged -= updateFromSkin;
    }

    #endregion

    public static O2LazerColumn Create(int index, O2LazerPlayfield playfield)
    {
        var providerType = O2LazerColumnFactory.GetColumnProviderType(index);
        var genericType = typeof(O2LazerColumnGeneric<>).MakeGenericType(providerType);
        return (O2LazerColumn)Activator.CreateInstance(genericType, index, playfield)!;
    }

    protected override HitObjectContainer CreateHitObjectContainer()
        => new O2LazerColumnHitObjectContainer(
            ParentPlayfield.ScrollController,
            () => HitTargetPosition);

    protected override HitObjectLifetimeEntry CreateLifetimeEntry(HitObject hitObject)
        => new O2LazerHitObjectLifetimeEntry(
            hitObject,
            ParentPlayfield.ScrollController,
            () => ParentPlayfield.VisualOffset.Value);

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // Build the keysound cursor from this column's own hit-object slice so the per-column
        // player only ever scans notes it actually owns. The slice mirrors the playfield's global
        // ordering ( StartTime, then Column ) so cursor reset/seek behaviour stays consistent.
        var columnHitObjects = ParentPlayfield.Beatmap.HitObjects
            .Where(h => h.Column == Index)
            .OrderBy(h => h.StartTime)
            .ThenBy(h => h.Column)
            .ToArray();

        keySound = new O2LazerColumnKeySound(columnHitObjects, HitObjectContainer);
        AddInternal(keySound);

        NewResult += onColumnNewResult;
    }

    internal static float DefaultColumnWidth(int index, O2LazerLayoutVariant layoutVariant) => layoutVariant switch
    {
        O2LazerLayoutVariant.O2Jam7K => index == 3 ? 70 : 80,
        _ => O2LazerLayout.IsScratchColumn(index, layoutVariant) ? SCRATCH_COLUMN_WIDTH : COLUMN_WIDTH,
    };

    [BackgroundDependencyLoader]
    private void load()
    {
        skin.SourceChanged += updateFromSkin;
        updateFromSkin();
    }

    private void updateFromSkin()
    {
        if (Hidden)
            return;

        var lookup = new O2LazerSkinComponentLookup(O2LazerSkinComponents.ColumnBackground, LayoutVariant, Index);
        AccentColour.Value = skin.GetConfig<O2LazerSkinConfigurationLookup, Color4>(
                                 new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour, lookup))?.Value
                             ?? Color4.Black;
        Width = skin.GetConfig<O2LazerSkinConfigurationLookup, float>(new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ColumnWidth, lookup))?.Value
                ?? DefaultColumnWidth(Index, LayoutVariant);

        // For 2P/scratch-on-right, ColumnSpacing indices must be remapped to follow
        // visual column order [keys…, scratch] rather than O2LAZER index order.
        int? spacingLeftCol;
        int? spacingRightCol;

        if (O2LazerLayout.Is2P(lookup.LayoutVariant) && lookup.ColumnIndex is int colIdx)
        {
            var totalCols = O2LazerLayout.GetTotalColumns(LayoutVariant);
            (spacingLeftCol, spacingRightCol) = O2LazerLayout.RemapColum2PGapIdx(colIdx, totalCols);
        }
        else
        {
            spacingLeftCol = lookup.ColumnIndex;
            spacingRightCol = lookup.ColumnIndex;
        }

        var spacingLookupLeft = spacingLeftCol != null
            ? new O2LazerSkinComponentLookup(O2LazerSkinComponents.ColumnBackground, LayoutVariant, spacingLeftCol.Value)
            : null;

        var spacingLookupRight = spacingRightCol != null
            ? new O2LazerSkinComponentLookup(O2LazerSkinComponents.ColumnBackground, LayoutVariant, spacingRightCol.Value)
            : null;

        Margin = new MarginPadding
        {
            Left = spacingLookupLeft != null
                ? skin.GetConfig<O2LazerSkinConfigurationLookup, float>(new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.LeftColumnSpacing, spacingLookupLeft))?.Value
                  ?? (LayoutVariant == O2LazerLayoutVariant.O2Jam7K ? 1 : 0)
                : 0,
            Right = spacingLookupRight != null
                ? skin.GetConfig<O2LazerSkinConfigurationLookup, float>(new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.RightColumnSpacing, spacingLookupRight))?.Value
                  ?? (LayoutVariant == O2LazerLayoutVariant.O2Jam7K ? 1 : 0)
                : 0,
        };

    }

    protected override void OnNewDrawableHitObject(DrawableHitObject drawableHitObject)
    {
        base.OnNewDrawableHitObject(drawableHitObject);

        if (drawableHitObject is DrawableO2LazerHitObject o2lazerObject)
            o2lazerObject.AccentColour.BindTo(AccentColour);
    }

    #region Hit explosions / landmine

    /// <summary>
    /// Spawns a hit explosion (hit light) in this column. Used for note hits (via this column's
    /// own NewResult), LN head/tail endpoints, and the repeating hit light fired throughout an
    /// LN hold. Moved here from O2LazerPlayfield so the column owns its lane's hit-light visuals.
    /// </summary>
    public void TriggerHitExplosion(bool isLongNote)
    {
        var pool = isLongNote ? longNoteHitExplosionPool : normalHitExplosionPool;
        HitExplosionArea.Add(pool.Get(explosion => explosion.ApplyPositionOffset(ParentPlayfield.Stage.HitTargetPositionOffset)));
    }

    private sealed partial class O2LazerHitExplosionPool(O2LazerSkinComponentLookup lookup, int initialSize)
        : DrawablePool<O2LazerHitExplosion>(initialSize)
    {
        protected override O2LazerHitExplosion CreateNewDrawable() => new(lookup);
    }

    /// <summary>
    /// Triggers a note-hit / LN-tail definition key. All columns and automation route through the
    /// shared key Track, so retriggering the definition has the same truncation behaviour.
    /// </summary>
    public void PlaySample(ushort? sampleKey, int volume) => keySound?.PlaySample(sampleKey, volume);

    private void onColumnNewResult(DrawableHitObject drawable, JudgementResult result)
    {
        if (drawable is not DrawableO2LazerHitObject o2lazerHitObject)
            return;

        // O2LAZER POOR is represented by framework Meh, which IsHit() considers successful even though
        // it must not produce the hit feedback reserved for BAD and better judgements.
        if (ShouldTriggerHitExplosion(result.Type))
            TriggerHitExplosion(o2lazerHitObject.HitObject is O2LazerLongNote);
    }

    internal static bool ShouldTriggerHitExplosion(HitResult result) => result != HitResult.Meh && result.IsHit();

    #endregion

    #region Input

    public bool IsPressed { get; private set; }

    public PressOutcome HandlePress(double time)
    {
        IsPressed = true;

        pressCandidates.Clear();
        pressJudgementCandidates.Clear();

        foreach (var alive in HitObjectContainer.AliveEntries.Values)
        {
            if (alive is not DrawableO2LazerHitObject d
                || d.Judged)
            {
                continue;
            }

            var candidate = new O2LazerJudgementCandidate(
                d.HitObject.StartTime,
                d.HitObject.GetEndTime(),
                d.HitObject.Column,
                d.HitObject.EffectiveJudgementRate,
                d.HitObject is O2LazerLongNote);
            pressCandidates.Add((d, candidate));
            pressJudgementCandidates.Add(candidate);
        }

        var selection = O2LazerJudgementSelector.SelectPress(LayoutVariant, Index, pressJudgementCandidates, time);
        var selectedSamplePlayed = false;

        if (!selection.IsEmptyPoor && selection.Candidate is { } selectedCandidate)
        {
            DrawableO2LazerHitObject? target = null;
            foreach (var candidate in pressCandidates)
            {
                if (candidate.Candidate.Equals(selectedCandidate))
                {
                    target = candidate.Drawable;
                    break;
                }
            }

            if (target != null && ShouldPlaySelectedSampleBeforeJudgement(LayoutVariant))
            {
                keySound?.PlaySampleAtTime(target.HitObject.SampleKey, target.HitObject.SampleVolume, target.HitObject.StartTime);
                selectedSamplePlayed = true;
            }

            if (target?.TryHit(selection.Result) == true)
            {
                if (ShouldPlaySelectedSampleAfterSuccessfulJudgement(LayoutVariant))
                    keySound?.PlaySample(target.HitObject.SampleKey, target.HitObject.SampleVolume);

                return PressOutcome.Hit;
            }
        }

        if (ShouldPlayPendingKeySound(selectedSamplePlayed))
            keySound?.PlayKeySound();

        // O2Jam key-downs may sound the lane's pending note, but they never create O2LAZER's
        // separate empty-key POOR judgement.
        if (LayoutVariant == O2LazerLayoutVariant.O2Jam7K)
            return PressOutcome.Empty;

        return selection is { IsEmptyPoor: true, Candidate: { } emptyPoorCandidate }
            ? PressOutcome.ForEmptyPoor(emptyPoorCandidate.StartTime, emptyPoorCandidate.Column)
            : PressOutcome.Empty;
    }

    internal static bool ShouldPlayPendingKeySound(bool selectedSampleWasRequested)
        => !selectedSampleWasRequested;

    internal static bool ShouldPlaySelectedSampleBeforeJudgement(O2LazerLayoutVariant layout)
        => layout == O2LazerLayoutVariant.O2Jam7K;

    internal static bool ShouldPlaySelectedSampleAfterSuccessfulJudgement(O2LazerLayoutVariant layout)
        => layout != O2LazerLayoutVariant.O2Jam7K;

    public void HandleRelease(double time)
    {
        if (!IsPressed)
            return;

        IsPressed = false;

        // Release: find the earliest held LN in this column and let it judge the key-up.
        // We must include LNs released before the tail window (a fast release is a drop,
        // scored as POOR) — filtering by the release window here would leave the note
        // frozen at the judgement line until its tail time passed.
        DrawableO2LazerHitObject? heldNote = null;

        foreach (var alive in HitObjectContainer.AliveEntries.Values)
        {
            if (alive is not DrawableO2LazerHitObject d) continue;
            if (d is not ILongNoteHolder ln || !ln.IsHoldingLongNote)
                continue;

            if (heldNote == null || d.HitObject.GetEndTime() < heldNote.HitObject.GetEndTime())
                heldNote = d;
        }

        if (heldNote is ILongNoteHolder ln2)
        {
            var tailTable = O2LazerJudgementProfileProvider.GetTable(LayoutVariant, Index, heldNote.HitObject.EffectiveJudgementRate, tail: true);
            var releaseOffset = time - heldNote.HitObject.GetEndTime();

            // O2Jam release events do not carry a playback sound; the LN tail is a
            // judgement marker only and must not retrigger a hitsound on key-up.
            ln2.TryRelease(releaseOffset, tailTable);
        }
    }

    #endregion

}
