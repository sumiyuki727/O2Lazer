using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.Result;

public sealed partial class O2LazerTimelineStatistic : CompositeDrawable
{
    private const int bucket_count = 300;
    private const float subplot_height = 96;

    private static readonly OsuColour colours = new();

    private static readonly Color4 note_colour = colours.Blue;
    private static readonly Color4 ln_colour = colours.GreenLight;
    private static readonly Color4 fast_colour = new(90, 175, 255, 255);
    private static readonly Color4 slow_colour = new(255, 130, 92, 255);
    private static readonly Color4 failed_colour = new(70, 70, 70, 255);

    private readonly TimelineData data;

    public O2LazerTimelineStatistic(ScoreInfo score, IBeatmap playableBeatmap)
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

        data = CreateData(score, playableBeatmap);
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChild = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 8),
            Children =
            [
                createSubplot(O2LazerStrings.Notes, data.Notes, null),
                createSubplot(O2LazerStrings.Judgement, data.Judgements, data.FailureFraction),
                createSubplot(O2LazerStrings.FastSlow, data.FastSlow, data.FailureFraction),
            ],
        };
    }

    internal static TimelineData CreateData(ScoreInfo score, IBeatmap playableBeatmap)
    {
        var beatmapMax = playableBeatmap.HitObjects.Count == 0 ? 0 : playableBeatmap.HitObjects.Max(h => h.StartTime);
        var timingHitEvents = score.HitEvents.ToArray();
        var scoringHitEvents = O2LazerJudgementEventStore.TryGet(score, out var judgementEvents)
            ? O2LazerJudgementEventProjection.CreateScoringHitEvents(judgementEvents).ToArray()
            : timingHitEvents;
        var hitEventsMax = timingHitEvents.Length == 0 ? 0 : timingHitEvents.Max(e => e.HitObject.GetEndTime());
        var duration = Math.Max(1, Math.Max(beatmapMax, hitEventsMax));

        var notes = createNotesSubplot(playableBeatmap, duration);
        var judgements = createJudgementSubplot(scoringHitEvents, duration);
        var fastSlow = createFastSlowSubplot(timingHitEvents, duration);
        var failure = score.Passed ? null : findFailureFraction(score, playableBeatmap, scoringHitEvents, duration);

        return new TimelineData(notes, judgements, fastSlow, failure);
    }

    private static SubplotData createNotesSubplot(IBeatmap playableBeatmap, double duration)
    {
        var note = new int[bucket_count];
        var ln = new int[bucket_count];

        foreach (var h in playableBeatmap.HitObjects.OfType<O2LazerHitObject>())
        {
            var b = bucketFor(h.StartTime, duration);

            switch (classifyNote(h))
            {
                case NoteKind.Note: note[b]++; break;

                case NoteKind.LongNote: ln[b]++; break;
            }
        }

        return new SubplotData([
            new CategoryData("ln", ln_colour, ln),
            new CategoryData("note", note_colour, note),
        ]);
    }

    private static SubplotData createJudgementSubplot(IReadOnlyList<HitEvent> hitEvents, double duration)
    {
        var cool = new int[bucket_count];
        var good = new int[bucket_count];
        var bad = new int[bucket_count];
        var miss = new int[bucket_count];

        foreach (var e in hitEvents.Where(isO2JamTimelineHit))
        {
            var b = bucketFor(e.HitObject.GetEndTime(), duration);

            switch (e.Result)
            {
                case HitResult.Perfect: cool[b]++; break;

                case HitResult.Good: good[b]++; break;

                case HitResult.Ok: bad[b]++; break;

                case HitResult.Meh or HitResult.Miss: miss[b]++; break;
            }
        }

        return new SubplotData([
            new CategoryData("MISS", O2LazerHitResultColours.ForHitResult(HitResult.Miss), miss),
            new CategoryData("BAD", O2LazerHitResultColours.ForHitResult(HitResult.Ok), bad),
            new CategoryData("GOOD", O2LazerHitResultColours.ForHitResult(HitResult.Good), good),
            new CategoryData("COOL", O2LazerHitResultColours.ForHitResult(HitResult.Perfect), cool),
        ]);
    }

    private static SubplotData createFastSlowSubplot(IReadOnlyList<HitEvent> hitEvents, double duration)
    {
        var fast = new int[bucket_count];
        var slow = new int[bucket_count];

        foreach (var e in hitEvents.Where(isO2JamTimelineHit).Where(e => e.Result.IsHit()))
        {
            var b = bucketFor(e.HitObject.GetEndTime(), duration);

            if (e.TimeOffset < 0) fast[b]++;
            else if (e.TimeOffset > 0) slow[b]++;
        }

        return new SubplotData([
            new CategoryData("fast", fast_colour, fast),
            new CategoryData("slow", slow_colour, slow),
        ]);
    }

    // O2Jam does not expose a gauge history through the shared result model; the failure marker is omitted.
    private static double? findFailureFraction(ScoreInfo score, IBeatmap playableBeatmap, IReadOnlyList<HitEvent> scoringHitEvents, double duration)
        => null;

    private static bool isO2JamTimelineHit(HitEvent e) => e.Result switch
    {
        HitResult.Perfect or HitResult.Great or HitResult.Good or HitResult.Ok or HitResult.Meh => e.HitObject is O2LazerHitObject,
        HitResult.Miss => true,
        _ => false,
    };

    private static int bucketFor(double time, double duration) => Math.Clamp((int)Math.Floor(time / duration * bucket_count), 0, bucket_count - 1);

    private static NoteKind classifyNote(O2LazerHitObject h)
    {
        if (h is O2LazerLongNote) return NoteKind.LongNote;

        return NoteKind.Note;
    }

    private static Drawable createSubplot(LocalisableString title, SubplotData subplot, double? failureFraction) => new FillFlowContainer
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Direction = FillDirection.Vertical,
        Spacing = new Vector2(0, 2),
        Children =
        [
            createLegend(title, subplot),
            createPlot(subplot, failureFraction),
        ],
    };

    private static Drawable createLegend(LocalisableString title, SubplotData subplot)
    {
        var items = new List<Drawable>
        {
            new OsuSpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = title,
                Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold),
            },
        };

        foreach (var c in subplot.Categories)
        {
            items.Add(new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(4, 0),
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Children =
                [
                    new Circle
                    {
                        Size = new Vector2(8),
                        Colour = c.Colour,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                    },
                    new OsuSpriteText
                    {
                        Text = localiseCategory(c.Label),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = OsuFont.GetFont(size: 11),
                    },
                ],
            });
        }

        return new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(10, 0),
            Children = items,
        };
    }

    private static LocalisableString localiseCategory(string category) => category switch
    {
        "note" => O2LazerStrings.Note,
        "ln" => O2LazerStrings.LongNote,
        "MISS" => O2LazerStrings.Miss,
        "BAD" => O2LazerStrings.Bad,
        "GOOD" => O2LazerStrings.Good,
        "COOL" => O2LazerStrings.Cool,
        "fast" => O2LazerStrings.Fast,
        "slow" => O2LazerStrings.Slow,
        _ => category,
    };

    private static Drawable createPlot(SubplotData subplot, double? failureFraction)
    {
        var maxTotal = Math.Max(1, Enumerable.Range(0, bucket_count)
            .Select(b => subplot.Categories.Sum(c => c.Buckets[b]))
            .DefaultIfEmpty(0)
            .Max());

        var children = new List<Drawable>
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.18f },
            new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                ColumnDimensions = Enumerable.Range(0, bucket_count).Select(_ => new Dimension()).ToArray(),
                Content = new[] { Enumerable.Range(0, bucket_count).Select(b => createBar(subplot, b, maxTotal)).ToArray() },
            },
        };

        if (failureFraction is { } frac && frac < 1)
        {
            children.Add(new Box
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Width = (float)(1 - frac),
                Height = 1,
                Colour = failed_colour,
                Alpha = 0.6f,
            });
        }

        return new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = subplot_height,
            Children = children,
        };
    }

    private static Drawable createBar(SubplotData subplot, int bucket, int maxTotal)
    {
        var bar = new Container { RelativeSizeAxes = Axes.Both };
        var total = subplot.Categories.Sum(c => c.Buckets[bucket]);

        if (total == 0) return bar;

        float cumulative = 0;

        // Categories are ordered bottom-to-top; first iterated sits at the bottom.
        foreach (var cat in subplot.Categories)
        {
            var count = cat.Buckets[bucket];
            if (count == 0) continue;

            var height = (float)count / maxTotal;
            bar.Add(new Box
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
                RelativePositionAxes = Axes.Y,
                Y = -cumulative,
                Height = height,
                Colour = cat.Colour,
                Alpha = 0.86f,
            });
            cumulative += height;
        }

        return bar;
    }

    private enum NoteKind
    {
        Note,
        LongNote,
    }

    internal sealed record TimelineData(SubplotData Notes, SubplotData Judgements, SubplotData FastSlow, double? FailureFraction);

    internal sealed record SubplotData(IReadOnlyList<CategoryData> Categories);

    internal sealed record CategoryData(string Label, Color4 Colour, int[] Buckets);
}
