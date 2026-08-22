using System;
using System.Buffers;
using System.Collections.Generic;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Textures;
using osu.Game.Audio;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Configuration;
using osu.Game.Rulesets.O2Lazer.Skinning.Runtime;
using osu.Game.Rulesets.O2Lazer.UI.HudComponents;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Embedded;

public sealed class O2LazerEmbeddedSkinSource : ISkinSource, IDisposable, IO2LazerGameplaySkinDrawableSource
{
    public IEnumerable<ISkin> AllSources
    {
        get
        {
            if (parent != null)
            {
                foreach (var source in parent.AllSources)
                    yield return source;
            }

            if (embeddedFallbacks != null)
            {
                foreach (var source in embeddedFallbacks.AllSources)
                    yield return source;
            }
        }
    }

    private readonly object sourceChangedLock = new();

    private readonly List<SourceChangedSubscription> sourceChangedSubscriptions = [];
    private readonly Dictionary<Action, int> sourceChangedLastSubscription = [];

    private ISkinSource? parent;
    private O2LazerEmbeddedSkinFallbackChain? embeddedFallbacks;

    private int sourceChangedHead = -1;
    private int sourceChangedTail = -1;
    private int sourceChangedFreeHead = -1;
    private int sourceChangedCount;

    #region Disposal

    /// <inheritdoc/>
    public void Dispose() => DisposeEmbeddedSkins();

    #endregion

    /// <summary>
    /// Replaces the current skin sources with new ones, disposing any previously held
    /// embedded skins before taking ownership of the new ones.
    /// </summary>
    /// <param name="parent">The osu! skin source chain (must not be null).</param>
    /// <param name="embeddedFallbacks">The O2LAZER embedded fallback chain to consult after <paramref name="parent"/> misses.</param>
    public void SetSources(ISkinSource parent, O2LazerEmbeddedSkinFallbackChain? embeddedFallbacks)
    {
        DisposeEmbeddedSkins();

        this.parent = parent;
        this.embeddedFallbacks = embeddedFallbacks;
        raiseSourceChanged();
    }

    /// <inheritdoc />
    /// <summary>
    /// parent → fallbackChain(primary → fallback)
    /// when set to buildin skin, parent lookup will be null for all, auto fallback to fallback chain
    /// </summary>
    public Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
    {
        if (O2LazerDefaultHud.TryGetMainHudWithStage(lookup,
                () => parent?.GetDrawableComponent(lookup) ?? embeddedFallbacks?.GetDrawableComponent(lookup), out var mainHud))
            return mainHud;

        var drawable = lookup is
            O2LazerSkinComponentLookup or
            SkinComponentLookup<HitResult> or
            GlobalSkinnableContainerLookup { Lookup: GlobalSkinnableContainers.MainHUDComponents, Ruleset: not null }
            ? parent?.GetDrawableComponent(lookup)
              ?? (isO2JamNoteComponent(lookup) ? null : embeddedFallbacks?.GetDrawableComponent(lookup))
            : parent?.GetDrawableComponent(lookup);

        return drawable;
    }

