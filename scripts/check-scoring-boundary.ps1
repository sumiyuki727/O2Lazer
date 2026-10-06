$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$scoringRoot = Join-Path $repositoryRoot 'osu.Game.Rulesets.O2Lazer/Integration/Scoring'
$violations = @()
$sources = @(Get-ChildItem -LiteralPath $scoringRoot -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/](?:obj|bin)[\\/]' })
if ($sources.Count -eq 0) { throw 'No source files found for the boundary check.' }
foreach ($source in $sources) {
    $source = $source.FullName
    $code = [IO.File]::ReadAllText($source)
    $code = [regex]::Replace($code, '(?s)/\*.*?\*/|(?m)//[^\r\n]*', '')
    # Namespace compatibility keeps host and bridge types together; imports alone cannot guard this seam.
    if ($code -match '\b(?:O2JamScoreProcessor|ManiaScoreProcessorAdapter|O2JamGameplayProfile|O2JamModManiaScore)\b|\busing\s+osu\.Game\.Rulesets\.O2Lazer\.(?:Mods|UI)\b') {
        $violations += $source
    }
}
if ($violations.Count) { throw "Scoring integration depends on a concrete gameplay route: $($violations -join ', ')" }
Write-Output 'Scoring integration source boundary passed.'
