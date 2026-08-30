using System;

namespace osu.Game.Rulesets.O2Lazer.Core;

public readonly record struct O2JamGameplaySnapshot(
    long Score,
    int Combo,
    int MaximumCombo,
    int JamProgress,
    int JamCombo,
    int MaximumJamCombo,
    int ConsecutiveCoolProgress,
    int Pills,
    int Life,
    bool ScoringEnabled,
    bool HasFailed);

public readonly record struct O2JamResolvedJudgement(
    O2JamAccuracy RequestedAccuracy,
    O2JamAccuracy ResolvedAccuracy,
    bool PillConsumed,
    long ScoreDelta,
    int LifeDelta,
    O2JamGameplaySnapshot State);

public interface IO2JamGameplayStateSource
{
    O2JamGameplaySnapshot Current { get; }

    event Action<O2JamGameplaySnapshot>? StateChanged;
}

/// <summary>
/// Authoritative O2Jam score/life/Jam state. Drawables, replay and HUD consume its resolved result
/// and snapshot; they must not independently convert pills or recompute score.
/// </summary>
public sealed class O2JamGameplayState : IO2JamGameplayStateSource
{
    public const int MaximumLife = 1000;
    public const int MaximumJamProgress = 100;
    public const int MaximumPills = 5;
    public const int CoolHitsPerPill = 15;

    private readonly O2JamDifficulty difficulty;

    private long score;
    private int combo = -1;
    private int maximumCombo;
    private int jamProgress;
    private int jamCombo;
    private int maximumJamCombo;
    private int consecutiveCoolProgress;
    private int pills;
    private int life = MaximumLife;
    private bool scoringEnabled = true;
    private bool hasFailed;

    public O2JamGameplayState(O2JamDifficulty difficulty)
    {
        this.difficulty = difficulty;
    }

    public O2JamGameplaySnapshot Current => new(
        score,
        combo,
        maximumCombo,
        jamProgress,
        jamCombo,
        maximumJamCombo,
        consecutiveCoolProgress,
        pills,
        life,
        scoringEnabled,
        hasFailed);

    public event Action<O2JamGameplaySnapshot>? StateChanged;

    public static int LifeDeltaFor(O2JamDifficulty difficulty, O2JamAccuracy accuracy) => (difficulty, accuracy) switch
    {
        (O2JamDifficulty.EX, O2JamAccuracy.Cool) => 3,
        (O2JamDifficulty.EX, O2JamAccuracy.Good) => 2,
        (O2JamDifficulty.EX, O2JamAccuracy.Bad) => -10,
        (O2JamDifficulty.EX, O2JamAccuracy.Miss) => -50,

        (O2JamDifficulty.NX, O2JamAccuracy.Cool) => 2,
        (O2JamDifficulty.NX, O2JamAccuracy.Good) => 1,
        (O2JamDifficulty.NX, O2JamAccuracy.Bad) => -7,
        (O2JamDifficulty.NX, O2JamAccuracy.Miss) => -40,

        (O2JamDifficulty.HX, O2JamAccuracy.Cool) => 1,
        (O2JamDifficulty.HX, O2JamAccuracy.Good) => 0,
        (O2JamDifficulty.HX, O2JamAccuracy.Bad) => -5,
        (O2JamDifficulty.HX, O2JamAccuracy.Miss) => -30,
        _ => 0,
    };

    public O2JamResolvedJudgement Apply(O2JamAccuracy requestedAccuracy)
    {
        if (requestedAccuracy == O2JamAccuracy.None)
            return new O2JamResolvedJudgement(requestedAccuracy, requestedAccuracy, false, 0, 0, Current);

        if (!scoringEnabled)
            return applyAfterLifeDepleted(requestedAccuracy);

        var resolvedAccuracy = requestedAccuracy;
        var pillConsumed = false;

        if (requestedAccuracy == O2JamAccuracy.Bad && pills > 0)
        {
            pills--;
            consecutiveCoolProgress = 0;
            resolvedAccuracy = O2JamAccuracy.Cool;
            pillConsumed = true;
        }

        var scoreBefore = score;
        var lifeBefore = life;

        applyScore(resolvedAccuracy);
        applyComboAndJam(resolvedAccuracy);
        applyLife(resolvedAccuracy);

        if (life == 0)
        {
            scoringEnabled = false;
            hasFailed = difficulty != O2JamDifficulty.EX;
        }

        var snapshot = Current;
        StateChanged?.Invoke(snapshot);

        return new O2JamResolvedJudgement(
            requestedAccuracy,
            resolvedAccuracy,
            pillConsumed,
            score - scoreBefore,
            life - lifeBefore,
            snapshot);
    }

    public void Reset()
    {
        score = 0;
        combo = -1;
        maximumCombo = 0;
        jamProgress = 0;
        jamCombo = 0;
        maximumJamCombo = 0;
        consecutiveCoolProgress = 0;
        pills = 0;
        life = MaximumLife;
        scoringEnabled = true;
        hasFailed = false;
        StateChanged?.Invoke(Current);
    }

    private O2JamResolvedJudgement applyAfterLifeDepleted(O2JamAccuracy accuracy)
    {
        if (difficulty == O2JamDifficulty.EX)
        {
            if (accuracy is O2JamAccuracy.Cool or O2JamAccuracy.Good)
                combo++;
            else
                combo = -1;
        }

        var snapshot = Current;
        StateChanged?.Invoke(snapshot);
        return new O2JamResolvedJudgement(accuracy, accuracy, false, 0, 0, snapshot);
    }

    private void applyScore(O2JamAccuracy accuracy)
    {
        var delta = accuracy switch
        {
            O2JamAccuracy.Cool => 200 + 10 * jamCombo,
            O2JamAccuracy.Good => 100 + 5 * jamCombo,
            O2JamAccuracy.Bad => 4,
            O2JamAccuracy.Miss => -10,
            _ => 0,
        };

        score = Math.Max(0, score + delta);
    }

    private void applyComboAndJam(O2JamAccuracy accuracy)
    {
        switch (accuracy)
        {
            case O2JamAccuracy.Cool:
                combo++;
                consecutiveCoolProgress++;
                jamProgress += 4;
                break;

            case O2JamAccuracy.Good:
                combo++;
                consecutiveCoolProgress = 0;
                jamProgress += 2;
                break;

            case O2JamAccuracy.Bad:
            case O2JamAccuracy.Miss:
                combo = -1;
                consecutiveCoolProgress = 0;
                jamProgress = 0;
                jamCombo = 0;
                return;
        }

        maximumCombo = Math.Max(maximumCombo, combo);

        if (consecutiveCoolProgress >= CoolHitsPerPill)
        {
            pills = Math.Min(pills + 1, MaximumPills);
            consecutiveCoolProgress = 0;
        }

        if (jamProgress >= MaximumJamProgress)
        {
            jamProgress %= MaximumJamProgress;
            jamCombo++;
            maximumJamCombo = Math.Max(maximumJamCombo, jamCombo);
        }
    }

    private void applyLife(O2JamAccuracy accuracy)
    {
        life = Math.Clamp(life + LifeDeltaFor(difficulty, accuracy), 0, MaximumLife);
    }
}
