[CmdletBinding()]
param(
    [int]$LazerProcessId,
    [ValidateSet('start', 'exit', 'exit-state', 'confirm-exit', 'open-song-select', 'capabilities', 'status', 'difficulties', 'select', 'play', 'pause', 'seek', 'rulesets', 'set-ruleset')]
    [string]$Command = 'status',
    [Guid]$ExpectedBeatmapId,
    [Guid]$BeatmapId,
    [double]$TimeMs,
    [string]$Ruleset,
    [string]$ExpectedRuleset,
    [string]$ExecutablePath = "$env:LOCALAPPDATA\osulazer\current\osu!.exe"
)

$ErrorActionPreference = 'Stop'
if ($Command -eq 'start') {
    $resolvedExe = (Resolve-Path -LiteralPath $ExecutablePath).Path
    $directory = Split-Path -Parent $resolvedExe
    # Verify the lazer installation layout so the similarly named stable client is never launched.
    if ((Split-Path -Leaf $resolvedExe) -ne 'osu!.exe' -or
        -not (Test-Path -LiteralPath (Join-Path $directory 'osu.Game.dll')) -or
        -not (Test-Path -LiteralPath (Join-Path $directory 'osu.Game.Rulesets.Mania.dll'))) {
        throw 'ExecutablePath must point to an installed lazer client.'
    }
    $existing = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $resolvedExe })
    if ($existing.Count -gt 0) {
        [pscustomobject]@{ started = $false; processId = $existing[0].ProcessId; executablePath = $resolvedExe }
    } else {
        $process = Start-Process -FilePath $resolvedExe -WorkingDirectory $directory -PassThru
        [pscustomobject]@{ started = $true; processId = $process.Id; executablePath = $resolvedExe }
    }
    return
}
if ($LazerProcessId -le 0) { throw 'LazerProcessId is required for bridge commands.' }
$request = @{ command = $Command }
if ($Command -in @('select', 'play', 'pause', 'seek')) {
    if ($ExpectedBeatmapId -eq [Guid]::Empty) { throw 'ExpectedBeatmapId is required for control commands.' }
    $request.expectedBeatmapId = $ExpectedBeatmapId.ToString()
}
if ($Command -eq 'select') {
    if ($BeatmapId -eq [Guid]::Empty) { throw 'BeatmapId is required for selection.' }
    $request.beatmapId = $BeatmapId.ToString()
}
if ($Command -eq 'seek') { $request.timeMs = $TimeMs }
if ($Command -eq 'set-ruleset') {
    if (-not $Ruleset -or -not $ExpectedRuleset) { throw 'Ruleset and ExpectedRuleset are required.' }
    $request.ruleset = $Ruleset
    $request.expectedRuleset = $ExpectedRuleset
}
$pipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', "o2lazer-test-$LazerProcessId", [System.IO.Pipes.PipeDirection]::InOut)
try {
    $pipe.Connect(3000)
    $reader = [System.IO.StreamReader]::new($pipe)
    $writer = [System.IO.StreamWriter]::new($pipe)
    $writer.AutoFlush = $true
    $writer.WriteLine(($request | ConvertTo-Json -Compress))
    $read = $reader.ReadLineAsync()
    if (-not $read.Wait(12000)) { throw 'Bridge response timed out.' }
    if ($null -eq $read.Result) { throw 'Bridge disconnected without a response.' }
    $response = $read.Result | ConvertFrom-Json
    if (-not $response.success) { throw $response.error }
    $response
}
finally { $pipe.Dispose() }

if ($Command -eq 'exit') {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    $confirmed = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        if (-not (Get-Process -Id $LazerProcessId -ErrorAction SilentlyContinue)) {
            [pscustomobject]@{ exited = $true; processId = $LazerProcessId }
            return
        }
        try {
            $state = (& $PSCommandPath -LazerProcessId $LazerProcessId -Command exit-state).data
            if ($state.ongoingOperations) {
                [pscustomobject]@{ exited = $false; manualConfirmationRequired = $true; reason = 'ongoing-operations' }
                return
            }
            if ($state.canConfirm -and -not $confirmed) {
                & $PSCommandPath -LazerProcessId $LazerProcessId -Command confirm-exit | Out-Null
                $confirmed = $true
            }
        } catch {
            if (Get-Process -Id $LazerProcessId -ErrorAction SilentlyContinue) { throw }
        }
        Start-Sleep -Milliseconds 300
    }
    [pscustomobject]@{ exited = $false; timedOut = $true; processId = $LazerProcessId }
}
