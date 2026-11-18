param([Parameter(Mandatory=$true)][string]$DotNetPath, [switch]$Sessions, [string]$NativeExePath = 'artifacts/efb-control-test/tests/bridge_transport_probe.exe', [string]$OutputRoot = '')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot
$nativeExe=(Resolve-Path -LiteralPath $NativeExePath).Path
$managedProbe=(Resolve-Path -LiteralPath 'FsCopilot.Native.Probe/bin/Release/net9.0/FsCopilot.Native.Probe.dll').Path
$output=Join-Path $repoRoot 'artifacts/native-bridge'
if($Sessions){$output=Join-Path $repoRoot 'artifacts/native-session/live'}
if($OutputRoot){$output=if([IO.Path]::IsPathRooted($OutputRoot)){$OutputRoot}else{Join-Path $repoRoot $OutputRoot}}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$serverLog=Join-Path $output 'transport-expiry-server.log'
$serverErrors=Join-Path $output 'transport-expiry-server-errors.log'
$clientLog=Join-Path $output 'transport-expiry-client.log'
$testPrefix='FSC_TEST_'+(Get-Date -Format 'HHmmssfff')+'_'
$serverArgs=@($testPrefix)
if($Sessions){$serverArgs+='--session'}
$serverProc=Start-Process -FilePath $nativeExe -ArgumentList $serverArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput $serverLog -RedirectStandardError $serverErrors
# Retain the process handle before it exits. Windows PowerShell otherwise can
# lose ExitCode when a redirected child terminates before WaitForExit is called.
$serverHandle=$serverProc.Handle
try {
    $started=Get-Date
    while(-not $serverProc.HasExited -and ((Get-Date)-$started).TotalSeconds -lt 8) {
        if((Get-Content -LiteralPath $serverLog -Raw -ErrorAction SilentlyContinue) -match 'READY'){break}
        Start-Sleep -Milliseconds 100
    }
    if($serverProc.HasExited -or (Get-Content -LiteralPath $serverLog -Raw) -notmatch 'READY') {
        Get-Content -LiteralPath $serverLog
        throw 'The isolated native SDK server could not connect or initialize.'
    }
    $ErrorActionPreference='Continue'
    $probeArgs=@($managedProbe, "--prefix=$testPrefix", '--expect-expiry', '--seconds=15')
    if($Sessions){$probeArgs+='--session'}
    & $DotNetPath @probeArgs 2>&1 | Out-File -Encoding UTF8 -LiteralPath $clientLog
    $clientCode=$LASTEXITCODE
    $ErrorActionPreference='Stop'
    if(-not $serverProc.WaitForExit(5000)){throw 'The isolated native server did not finish.'}
    $serverProc.Refresh()
    $serverCode=$serverProc.ExitCode
    Get-Content -Encoding UTF8 -LiteralPath $clientLog -Tail 2
    Get-Content -LiteralPath $serverLog -Tail 1
    if($null -eq $serverCode -or $serverCode -ne 0 -or $clientCode -ne 0){throw "Transport check failed: native exit=$serverCode; managed exit=$clientCode"}
    $fixtureScope='Actual production C++ transport and C# client across running MSFS ClientData, with fixture telemetry and isolated names. Fresh discovery, exact values and expiry after server shutdown verified. No aircraft/control writes. WASM-installed-module discovery remains a separate check.'
    if($Sessions){$fixtureScope='Actual production C++ transport and queued C# client across running MSFS ClientData, using isolated fixture areas. Bind, state, committed-transfer ticket, input bytes and expiry after server shutdown verified. No hardware masks or aircraft/control writes. This does not prove installed-WASM remote execution or real multiplayer coordination.'}
    [ordered]@{
        date_utc=[DateTime]::UtcNow.ToString('o'); native_exit=$serverCode; managed_exit=$clientCode; prefix=$testPrefix; session_fixture=[bool]$Sessions
        scope=$fixtureScope
    } | ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $output 'live-transport-verification.json')
    Write-Output 'PASS live native/C# SDK transport and expiry, both processes exited 0.'
} finally {
    if(-not $serverProc.HasExited) { $serverProc.Kill(); $null=$serverProc.WaitForExit(5000) }
    $serverProc.Dispose()
}
