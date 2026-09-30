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
& (Join-Path $PSScriptRoot 'check-storage-boundary.ps1')
& (Join-Path $PSScriptRoot 'check-scoring-boundary.ps1')
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
    '--filter', 'FullyQualifiedName~.Normal.&TestCategory!=LocalDiagnostics&TestCategory!=Isolated&FullyQualifiedName!~O2JamLegacyLibraryMigrationTest&FullyQualifiedName!~O2JamReplayImportTest',
    '--logger', 'trx;LogFileName=normal.trx',
    '--results-directory', (Join-Path $repositoryRoot '.artifacts/test-results')
)
if ($NoBuild) {
    $testArguments += '--no-build'
}

Push-Location $repositoryRoot
try {
    $formatArguments = @(
        'test',
        (Join-Path $repositoryRoot 'O2Jam.Formats.Tests/O2Jam.Formats.Tests.csproj'),
        '-c', $Configuration,
        '--filter', 'FullyQualifiedName~.Normal.',
        '--logger', 'trx;LogFileName=formats.trx',
        '--results-directory', (Join-Path $repositoryRoot '.artifacts/test-results')
    )
    if ($NoBuild) {
        $formatArguments += '--no-build'
    }
    & $DotNet @formatArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Format verification failed (exit code $LASTEXITCODE)."
    }

    $coreArguments = @(
        'test', (Join-Path $repositoryRoot 'O2Jam.Core.Tests/O2Jam.Core.Tests.csproj'),
        '-c', $Configuration, '--filter', 'FullyQualifiedName~.Normal.Core.',
        '--logger', 'trx;LogFileName=core.trx',
        '--results-directory', (Join-Path $repositoryRoot '.artifacts/test-results')
    )
    if ($NoBuild) { $coreArguments += '--no-build' }
    & $DotNet @coreArguments
    if ($LASTEXITCODE -ne 0) { throw "Core verification failed (exit code $LASTEXITCODE)." }

    & $DotNet @testArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Verification failed (exit code $LASTEXITCODE)."
    }
    # Native Realm lifetime tests require separate test-host processes.
    foreach ($fixture in @('O2JamLegacyLibraryMigrationTest', 'O2JamReplayImportTest')) {
        & $DotNet test (Join-Path $repositoryRoot 'osu.Game.Rulesets.O2Lazer.Tests/osu.Game.Rulesets.O2Lazer.Tests.csproj') `
            -c $Configuration --no-build "-p:OsuBinaryDirectory=$binaryDirectory" `
            --filter "FullyQualifiedName~$fixture&TestCategory!=Isolated&TestCategory!=LocalDiagnostics" `
            --logger "trx;LogFileName=$fixture.trx" `
            --results-directory (Join-Path $repositoryRoot '.artifacts/test-results')
        if ($LASTEXITCODE -ne 0) { throw "$fixture failed (exit code $LASTEXITCODE)." }
    }
}
finally {
    Pop-Location
}
