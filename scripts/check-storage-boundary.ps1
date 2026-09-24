$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$roots = @('osu.Game.Rulesets.O2Lazer', 'O2Jam.Core', 'O2Jam.Formats') | ForEach-Object { Join-Path $repositoryRoot $_ }
$violations = @()
foreach ($source in (& rg --files $roots -g '*.cs' -g '!obj/**' -g '!bin/**')) {
    if ($source -match '[\\/]Host[\\/]Persistence[\\/]Realm[\\/]') { continue }
    $code = [IO.File]::ReadAllText($source)
    $code = [regex]::Replace($code, '(?s)/\*.*?\*/|(?m)//[^\r\n]*', '')
    if ($code -match '\busing\s+(?:Realms|osu\.Game\.Database)\b|\b(?:Realm|RealmAccess|RealmConfiguration|RealmFileStore|RealmUser|RealmNamedFileUsage|RealmArchiveModelImporter|IRealmCollection)\b|\bRealms\.') {
        $violations += $source
    }
}
if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate sources with rg.' }
if ($violations.Count) { throw "Direct Realm dependencies escaped Host/Persistence/Realm: $($violations -join ', ')" }
Write-Output 'Direct Realm source boundary passed.'
