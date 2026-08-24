using System;
using osu.Framework.Graphics;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.UI.Gameplay;
using osu.Game.Rulesets.O2Lazer.UI.Objects;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.UI.Scrolling;

namespace osu.Game.Rulesets.O2Lazer.UI.Components;

public sealed partial class O2LazerColumnHitObjectContainer : HitObjectContainer
{

    private readonly O2LazerGameplayScrollController scrollController;
    private readonly Func<float> getHitTargetPosition;

    internal O2LazerColumnHitObjectContainer(
        O2LazerGameplayScrollController scrollController,
        Func<float> getHitTargetPosition)
    {
        this.scrollController = scrollController;
        this.getHitTargetPosition = getHitTargetPosition;
        RelativeSizeAxes = Axes.Both;
    }

    /// <summary>
    ///     Re-compute lifetimes for every entry in this container.
    ///     Triggered on load completion and whenever the user adjusts the scroll speed.
    /// </summary>
    public void RefreshAllEntries(double? currentTime = null)
    {
        foreach (var entry in Entries)
        {
            if (entry is O2LazerHitObjectLifetimeEntry o2lazerEntry)
                o2lazerEntry.RefreshLifetime(currentTime);
        }
    }

    internal void ApplyVisualOffsetToAllEntries()
    {
        foreach (var entry in Entries)
        {
            if (entry is O2LazerHitObjectLifetimeEntry o2lazerEntry)
                o2lazerEntry.ApplyVisualOffset();
        }
    }

    protected override void UpdateAfterChildrenLife()
    {
        base.UpdateAfterChildrenLife();

        // Skip positioning until layout provides a valid column height; otherwise notes
        // momentarily cluster at the judgement line before the first sized frame.
        if (DrawHeight <= 0)
            return;

        var currentScrollPos = scrollController.CurrentScrollPosition;
        var hitTarget = getHitTargetPosition();
        var scale = scrollController.ScrollSpeedMultiplier / Math.Max(1.0, scrollController.ScrollRange)
                    * Math.Max(1f, DrawHeight - hitTarget);

        // Position all alive entries in this column
        foreach (var entry in AliveEntries)
        {
            if (entry.Value is not DrawableO2LazerHitObject note)
                continue;

            var hitObject = note.HitObject!;
            var startPosition = scrollController.GetVisualScrollPosition(hitObject.StartTime, hitObject.ScrollPositionAtStartTime);
            var offset = (float)((startPosition - currentScrollPos) * scale);
            var y = scrollController.Direction == ScrollingDirection.Up
                ? hitTarget + offset
                : -(hitTarget + offset);

            note.Y = y;

            if (note is ILongNoteHolder ln && hitObject is O2LazerLongNote longNote)
            {
                var endPosition = scrollController.GetVisualScrollPosition(longNote.EndTime, longNote.ScrollPositionAtEndTime);
                var endOffset = (float)((endPosition - currentScrollPos) * scale);
                var endY = scrollController.Direction == ScrollingDirection.Up ? hitTarget + endOffset : -(hitTarget + endOffset);
                ln.UpdateBodyGeometry(y, endY);
            }

            if (note.RequiresColumnFrameUpdate)
                note.UpdateColumnFrame();
        }
    }
}
