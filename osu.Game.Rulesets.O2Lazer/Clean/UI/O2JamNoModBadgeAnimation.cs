using osu.Framework.Graphics;

namespace osu.Game.Rulesets.O2Lazer.UI;

internal sealed class O2JamNoModBadgeAnimation
{
    internal enum Destination
    {
        LeftUpper,
        NativeNoMods,
        NativeUnranked,
        NativeRanked,
    }

    internal const double Duration = 240;
    internal const Easing Easing = osu.Framework.Graphics.Easing.OutQuint;
    private readonly Drawable badge;
    private readonly float leftX;
    private Destination applied;
    private Destination? pending;
    private float nativeX;
    private float badgeWidth;
    private bool customActive;
    private int generation;

    public bool OwnsBadge { get; private set; }
    public bool UpdateButtonWidth { get; private set; }
    public Destination AppliedDestination => applied;

    public O2JamNoModBadgeAnimation(Drawable badge, Destination initial)
    {
        this.badge = badge;
        // Keep the native margin intact. Moving X by the same amount produces the old left
        // position without leaving a second horizontal transform for the native code to undo.
        leftX = -badge.Margin.Left;
        nativeX = badge.X;
        badgeWidth = badge.DrawWidth;
        applied = initial;
        updateNativeX(initial);
        if (initial == Destination.LeftUpper)
        {
            OwnsBadge = customActive = true;
            badge.X = leftX;
            badge.Y = -5;
            badge.Alpha = 1;
        }
    }

    public void Request(Destination target)
    {
        if (OwnsBadge || target == Destination.LeftUpper)
        {
            OwnsBadge = true;
            pending = target;
        }
        else
        {
            updateNativeX(target);
            applied = target;
        }
    }

    // True means a transient request to enter No Mod was cancelled before it was displayed;
    // the caller must let the native method apply the final selection's badge transforms.
    public bool Flush()
    {
        UpdateButtonWidth = false;
        if (pending is not { } target)
            return false;
        pending = null;
        updateNativeX(target);
        var widthChanged = badgeWidth != badge.DrawWidth;
        badgeWidth = badge.DrawWidth;

        if (!customActive && target != Destination.LeftUpper)
        {
            OwnsBadge = false;
            applied = target;
            return true;
        }
        if (customActive && applied == target)
        {
            // Localisation can resize the badge during a slide. Refresh only the width,
            // without restarting the badge's position or visibility transforms.
            UpdateButtonWidth = widthChanged && target == Destination.NativeUnranked;
            return false;
        }

        customActive = true;
        UpdateButtonWidth = true;
        applied = target;
        var currentGeneration = ++generation;

        if (target == Destination.LeftUpper)
        {
            if (badge.Alpha == 0)
            {
                // Order matters even while hidden: leave the mod bar vertically before
                // moving to the left column, then start the visible upward movement.
                badge.MoveToY(20);
                badge.MoveToX(leftX);
            }

            badge.MoveToX(leftX, Duration, Easing);
            badge.MoveToY(-5, Duration, Easing);
            badge.FadeIn(Duration, Easing);
        }
        else if (target == Destination.NativeUnranked)
        {
            badge.MoveToX(0, Duration, Easing);
            badge.MoveToY(-5, Duration, Easing);
            badge.FadeIn(Duration, Easing).OnComplete(_ => release(currentGeneration));
        }
        else
        {
            // An interrupted horizontal slide must not keep moving sideways during the
            // downward fade. The other properties use native replacement semantics.
            badge.MoveToX(badge.X);
            if (badge.Alpha == 0)
                finishHiddenExit(currentGeneration, target);
            else
            {
                badge.MoveToY(20, Duration, Easing);
                badge.FadeOut(Duration, Easing).OnComplete(_ => finishHiddenExit(currentGeneration, target));
            }
        }
        return false;
    }

    private void updateNativeX(Destination target)
    {
        if (target == Destination.NativeUnranked)
            nativeX = 0;
        else if (target == Destination.NativeRanked)
            nativeX = -badge.DrawWidth;
        // Native No Mod changes only Y and alpha, retaining its previous horizontal target.
    }

    private void finishHiddenExit(int currentGeneration, Destination target)
    {
        if (generation != currentGeneration)
            return;

        badge.MoveToY(20);
        badge.MoveToX(nativeX);
        if (target == Destination.NativeRanked)
            badge.MoveToY(-5);
        release(currentGeneration);
    }

    private void release(int currentGeneration)
    {
        if (generation == currentGeneration)
            OwnsBadge = customActive = false;
    }
}
