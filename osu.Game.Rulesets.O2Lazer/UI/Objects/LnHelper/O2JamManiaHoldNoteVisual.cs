// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Drawables;
using osu.Game.Rulesets.O2Lazer.Skinning.Runtime;
using osu.Game.Rulesets.UI.Scrolling;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.UI.Objects.LnHelper;

/// <summary>
/// The osu!mania hold-note hierarchy adapted to O2Jam hit objects.
/// The body and tail are drawn through proxies inside a shrinking mask while the
/// head remains attached to the bottom edge of the sizing container.
/// </summary>
internal sealed partial class O2JamManiaHoldNoteVisual : CompositeDrawable
{
    internal O2LazerManiaLongNoteBody Body { get; }

    internal O2LazerCachedSkinnableDrawable Tail { get; }

    internal float SizingHeight => sizingContainer.Height;

    private readonly Container sizingContainer;
    private readonly Container maskingContainer;
    private readonly Container headHost;
    private readonly Container tailHost;
    private Container? attachedHead;

    private IBindable<ScrollingDirection> direction = null!;

    [Resolved]
    private IScrollingInfo scrollingInfo { get; set; } = null!;

    private bool headAttached;
    private bool wasPinned;
    private bool dropped;
    private float tailVisualHeight => Tail.Drawable?.DrawHeight ?? 0;

    public O2JamManiaHoldNoteVisual(O2LazerLayoutVariant layoutVariant, int column)
    {
        Anchor = Anchor.BottomLeft;
        Origin = Anchor.BottomLeft;
        RelativeSizeAxes = Axes.X;

        Container maskedContents;

        InternalChildren =
        [
            sizingContainer = new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.Both,
                Children =
                [
                    maskingContainer = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Child = maskedContents = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                        },
                    },
                    headHost = new Container { RelativeSizeAxes = Axes.Both },
                ],
            },
            Body = new O2LazerManiaLongNoteBody
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                RelativeSizeAxes = Axes.X,
                BodyColour = Color4.Cyan,
                Alpha = 0,
            },
            tailHost = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Child = Tail = new O2LazerCachedSkinnableDrawable(
                    new O2LazerSkinComponentLookup(O2LazerSkinComponents.HoldNoteTail, layoutVariant, column),
                    _ => new O2LazerManiaDefaultNotePiece())
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.BottomLeft,
                    AutoSizeHeight = true,
                    ComponentAnchor = Anchor.BottomCentre,
                    Alpha = 0,
                },
            },
        ];

        Body.SetSkinLookup(layoutVariant, column);
        maskedContents.AddRange([Body.CreateProxy(), tailHost.CreateProxy()]);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        direction = scrollingInfo.Direction.GetBoundCopy();
        direction.BindValueChanged(_ => ApplyDirection(), true);
    }

    private void ApplyDirection()
    {
        var isUp = scrollingInfo.Direction.Value == ScrollingDirection.Up;

        Anchor = Origin = isUp ? Anchor.TopLeft : Anchor.BottomLeft;
        sizingContainer.Anchor = sizingContainer.Origin = isUp ? Anchor.BottomLeft : Anchor.TopLeft;
        Body.Anchor = Body.Origin = isUp ? Anchor.TopLeft : Anchor.BottomLeft;
        Tail.Anchor = isUp ? Anchor.BottomLeft : Anchor.TopLeft;
        Tail.Origin = isUp ? Anchor.TopLeft : Anchor.BottomLeft;
        Tail.SetComponentAnchor(isUp ? Anchor.TopCentre : Anchor.BottomCentre);

        if (attachedHead != null)
            attachedHead.Anchor = attachedHead.Origin = isUp ? Anchor.TopLeft : Anchor.BottomLeft;
    }

    internal void AttachHead(Container noteContainer)
    {
        if (headAttached)
            throw new InvalidOperationException("The hold-note head is already attached.");

        if (noteContainer.Parent != null)
            throw new InvalidOperationException("Attach the hold-note head before parenting it.");

        headAttached = true;
        attachedHead = noteContainer;
        noteContainer.Anchor = noteContainer.Origin = scrollingInfo?.Direction.Value == ScrollingDirection.Up ? Anchor.TopLeft : Anchor.BottomLeft;
        noteContainer.RelativeSizeAxes = Axes.X;
        noteContainer.AutoSizeAxes = Axes.Y;
        noteContainer.Y = 0;
        headHost.Child = noteContainer;
    }

    internal void UpdateGeometry(float fullHeight, float consumedHeight, float headHeight, bool pinActive, bool holding)
    {
        if (scrollingInfo == null)
            return;

        ApplyDirection();

        Height = Math.Max(0, fullHeight);

        var tailHeight = tailVisualHeight;
        var isUp = scrollingInfo?.Direction.Value == ScrollingDirection.Up;

        // These are the same paddings used by mania's DrawableHoldNote:
        // extend the full-size container under the tail and begin masking at the head centre.
        sizingContainer.Padding = new MarginPadding
        {
            Top = isUp ? 0 : -tailHeight,
            Bottom = isUp ? -tailHeight : 0,
        };
        maskingContainer.Padding = new MarginPadding
        {
            Top = isUp ? headHeight / 2 : 0,
            Bottom = isUp ? 0 : headHeight / 2,
        };

        Body.Y = (isUp ? 1 : -1) * headHeight / 2;
        // osu!mania does not force a minimum body height: when the head and tail overlap the
        // body naturally disappears, otherwise a forced 1px sliver renders badly in some skins.
        Body.Height = Math.Max(0, fullHeight - headHeight / 2 + tailHeight / 2);
        Body.Alpha = fullHeight > 0 ? 1 : 0;
        Tail.Alpha = fullHeight > 0 ? 1 : 0;
        Body.UpdateBody(Body.Height, tailAtTop: !isUp, isHolding: holding);

        if (Tail.Drawable is IO2LazerManiaHoldNoteVisualPiece tailPiece)
            tailPiece.SetHolding(holding);

        // Early hits retain the full body. Once chart time reaches the head, shrink from the
        // judgement line exactly like mania. A dropped hold intentionally leaves the last height
        // latched, so it continues scrolling without snapping back to its unheld chart position.
        if (pinActive && !dropped && fullHeight > 0)
        {
            wasPinned = true;
            sizingContainer.Height = Math.Clamp((fullHeight - Math.Max(0, consumedHeight)) / fullHeight, 0, 1);
        }
        else if (!wasPinned)
        {
            sizingContainer.Height = 1;
        }
    }

    internal void MarkDropped()
    {
        if (dropped)
            return;

        dropped = true;
        this.FadeColour(Color4.DarkGray, 60);
    }

    internal void ResetVisual()
    {
        ClearTransforms();
        Colour = Color4.White;
        Alpha = 1;
        sizingContainer.Height = 1;
        wasPinned = false;
        dropped = false;
        Body.ResetBody();
        Body.Alpha = 0;
        Tail.Alpha = 0;

        if (Tail.Drawable is IO2LazerManiaHoldNoteVisualPiece tailPiece)
        {
            tailPiece.SetHolding(false);
            tailPiece.Recycle();
        }
    }
}
