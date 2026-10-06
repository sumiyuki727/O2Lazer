param(
    [ValidateSet('Start', 'Stop', 'Check')]
    [string]$Action = 'Start'
)

$ErrorActionPreference = 'Stop'
$referenceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\.artifacts\o2jamo2-local'))
$serverDirectory = Join-Path $referenceRoot 'server'
$serverPath = Join-Path $serverDirectory 'Identity.Encore.exe'
$clientDirectory = Join-Path $referenceRoot 'client'
$clientPath = Join-Path $clientDirectory 'OTwo.exe'
$processRecordPath = Join-Path $referenceRoot 'server-process.json'
$accountPath = Join-Path $referenceRoot 'account.json'
$port = 15010

function Get-ReferenceGame {
    @(Get-CimInstance Win32_Process -Filter "Name = 'OTwo.exe'" |
        Where-Object { $_.ExecutablePath -eq $clientPath })
}

function Get-OwnedServer {
    if (-not (Test-Path -LiteralPath $processRecordPath -PathType Leaf)) { return $null }
    $record = Get-Content -LiteralPath $processRecordPath -Raw | ConvertFrom-Json
    if ($record.Executable -ne $serverPath) { throw 'The server process record belongs to a different executable.' }
    $process = Get-CimInstance Win32_Process -Filter "ProcessId = $([int]$record.Pid)"
    if (-not $process) { return $null }
    # PID reuse must never let the stop entry terminate an unrelated process.
    if ($process.ExecutablePath -ne $serverPath) { throw 'The recorded PID now belongs to a different executable.' }
    if (-not $record.StartTimeUtc -or
        $process.CreationDate.ToUniversalTime() -ne ([DateTime]$record.StartTimeUtc).ToUniversalTime()) {
        throw 'The recorded process creation time does not match.'
    }
    return $process
}

function Get-Listeners {
    @([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() |
        Where-Object { $_.Port -eq $port })
}

function Assert-ReferenceFiles {
    $expected = @{
        $clientPath = '2C8AC19BC26A7E1CFDE527F5871C22B14FCC5473137101F4BE849435D99B3102'
        $serverPath = 'F6327CD955F9644EC6EB228D07CDB523E8862978F0D1F468E7BF7E85CACD6F09'
    }
    foreach ($path in $expected.Keys) {
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected[$path]) {
            throw "Reference executable changed: $path"
        }
    }
    foreach ($path in @($accountPath, (Join-Path $serverDirectory 'O2JAM.db'),
        (Join-Path $serverDirectory 'OJNList.dat'), (Join-Path $clientDirectory 'Image\OJNList.dat'))) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required local data is missing: $path" }
    }
    # Launch arguments and the server must share the same loopback endpoint.
    $config = Get-Content -LiteralPath (Join-Path $serverDirectory 'config.ini') -Raw
    $serverSettings = [regex]::Match($config, '(?ms)^\[Server\]\r?\n(.*?)(?=^\[|\z)').Groups[1].Value
    if ($serverSettings -notmatch '(?m)^Address=127\.0\.0\.1\s*$' -or
        $serverSettings -notmatch '(?m)^Port=15010\s*$' -or
        $serverSettings -notmatch '(?m)^Mode=Full\s*$') {
        throw 'The local endpoint configuration changed. Review config.ini before launching.'
    }
}

$game = @(Get-ReferenceGame)
$ownedServer = Get-OwnedServer
if ($Action -eq 'Stop') {
    if ($game.Count) { throw 'Exit O2JamO2 first. The stop entry does not interrupt a game.' }
    if ($ownedServer) {
        Stop-Process -Id $ownedServer.ProcessId
        Write-Output 'Local O2JamO2 server stopped.'
    } else {
        Write-Output 'The local O2JamO2 server is already stopped.'
    }
    exit 0
}

Assert-ReferenceFiles
$listeners = @(Get-Listeners)
if ($listeners | Where-Object { $_.Address.ToString() -ne '127.0.0.1' }) {
    throw 'TCP 15010 is listening outside loopback. No process was changed.'
}
if ($listeners.Count -and -not $ownedServer) {
    throw 'TCP 15010 is occupied by an untracked process. No process was changed.'
}
if ($Action -eq 'Check') {
    [pscustomobject]@{
        Client = $clientPath
        Server = $serverPath
        Address = "127.0.0.1:$port"
        ServerRunning = [bool]$ownedServer
        Listening = $listeners.Count -gt 0
        GameRunning = $game.Count -gt 0
    } | ConvertTo-Json
    exit 0
}
if ($game.Count) {
    Write-Output 'O2JamO2 is already running. No second client was launched.'
    exit 0
}
if (-not $ownedServer) {
    $logPrefix = 'server-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
    $stdoutLog = Join-Path $referenceRoot ($logPrefix + '.stdout.log')
    $stderrLog = Join-Path $referenceRoot ($logPrefix + '.stderr.log')
    $serverProcess = Start-Process -FilePath $serverPath -WorkingDirectory $serverDirectory -WindowStyle Hidden `
        -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog -PassThru
    $ownedServer = Get-CimInstance Win32_Process -Filter "ProcessId = $($serverProcess.Id)"
    if (-not $ownedServer -or $ownedServer.ExecutablePath -ne $serverPath) { throw 'Local server did not start.' }
    [pscustomobject]@{
        Pid = $ownedServer.ProcessId
        Executable = $serverPath
        StartTimeUtc = $ownedServer.CreationDate.ToUniversalTime().ToString('o')
        StdoutLog = $stdoutLog
        StderrLog = $stderrLog
    } | ConvertTo-Json | Set-Content -LiteralPath $processRecordPath -Encoding utf8
}

$ready = $false
for ($attempt = 0; $attempt -lt 50; $attempt++) {
    if (-not (Get-OwnedServer)) { throw 'The local server exited during startup. See the newest server-*.stderr.log.' }
    $listeners = @(Get-Listeners)
    if ($listeners.Count -and @($listeners | Where-Object { $_.Address.ToString() -ne '127.0.0.1' }).Count -eq 0) {
        $ready = $true
        break
    }
    Start-Sleep -Milliseconds 200
}
if (-not $ready) { throw 'Local server startup timed out. See the newest server-*.stdout.log.' }

$account = Get-Content -LiteralPath $accountPath -Raw | ConvertFrom-Json
if (-not $account.Username -or -not $account.Password) { throw 'The local test account is incomplete.' }
Push-Location $serverDirectory
try {
    & $serverPath game:start $account.Username $account.Password $clientDirectory
    if ($LASTEXITCODE -ne 0) { throw 'O2JamO2 launch failed.' }
} finally {
    Pop-Location
}
