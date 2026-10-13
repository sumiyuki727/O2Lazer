$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$roots = @('osu.Game.Rulesets.O2Lazer', 'O2Jam.Core', 'O2Jam.Formats') | ForEach-Object { Join-Path $repositoryRoot $_ }
$testRealmRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'osu.Game.Rulesets.O2Lazer/TestSupport/Persistence/Realm')) + [IO.Path]::DirectorySeparatorChar
[xml]$hostProject = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'osu.Game.Rulesets.O2Lazer/osu.Game.Rulesets.O2Lazer.csproj'))
$testCompile = @($hostProject.SelectNodes('//Compile') | Where-Object { $_.Include.Replace('\', '/').StartsWith('TestSupport/', [StringComparison]::Ordinal) })
$testCondition = "'`$(O2LazerTestBridge)' == 'true'"
# Instrumentation may inspect native storage only while explicitly excluded from normal builds.
if ($hostProject.SelectSingleNode('//EnableDefaultCompileItems').InnerText -ne 'false' -or
    $testCompile.Count -ne 1 -or $testCompile[0].Include.Replace('\', '/') -ne 'TestSupport/**/*.cs' -or
    $testCompile[0].ParentNode.GetAttribute('Condition') -ne $testCondition) {
    throw 'TestSupport compilation is no longer restricted to the opt-in test bridge.'
}
$violations = @()
$sources = @(Get-ChildItem -LiteralPath $roots -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/](?:obj|bin)[\\/]' })
if ($sources.Count -eq 0) { throw 'No source files found for the boundary check.' }
foreach ($source in $sources) {
    $source = $source.FullName
    if ($source -match '[\\/]Host[\\/]Persistence[\\/]Realm[\\/]') { continue }
    if ($source.StartsWith($testRealmRoot, [StringComparison]::OrdinalIgnoreCase)) { continue }
    $code = [IO.File]::ReadAllText($source)
    $code = [regex]::Replace($code, '(?s)/\*.*?\*/|(?m)//[^\r\n]*', '')
    if ($code -match '\busing\s+(?:Realms|osu\.Game\.Database)\b|\b(?:Realm|RealmAccess|RealmConfiguration|RealmFileStore|RealmUser|RealmNamedFileUsage|RealmArchiveModelImporter|IRealmCollection)\b|\bRealms\.') {
        $violations += $source
    }
}
if ($violations.Count) { throw "Direct Realm dependencies escaped Host/Persistence/Realm: $($violations -join ', ')" }
Write-Output 'Direct Realm source boundary passed.'
