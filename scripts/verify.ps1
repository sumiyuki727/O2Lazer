[CmdletBinding()]
param(
    [string]$OsuBinaryDirectory,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$DotNet = 'dotnet',
    [ValidateSet('All', 'Portable', 'Host')]
    [string]$Scope = 'All',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$resultsDirectory = Join-Path $repositoryRoot '.artifacts/test-results'
# New fixtures must not turn routine checks into benchmarks or private-library scans.
$routineFilter = 'TestCategory!=Benchmark&TestCategory!=LocalDiagnostics&TestCategory!=Isolated'
$binaryDirectory = $null
if ($Scope -ne 'Portable') {
    if ([string]::IsNullOrWhiteSpace($OsuBinaryDirectory)) {
        throw 'All and Host verification require OsuBinaryDirectory. Use -Scope Portable for independent checks.'
    }
    $binaryDirectory = (Resolve-Path -LiteralPath $OsuBinaryDirectory).Path
    foreach ($assemblyName in @('osu.Game.dll', 'osu.Game.Rulesets.Mania.dll', 'osu.Game.Resources.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $binaryDirectory $assemblyName) -PathType Leaf)) {
            throw "Missing $assemblyName in $binaryDirectory. See docs/development.md."
        }
    }
}

& (Join-Path $PSScriptRoot 'check-module-boundary.ps1')
& (Join-Path $PSScriptRoot 'check-storage-boundary.ps1')
& (Join-Path $PSScriptRoot 'check-scoring-boundary.ps1')
if ($Scope -ne 'Portable') {
    & (Join-Path $PSScriptRoot 'check-architecture.ps1') -OsuBinaryDirectory $binaryDirectory -Configuration $Configuration -DotNet $DotNet
}

Push-Location $repositoryRoot
try {
    if ($Scope -ne 'Host') {
        $portableGroups = @(
            @{ Project = 'O2Jam.Formats.Tests/O2Jam.Formats.Tests.csproj'; Filter = "FullyQualifiedName~.Normal.&$routineFilter"; Report = 'formats.trx' },
            @{ Project = 'O2Jam.Core.Tests/O2Jam.Core.Tests.csproj'; Filter = "FullyQualifiedName~.Normal.Core.&$routineFilter"; Report = 'core.trx' },
            @{ Project = 'scripts/Architecture.Tests/Architecture.Tests.csproj'; Filter = "FullyQualifiedName~.Normal.&$routineFilter"; Report = 'architecture.trx' }
        )
        foreach ($group in $portableGroups) {
            $arguments = @(
                'test', (Join-Path $repositoryRoot $group.Project), '-c', $Configuration,
                '--filter', $group.Filter,
                '--logger', "trx;LogFileName=$($group.Report)",
                '--results-directory', $resultsDirectory
            )
            if ($NoBuild) { $arguments += '--no-build' }
            & $DotNet @arguments
            if ($LASTEXITCODE -ne 0) { throw "$($group.Report) verification failed (exit code $LASTEXITCODE)." }
        }
    }

    if ($Scope -ne 'Portable') {
        $hostProject = Join-Path $repositoryRoot 'osu.Game.Rulesets.O2Lazer.Tests/osu.Game.Rulesets.O2Lazer.Tests.csproj'
        # Native Realm lifetime tests require separate test-host processes.
        $hostGroups = @(
            @{ Filter = "FullyQualifiedName~.Normal.&$routineFilter&FullyQualifiedName!~O2JamLegacyLibraryMigrationTest&FullyQualifiedName!~O2JamReplayImportTest"; Report = 'normal.trx'; ReuseBuild = $false },
            @{ Filter = "FullyQualifiedName~O2JamLegacyLibraryMigrationTest&$routineFilter"; Report = 'O2JamLegacyLibraryMigrationTest.trx'; ReuseBuild = $true },
            @{ Filter = "FullyQualifiedName~O2JamReplayImportTest&$routineFilter"; Report = 'O2JamReplayImportTest.trx'; ReuseBuild = $true }
            # This fixture is intentionally isolated from the routine Realm group.
            @{ Filter = 'FullyQualifiedName~O2JamLegacyLibraryMigrationTest.LegacySettingsRemainReadableWithoutRevivingManualEncoding&TestCategory!=Benchmark&TestCategory!=LocalDiagnostics'; Report = 'legacy-settings.trx'; ReuseBuild = $true }
        )
        foreach ($group in $hostGroups) {
            $arguments = @(
                'test', $hostProject, '-c', $Configuration,
                "-p:OsuBinaryDirectory=$binaryDirectory",
                '-p:O2JamSyncDiagnostics=false', '-p:O2LazerTestBridge=false',
                '--filter', $group.Filter,
                '--logger', "trx;LogFileName=$($group.Report)",
                '--results-directory', $resultsDirectory
            )
            if ($NoBuild -or $group.ReuseBuild) { $arguments += '--no-build' }
            & $DotNet @arguments
            if ($LASTEXITCODE -ne 0) { throw "$($group.Report) verification failed (exit code $LASTEXITCODE)." }
        }
    }
}
finally {
    Pop-Location
}
