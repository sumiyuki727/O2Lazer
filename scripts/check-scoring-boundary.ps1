$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$scoringRoot = Join-Path $repositoryRoot 'osu.Game.Rulesets.O2Lazer/Integration/Scoring'
$violations = @()
foreach ($source in (& rg --files $scoringRoot -g '*.cs')) {
    $code = [IO.File]::ReadAllText($source)
    $code = [regex]::Replace($code, '(?s)/\*.*?\*/|(?m)//[^\r\n]*', '')
    # Namespace compatibility keeps host and bridge types together; imports alone cannot guard this seam.
    if ($code -match '\b(?:O2JamScoreProcessor|ManiaScoreProcessorAdapter|O2JamGameplayProfile|O2JamModManiaScore)\b|\busing\s+osu\.Game\.Rulesets\.O2Lazer\.(?:Mods|UI)\b') {
        $violations += $source
    }
}
if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate scoring sources with rg.' }
if ($violations.Count) { throw "Scoring integration depends on a concrete gameplay route: $($violations -join ', ')" }
Write-Output 'Scoring integration source boundary passed.'
