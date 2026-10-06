using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using O2Jam.Core;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Skinning;
using osu.Framework.Logging;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania.Scoring;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play.HUD.HitErrorMeters;

namespace osu.Game.Rulesets.O2Lazer.UI;

internal static class O2JamHitErrorMeterPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.HitErrorMeter";
    private static readonly object installLock = new();
    private static Func<HitErrorMeter, HitWindows> getWindows = null!;
    private static Action<HitErrorMeter, HitWindows> setWindows = null!;
    private static Func<CompositeDrawable, Drawable> getRoot = null!;
    private static Func<CompositeDrawable, IReadOnlyList<Drawable>> getChildren = null!;
    private static FieldInfo argonEarlyBars = null!;

    internal static bool IsInstalled { get; private set; }

    internal static bool InstallOnce()
    {
        lock (installLock)
        {
            if (IsInstalled)
                return true;

            try
            {
                getWindows = AccessTools.PropertyGetter(typeof(HitErrorMeter), "HitWindows").CreateDelegate<Func<HitErrorMeter, HitWindows>>();
                setWindows = AccessTools.PropertySetter(typeof(HitErrorMeter), "HitWindows").CreateDelegate<Action<HitErrorMeter, HitWindows>>();
                getRoot = AccessTools.PropertyGetter(typeof(CompositeDrawable), "InternalChild").CreateDelegate<Func<CompositeDrawable, Drawable>>();

                getChildren = AccessTools.PropertyGetter(typeof(CompositeDrawable), "InternalChildren").CreateDelegate<Func<CompositeDrawable, IReadOnlyList<Drawable>>>();
                argonEarlyBars = AccessTools.Field(typeof(BarHitErrorMeter), "colourBarsEarly");

                var harmony = new Harmony(harmony_id);
                harmony.Patch(AccessTools.Method(typeof(HitErrorMeter), "load"),
                    postfix: new HarmonyMethod(typeof(O2JamHitErrorMeterPatch), nameof(adaptDisplayWindows)));
                harmony.Patch(AccessTools.Method(typeof(LegacyBarHitErrorMeter), "load"),
                    postfix: new HarmonyMethod(typeof(O2JamHitErrorMeterPatch), nameof(adaptLegacyRegions)));
                harmony.Patch(AccessTools.Method(typeof(BarHitErrorMeter), "load"),
                    postfix: new HarmonyMethod(typeof(O2JamHitErrorMeterPatch), nameof(adaptArgonLength)));

                var transpiler = new HarmonyMethod(typeof(O2JamHitErrorMeterPatch), nameof(projectDisplayOffset));
                harmony.Patch(AccessTools.Method(typeof(LegacyBarHitErrorMeter), "OnNewJudgement"), transpiler: transpiler);
                harmony.Patch(AccessTools.Method(typeof(BarHitErrorMeter), "OnNewJudgement"), transpiler: transpiler);

                // Argon's pooled marker reads its offset in a captured callback; the moving
                // average reads it in the outer method. Both must use the same axis.
                var markerCallback = typeof(BarHitErrorMeter).GetNestedTypes(BindingFlags.NonPublic)
                    .SelectMany(type => AccessTools.GetDeclaredMethods(type))
                    .Single(method => method.Name.StartsWith("<OnNewJudgement>b__", StringComparison.Ordinal) && method.ReturnType == typeof(void));
                harmony.Patch(markerCallback, transpiler: transpiler);

                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                O2JamPatchRollback.Unpatch(harmony_id);
                Logger.Error(exception, "O2Lazer could not install its tick-based hit error display.");
                return false;
            }
        }
    }

    private static void adaptDisplayWindows(HitErrorMeter __instance)
    {
        // Only native positional meters need this visual projection. Object windows remain
        // in milliseconds for lifetimes, input selection, replay and native score statistics.
        var type = __instance.GetType();
        if ((type == typeof(LegacyBarHitErrorMeter) || type == typeof(BarHitErrorMeter)) && getWindows(__instance) is O2JamFrameworkHitWindows)
            setWindows(__instance, O2JamHitErrorDisplayWindows.Instance);
    }

    private static void adaptLegacyRegions(LegacyBarHitErrorMeter __instance)
    {
        if (__instance.GetType() != typeof(LegacyBarHitErrorMeter) || getWindows(__instance) is not O2JamHitErrorDisplayWindows windows)
            return;

        var colourBars = (Container)getChildren(__instance)[1];
        var regions = windows.GetAllAvailableWindows().Where(window => window.result.IsHit()).ToArray();
        // Keep zero at the native axis centre. The early-side spare tick is transparent;
        // using autosize on shifted colour boxes would otherwise move the axis itself.
        colourBars.AutoSizeAxes = Axes.None;
        colourBars.Width = (float)windows.WindowFor(HitResult.Meh) * LegacySkin.STABLE_MAGIC_SCALE_FACTOR;
        for (var i = 0; i < regions.Length; i++)
        {
            var early = windows.EarlyWindowFor(regions[i].result);
            var late = windows.WindowFor(regions[i].result);
            colourBars.Children[i].Width = (float)((early + late) / 2) * LegacySkin.STABLE_MAGIC_SCALE_FACTOR;
            colourBars.Children[i].X = (float)((late - early) / 4) * LegacySkin.STABLE_MAGIC_SCALE_FACTOR;
        }
    }

    private static void adaptArgonLength(BarHitErrorMeter __instance)
    {
        if (__instance.GetType() != typeof(BarHitErrorMeter) || getWindows(__instance) is not O2JamHitErrorDisplayWindows windows)
            return;

        // Native root scaling preserves the early COOL half-width. Each native colour
        // drawable and its gradient/animation is reused; only early extents differ.
        getRoot(__instance).Height *= (float)O2JamHitErrorDisplayWindows.NativeBarLengthMultiplier;
        var earlyBars = (Container)argonEarlyBars.GetValue(__instance)!;
        var regions = windows.GetAllAvailableWindows().Where(window => window.result.IsHit()).ToArray();
        var maximum = windows.WindowFor(HitResult.Meh);
        for (var i = 0; i < regions.Length; i++)
            earlyBars.Children[i].Height = (float)(windows.EarlyWindowFor(regions[i].result) / maximum);
    }

    private static double displayOffset(JudgementResult result, HitErrorMeter meter) =>
        getWindows(meter) is O2JamHitErrorDisplayWindows && result.HitObject is IO2JamJudgedObject
            ? O2JamHitErrorProjection.OffsetTicks(result) * O2JamHitErrorDisplayWindows.UnitsPerTick
            : result.TimeOffset;

    private static IEnumerable<CodeInstruction> projectDisplayOffset(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var offsetGetter = AccessTools.PropertyGetter(typeof(JudgementResult), nameof(JudgementResult.TimeOffset));
        var replacement = AccessTools.Method(typeof(O2JamHitErrorMeterPatch), nameof(displayOffset));
        var ownerType = __originalMethod.DeclaringType!;
        var ownerField = typeof(HitErrorMeter).IsAssignableFrom(ownerType) ? null
            : ownerType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(field => typeof(HitErrorMeter).IsAssignableFrom(field.FieldType));
        var result = new List<CodeInstruction>();
        var replacements = 0;

        foreach (var instruction in instructions)
        {
            if (!instruction.Calls(offsetGetter))
            {
                result.Add(instruction);
                continue;
            }

            result.Add(new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction));
            if (ownerField != null)
                result.Add(new CodeInstruction(OpCodes.Ldfld, ownerField));
            result.Add(new CodeInstruction(OpCodes.Call, replacement));
            replacements++;
        }

        // Fail back to complete native drawing if an upstream revision changes this seam.
        if (replacements != 1)
            throw new InvalidOperationException($"Expected one native timing read in {__originalMethod}, found {replacements}.");

        return result;
    }
}

