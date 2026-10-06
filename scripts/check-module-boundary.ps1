$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$moduleRoots = @{
    'O2Jam.Core' = 'O2Jam.Core'
    'O2Jam.Formats' = 'O2Jam.Formats'
    'osu.Game.Rulesets.O2Lazer' = $null
}
$violations = @()
foreach ($module in $moduleRoots.GetEnumerator()) {
    $moduleRoot = Join-Path $repositoryRoot $module.Key
    # Hosted runners have PowerShell but do not necessarily provide ripgrep.
    $sources = @(Get-ChildItem -LiteralPath $moduleRoot -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '[\\/](?:obj|bin)[\\/]' })
    if ($sources.Count -eq 0) { throw "No source files found in $moduleRoot." }
    foreach ($source in $sources) {
        $source = $source.FullName
        $code = [IO.File]::ReadAllText($source)
        $code = [regex]::Replace($code, '(?s)/\*.*?\*/|(?m)//[^\r\n]*', '')
        $namespaces = [regex]::Matches($code, '(?m)^\s*namespace\s+([\w.]+)\s*[;{]')
        if ($namespaces.Count -eq 0) { $violations += "$source has no explicit namespace." }
        foreach ($declaration in $namespaces) {
            $name = $declaration.Groups[1].Value
            if ($module.Value) {
                if ($name -ne $module.Value -and -not $name.StartsWith($module.Value + '.', [StringComparison]::Ordinal)) {
                    $violations += "$source declares $name outside $($module.Value)."
                }
            }
            # A host source must not look like an API supplied by a standalone project.
            elseif ($name -match '^(?:O2Jam\.(?:Core|Formats)|osu\.Game\.Rulesets\.O2Lazer\.(?:Core|Formats))(?:\.|$)') {
                $violations += "$source claims a standalone module namespace: $name."
            }
        }
    }
    if (-not $module.Value) { continue }
    $projectPath = Join-Path $moduleRoot ($module.Key + '.csproj')
    [xml]$project = [IO.File]::ReadAllText($projectPath)
    # Core and Formats have separate portability contracts; even a reference to each other breaks them.
    foreach ($reference in $project.SelectNodes('//ProjectReference|//Reference|//PackageReference')) {
        if ($module.Key -eq 'O2Jam.Formats' -and $reference.Name -eq 'PackageReference' -and $reference.Include -eq 'System.Text.Encoding.CodePages') { continue }
        $violations += "$projectPath declares an unsupported dependency: $($reference.OuterXml)."
    }
    if ($project.SelectSingleNode('//RootNamespace').InnerText -ne $module.Value) {
        $violations += "$projectPath has a mismatched RootNamespace."
    }
}
if ($violations.Count) { throw ($violations -join [Environment]::NewLine) }
Write-Output 'Standalone module source and project boundaries passed.'
