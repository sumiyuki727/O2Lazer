using System;
using System.Linq;

namespace osu.Game.Rulesets.O2Lazer;

public static class Constant
{
    public static readonly string[] O2LAZER_EXTENSIONS = [".ojn"];

    public const string AUTHOR = "O2Jam";
    public const string SHORT_NAME = "o2lazer";

    public static bool IsChartFile(string filename)
        => O2LAZER_EXTENSIONS.Any(e => filename.EndsWith(e, StringComparison.OrdinalIgnoreCase));
}