/// <summary>
/// Visual coordinates only; never assigned to a playable hit object.
/// </summary>
internal sealed class O2JamHitErrorDisplayWindows : HitWindows
{
    internal static readonly double UnitsPerTick = maniaOd7Window(HitResult.Great) / O2JamJudgementEngine.CoolTicks;
    internal static readonly double NativeBarLengthMultiplier = UnitsPerTick * O2JamJudgementEngine.WindowFor(O2JamAccuracy.Bad, O2JamEndpointKind.Tap).LateTicks / maniaOd7Window(HitResult.Meh);
    internal static readonly O2JamHitErrorDisplayWindows Instance = new();

    public override bool IsHitResultAllowed(HitResult result) => result is HitResult.Perfect or HitResult.Good or HitResult.Meh or HitResult.Miss;

    public override void SetDifficulty(double difficulty)
    {
    }

    public override double WindowFor(HitResult result) => UnitsPerTick * tickWindowFor(result).LateTicks;

    internal double EarlyWindowFor(HitResult result) => UnitsPerTick * tickWindowFor(result).EarlyTicks;

    private static O2JamJudgementWindow tickWindowFor(HitResult result) => result switch
    {
        HitResult.Perfect => O2JamJudgementEngine.WindowFor(O2JamAccuracy.Cool, O2JamEndpointKind.Tap),
        HitResult.Good => O2JamJudgementEngine.WindowFor(O2JamAccuracy.Good, O2JamEndpointKind.Tap),
        HitResult.Meh or HitResult.Miss => O2JamJudgementEngine.WindowFor(O2JamAccuracy.Bad, O2JamEndpointKind.Tap),
        _ => default,
    };

    private static double maniaOd7Window(HitResult result)
    {
        // Six early ticks span half the native OD7 Great colour bar. Retain this scale
        // for the extra late tick instead of narrowing both sides to fit the old width.
        var windows = new ManiaHitWindows();
        windows.SetDifficulty(7);
        return windows.WindowFor(result);
    }
}
