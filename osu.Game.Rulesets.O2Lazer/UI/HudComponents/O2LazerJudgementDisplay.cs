using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Skinning.Configuration;
using osu.Game.Rulesets.O2Lazer.Skinning.Embedded;
using osu.Game.Rulesets.O2Lazer.UI.Gameplay;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.UI.HudComponents;

public sealed partial class O2LazerJudgementDisplay : O2LazerHudComponent
{
    private readonly Dictionary<HitResult, SkinnableDrawable> drawableCache = new();
    private readonly Container drawablePool;
    private readonly Container displayArea;

    [Cached(typeof(ISkinSource))]
    private readonly O2LazerEmbeddedSkinSource activeSkin = new();

    private IO2LazerGameplayEvents? gameplayEvents;

    [Resolved]
    private DrawableRuleset drawableRuleset { get; set; } = null!;

    [Resolved]
    private GameHost host { get; set; } = null!;

    [Resolved]
    private ISkinSource parentSkin { get; set; } = null!;

    public O2LazerJudgementDisplay()
    {
        AlwaysPresent = true;
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Anchor = Anchor.TopCentre;
        Origin = Anchor.Centre;

        InternalChildren =
        [
            displayArea = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Anchor = Anchor.TopCentre,
                Origin = Anchor.Centre,
            },
            drawablePool = new Container { Alpha = 0, RelativeSizeAxes = Axes.Both },
        ];
    }

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        if (gameplayEvents != null)
            gameplayEvents.JudgementDisplayed -= showJudgement;
        parentSkin.SourceChanged -= updateEmbeddedSkinFallback;
        activeSkin.DisposeEmbeddedSkins();

        base.Dispose(isDisposing);
    }

    #endregion

    protected override void LoadComplete()
    {
        base.LoadComplete();

        gameplayEvents = (drawableRuleset as O2LazerDrawableRuleset)?.GameplayEvents;

        if (gameplayEvents != null)
            gameplayEvents.JudgementDisplayed += showJudgement;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        parentSkin.SourceChanged += updateEmbeddedSkinFallback;
        updateEmbeddedSkinFallback();

        Y = activeSkin.GetConfig<O2LazerSkinConfigurationLookup, float>(
            new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ScorePosition)
        )?.Value ?? 300 * 1.6f;

        foreach (var result in O2LazerRuleset.STATIC_VALID_HIT_RESULTS)
        {
            var drawable = new SkinnableDrawable(
                new SkinComponentLookup<HitResult>(result))
            {
                RelativeSizeAxes = Axes.None,
                AutoSizeAxes = Axes.Both,
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
            };

            drawableCache[result] = drawable;
            drawablePool.Add(drawable);
        }
    }

    private void updateEmbeddedSkinFallback()
    {
        if (drawableRuleset is O2LazerDrawableRuleset { Beatmap: O2LazerBeatmap beatmap })
            activeSkin.SetSources(parentSkin, O2LazerEmbeddedSkinFallbackFactory.Create(parentSkin.AllSources, beatmap, host.Renderer));
    }

    private void showJudgement(HitResult result)
    {
        if (!drawableCache.TryGetValue(result, out var drawable))
            return;

        var evicted = displayArea.ToArray();
        displayArea.Clear(false);

        foreach (var child in evicted)
            drawablePool.Add(child);

        drawablePool.Remove(drawable, false);
        displayArea.Add(drawable);

        if (drawable.Drawable is IAnimatableJudgement animatable)
        {
            drawable.ResetAnimation();
            animatable.PlayAnimation();
        }
    }
}
