using System;
using System.IO;
using osu.Framework.Logging;
using osu.Game.Rulesets.O2Lazer;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

/// <summary>
/// Diagnostic listener used to verify O2Jam beat-window judgement and pill-rescued display.
/// Writes one structured line per scoring judgement into the o2lazer runtime log.
/// </summary>
internal static class O2LazerJudgementAuditLogger
{
    private static readonly string csv_path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "o2lazer-judgement-audit.csv");

    private static readonly object write_lock = new();

    public static void Record(
        O2LazerTimingObservationKind kind,
        int column,
        double expectedTime,
        double actualTime,
        HitResult rawResult,
        HitResult displayedResult,
        int pillBefore,
        int pillAfter,
        double bpm,
        double gameplayRate,
        int comboBefore,
        int comboAfter)
    {
        var offset = actualTime - expectedTime;
        O2LazerLogger.Log(
            $"JUDGEMENT_AUDIT\t{kind}\tcol={column}\texpected={expectedTime:F3}\tactual={actualTime:F3}\toffset={offset:F3}\traw={o2JamName(rawResult)}\tshown={o2JamName(displayedResult)}\tpill={pillBefore}->{pillAfter}\tbpm={bpm:F2}\trate={gameplayRate:F3}\tcombo={comboBefore}->{comboAfter}",
            LogLevel.Verbose);

        writeCsv(kind, column, expectedTime, actualTime, offset, rawResult, displayedResult, pillBefore, pillAfter, bpm, gameplayRate, comboBefore, comboAfter);
    }

    private static string o2JamName(HitResult result) => result switch
    {
        HitResult.Perfect => "COOL",
        HitResult.Good => "GOOD",
        HitResult.Ok => "BAD",
        _ => result.ToString(),
    };

    private static void writeCsv(
        O2LazerTimingObservationKind kind,
        int column,
        double expectedTime,
        double actualTime,
        double offset,
        HitResult rawResult,
        HitResult displayedResult,
        int pillBefore,
        int pillAfter,
        double bpm,
        double gameplayRate,
        int comboBefore,
        int comboAfter)
    {
        try
        {
            lock (write_lock)
            {
                if (!File.Exists(csv_path))
                    File.WriteAllText(csv_path, "kind,column,expected,actual,offset,raw,shown,pillBefore,pillAfter,bpm,rate,comboBefore,comboAfter\n");

                File.AppendAllText(
                    csv_path,
                    $"{kind},{column},{expectedTime:F3},{actualTime:F3},{offset:F3},{o2JamName(rawResult)},{o2JamName(displayedResult)},{pillBefore},{pillAfter},{bpm:F2},{gameplayRate:F3},{comboBefore},{comboAfter}\n");
            }
        }
        catch
        {
            // Diagnostics must never break gameplay.
        }
    }
}
