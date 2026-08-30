[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OsuBinaryDirectory,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$DotNet = 'dotnet',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$binaryDirectory = (Resolve-Path -LiteralPath $OsuBinaryDirectory).Path
foreach ($assemblyName in @('osu.Game.dll', 'osu.Game.Rulesets.Mania.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $binaryDirectory $assemblyName) -PathType Leaf)) {
        throw "Missing $assemblyName in $binaryDirectory. See docs/development.md."
    }
}

# Corpus scans and diagnostic tests need private data or process isolation, not a routine check.
$testArguments = @(
    'test',
    (Join-Path $repositoryRoot 'osu.Game.Rulesets.O2Lazer.Tests/osu.Game.Rulesets.O2Lazer.Tests.csproj'),
    '-c', $Configuration,
    "-p:OsuBinaryDirectory=$binaryDirectory",
    '-p:O2JamSyncDiagnostics=false',
    '--filter', 'FullyQualifiedName~.Normal.&TestCategory!=LocalDiagnostics&TestCategory!=Isolated',
    '--logger', 'trx;LogFileName=normal.trx',
    '--results-directory', (Join-Path $repositoryRoot '.artifacts/test-results')
)
if ($NoBuild) {
    $testArguments += '--no-build'
}

Push-Location $repositoryRoot
try {
    & $DotNet @testArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Verification failed (exit code $LASTEXITCODE)."
    }
}
finally {
    Pop-Location
}
