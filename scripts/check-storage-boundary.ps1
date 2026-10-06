$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$roots = @('osu.Game.Rulesets.O2Lazer', 'O2Jam.Core', 'O2Jam.Formats') | ForEach-Object { Join-Path $repositoryRoot $_ }
$violations = @()
$sources = @(Get-ChildItem -LiteralPath $roots -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/](?:obj|bin)[\\/]' })
if ($sources.Count -eq 0) { throw 'No source files found for the boundary check.' }
foreach ($source in $sources) {
    $source = $source.FullName
    if ($source -match '[\\/]Host[\\/]Persistence[\\/]Realm[\\/]') { continue }
    $code = [IO.File]::ReadAllText($source)
    $code = [regex]::Replace($code, '(?s)/\*.*?\*/|(?m)//[^\r\n]*', '')
    if ($code -match '\busing\s+(?:Realms|osu\.Game\.Database)\b|\b(?:Realm|RealmAccess|RealmConfiguration|RealmFileStore|RealmUser|RealmNamedFileUsage|RealmArchiveModelImporter|IRealmCollection)\b|\bRealms\.') {
        $violations += $source
    }
}
if ($violations.Count) { throw "Direct Realm dependencies escaped Host/Persistence/Realm: $($violations -join ', ')" }
Write-Output 'Direct Realm source boundary passed.'
