[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OsuBinaryDirectory,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$DotNet = 'dotnet',
    [switch]$ReportOnly
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$binaryDirectory = (Resolve-Path -LiteralPath $OsuBinaryDirectory).Path
$reportRoot = Join-Path $repositoryRoot '.artifacts/architecture'
New-Item -Path $reportRoot -ItemType Directory -Force | Out-Null
$hostProject = Join-Path $repositoryRoot 'osu.Game.Rulesets.O2Lazer/osu.Game.Rulesets.O2Lazer.csproj'
$toolProject = Join-Path $PSScriptRoot 'Architecture/Architecture.csproj'

# Resolve the real MSBuild item lists, not directory guesses or stale installed ruleset metadata.
& $DotNet restore $hostProject "-p:OsuBinaryDirectory=$binaryDirectory" --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Architecture dependency restore failed.' }
& $DotNet build $toolProject -c $Configuration --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Architecture tool build failed.' }
$sourceInputs = @()
$references = @()
foreach ($projectName in @('osu.Game.Rulesets.O2Lazer', 'O2Jam.Core', 'O2Jam.Formats')) {
    $project = Join-Path $repositoryRoot "$projectName/$projectName.csproj"
    $arguments = @('msbuild', $project, '-getItem:Compile,ReferencePath', '-getProperty:DefineConstants,OsuVersion',
        "-p:Configuration=$Configuration", '-p:O2JamSyncDiagnostics=false', "-p:OsuBinaryDirectory=$binaryDirectory", '-verbosity:quiet')
    if ($projectName -eq 'osu.Game.Rulesets.O2Lazer') { $arguments += '-target:ResolveReferences' }
    $output = & $DotNet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect $projectName." }
    $items = ($output -join [Environment]::NewLine) | ConvertFrom-Json
    if ($projectName -eq 'osu.Game.Rulesets.O2Lazer') {
        foreach ($assemblyName in @('osu.Game.dll', 'osu.Game.Rulesets.Mania.dll')) {
            $assemblyPath = Join-Path $binaryDirectory $assemblyName
            $actualVersion = [Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Version.ToString(3)
            if ($actualVersion -ne $items.Properties.OsuVersion) {
                throw "$assemblyName is $actualVersion, but this repository targets $($items.Properties.OsuVersion)."
            }
        }
    }
    if (@($items.Items.Compile).Count -eq 0) { throw "No compile inputs found for $projectName." }
    foreach ($source in $items.Items.Compile) {
        if ($source.FullPath -match '[\\/]obj[\\/]|[\\/]bin[\\/]') { continue }
        $sourceInputs += @{ Path = $source.FullPath; Defines = @($items.Properties.DefineConstants -split ';' | Where-Object { $_ }) }
    }
    if ($projectName -eq 'osu.Game.Rulesets.O2Lazer') {
        # Both standalone modules are analysed from source, preserving their real declaration locations.
        $references = @($items.Items.ReferencePath | Where-Object { $_.Filename -notin @('O2Jam.Core', 'O2Jam.Formats') } | ForEach-Object { $_.FullPath })
    }
}
$inputPath = Join-Path $reportRoot 'inputs.json'
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($inputPath, (@{ Sources = $sourceInputs; References = $references } | ConvertTo-Json -Depth 5), $utf8)
$toolDll = Join-Path $PSScriptRoot "Architecture/bin/$Configuration/net10.0/Architecture.dll"
$reportPath = Join-Path $reportRoot 'dependencies.json'
$toolArguments = @($toolDll, $repositoryRoot, $inputPath, (Join-Path $PSScriptRoot 'architecture-policy.json'), $reportPath)
if ($ReportOnly) { $toolArguments += '--report-only' }
& $DotNet @toolArguments
if ($LASTEXITCODE -ne 0) { throw "Architecture check failed. Report: $reportPath" }
Write-Output "Architecture report: $reportPath"