    /// <summary>Looks up a texture, falling through parent → primary → fallback.</summary>
    public Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT) =>
        parent?.GetTexture(componentName, wrapModeS, wrapModeT)
        ?? embeddedFallbacks?.GetTexture(componentName, wrapModeS, wrapModeT);

    /// <summary>Looks up a sample, falling through parent → primary → fallback.</summary>
    public ISample? GetSample(ISampleInfo sampleInfo) =>
        parent?.GetSample(sampleInfo) ?? embeddedFallbacks?.GetSample(sampleInfo);

    /// <summary>
    /// Routes configuration lookups through the three-tier chain.
    /// </summary>
    /// <remarks>
    /// O2LazerSkinConfigurationLookup falls through parent → primary → fallback.
    /// All other lookups are forwarded to the parent only.
    /// </remarks>
    public IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
        where TLookup : notnull
        where TValue : notnull
        => lookup is O2LazerSkinConfigurationLookup o2lazerLookup
            ? parent?.GetConfig<TLookup, TValue>(lookup)
              ?? (isO2JamNoteComponent(o2lazerLookup.ComponentLookup) ? null : embeddedFallbacks?.GetConfig<TLookup, TValue>(lookup))
            : parent?.GetConfig<TLookup, TValue>(lookup);

    /// <inheritdoc/>
    public ISkin? FindProvider(Func<ISkin, bool> lookupFunction)
    {
        if (parent?.FindProvider(lookupFunction) is { } provider)
            return provider;

        return embeddedFallbacks?.FindProvider(lookupFunction);
    }

    /// <summary>
    /// Disposes and nulls the currently held embedded skin transformers
    /// without affecting the parent source.
    /// </summary>
    /// <remarks>
    /// Each transformer's underlying O2LazerEmbeddedSkin is disposed
    /// (via IDisposable) to release DLL store and texture/sample resources.
    /// </remarks>
    public void DisposeEmbeddedSkins()
    {
        embeddedFallbacks?.Dispose();
        embeddedFallbacks = null;
    }

    O2LazerResolvedDrawableFactory? IO2LazerGameplaySkinDrawableSource.GetDrawableFactory(O2LazerSkinComponentLookup lookup)
    {
        if (parent != null)
        {
            foreach (var source in parent.AllSources)
            {
                if (source is IO2LazerGameplaySkinDrawableSource factorySource
                    && factorySource.GetDrawableFactory(lookup) is { } factory)
                {
                    return factory;
                }
            }

            var embeddedFactory = isO2JamNoteComponent(lookup) ? null : embeddedFallbacks?.GetDrawableFactory(lookup);

            return new O2LazerResolvedDrawableFactory(() =>
                parent.GetDrawableComponent(lookup)
                ?? embeddedFactory?.Create());
        }

        return isO2JamNoteComponent(lookup) ? null : embeddedFallbacks?.GetDrawableFactory(lookup);
    }

    private static bool isO2JamNoteComponent(ISkinComponentLookup? lookup)
        => lookup is O2LazerSkinComponentLookup
        {
            LayoutVariant: O2LazerLayoutVariant.O2Jam7K,
            Component: O2LazerSkinComponents.Note
                or O2LazerSkinComponents.HoldNoteHead
                or O2LazerSkinComponents.HoldNoteTail
                or O2LazerSkinComponents.HoldNoteBody,
        };

    private void addSourceChangedHandler(Action handler)
    {
        var previousSame = sourceChangedLastSubscription.GetValueOrDefault(handler, -1);

        int slot;

        if (sourceChangedFreeHead >= 0)
        {
            slot = sourceChangedFreeHead;
            sourceChangedFreeHead = sourceChangedSubscriptions[slot].NextFree;

            sourceChangedSubscriptions[slot] = new SourceChangedSubscription
            {
                Handler = handler,
                PreviousSame = previousSame,
                PreviousActive = sourceChangedTail,
                NextActive = -1,
                NextFree = -1,
            };
        }
        else
        {
            slot = sourceChangedSubscriptions.Count;

            sourceChangedSubscriptions.Add(new SourceChangedSubscription
            {
                Handler = handler,
                PreviousSame = previousSame,
                PreviousActive = sourceChangedTail,
                NextActive = -1,
                NextFree = -1,
            });
        }

        if (sourceChangedTail >= 0)
        {
            var tail = sourceChangedSubscriptions[sourceChangedTail];
            tail.NextActive = slot;
            sourceChangedSubscriptions[sourceChangedTail] = tail;
        }
        else
        {
            sourceChangedHead = slot;
        }

        sourceChangedTail = slot;
        sourceChangedLastSubscription[handler] = slot;
        sourceChangedCount++;
    }

    private void removeSourceChangedHandler(Action handler)
    {
        if (!sourceChangedLastSubscription.TryGetValue(handler, out var slot))
            return;

        var subscription = sourceChangedSubscriptions[slot];

        if (subscription.PreviousSame >= 0)
            sourceChangedLastSubscription[handler] = subscription.PreviousSame;
        else
            sourceChangedLastSubscription.Remove(handler);

        if (subscription.PreviousActive >= 0)
        {
            var previous = sourceChangedSubscriptions[subscription.PreviousActive];
            previous.NextActive = subscription.NextActive;
            sourceChangedSubscriptions[subscription.PreviousActive] = previous;
        }
        else
        {
            sourceChangedHead = subscription.NextActive;
        }

        if (subscription.NextActive >= 0)
        {
            var next = sourceChangedSubscriptions[subscription.NextActive];
            next.PreviousActive = subscription.PreviousActive;
            sourceChangedSubscriptions[subscription.NextActive] = next;
        }
        else
        {
            sourceChangedTail = subscription.PreviousActive;
        }

        sourceChangedSubscriptions[slot] = new SourceChangedSubscription
        {
            Handler = null,
            PreviousSame = -1,
            PreviousActive = -1,
            NextActive = -1,
            NextFree = sourceChangedFreeHead,
        };

        sourceChangedFreeHead = slot;
        sourceChangedCount--;
    }

    private void raiseSourceChanged()
    {
        Action[]? handlers;
        var count = 0;

        lock (sourceChangedLock)
        {
            if (sourceChangedCount == 0)
                return;

            handlers = ArrayPool<Action>.Shared.Rent(sourceChangedCount);

            for (var slot = sourceChangedHead; slot >= 0; slot = sourceChangedSubscriptions[slot].NextActive)
            {
                var handler = sourceChangedSubscriptions[slot].Handler;

                if (handler != null)
                    handlers[count++] = handler;
            }
        }

        try
        {
            for (var i = 0; i < count; i++)
                handlers[i]();
        }
        finally
        {
            Array.Clear(handlers, 0, count);
            ArrayPool<Action>.Shared.Return(handlers);
        }
    }

    /// <summary>
    /// Fired when the skin source changes.  Uses a custom slot-based backing store with
    /// O(1) subscribe / unsubscribe instead of a plain multicast delegate, which would
    /// degrade to O(n²) copy churn when many <see cref="SkinReloadableDrawable"/> instances
    /// subscribe and then dispose (each <c>-=</c> copies the invocation list).
    /// </summary>
    public event Action? SourceChanged
    {
        add
        {
            if (value == null)
                return;

            lock (sourceChangedLock)
                addSourceChangedHandler(value);
        }

        remove
        {
            if (value == null)
                return;

            lock (sourceChangedLock)
                removeSourceChangedHandler(value);
        }
    }

    private struct SourceChangedSubscription
    {
        public Action? Handler;

        // Previous active subscription with the same delegate.
        // This lets -= remove the latest matching handler, like normal C# events.
        public int PreviousSame;

        // Active linked list.
        public int PreviousActive;
        public int NextActive;

        // Free-list slot reuse.
        public int NextFree;
    }
}
