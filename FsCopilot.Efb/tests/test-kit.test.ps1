param([string]$KitRoot='')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if(-not $KitRoot){$KitRoot=Join-Path $repo 'artifacts/two-player-ready/TestKit'}
$fixture=Join-Path $repo ('artifacts/two-player-ready/tests/fixture-'+[Guid]::NewGuid().ToString('N'))
$kit=Join-Path $fixture 'Kit with spaces';$community=Join-Path $fixture 'Community';$reports=Join-Path $fixture 'Reports'
New-Item -ItemType Directory -Path $kit,$community,$reports -Force | Out-Null
$manifest=Get-Content -LiteralPath (Join-Path $KitRoot 'test-kit.json') -Raw | ConvertFrom-Json
foreach($path in @($manifest.payloads.path)+@('test-kit.json','TEST RESULTS.txt')){
    $target=Join-Path $kit $path;New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $KitRoot $path) -Destination $target
}
foreach($payload in $manifest.payloads | Where-Object {$_.path.StartsWith('Community/')}){
    $target=Join-Path $community $payload.path.Substring('Community/'.Length)
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $kit $payload.path) -Destination $target
}
$script=Join-Path $kit 'Check setup.ps1'
function Check([bool]$condition,[string]$reason){if(-not $condition){throw $reason}}
function Inspect{& $script -KitRoot $kit -CommunityPath $community -ReportDirectory $reports -Mode Files -PassThru}
$result=Inspect;Check $result.passed 'Matching complete kit/package did not pass file validation.'
$missing='Helper/Definitions/modules/flight_controls.yaml'
Remove-Item -LiteralPath (Join-Path $kit $missing)
$result=Inspect;Check (-not $result.passed -and ($result.checks.detail -join ' ').Contains('flight_controls.yaml')) 'Missing included module was not named.'
Copy-Item -LiteralPath (Join-Path $KitRoot $missing) -Destination (Join-Path $kit $missing)
$ui='fscopilot-controls-test/html_ui/efb_ui/efb_apps/FsCopilotControls/FsCopilotControls.js'
[IO.File]::WriteAllText((Join-Path $community $ui),'wrong UI')
$result=Inspect;Check (-not $result.passed -and ($result.checks.detail -join ' ').Contains('FsCopilotControls.js')) 'A mismatched installed UI was not refused.'
Copy-Item -LiteralPath (Join-Path $kit ('Community/'+$ui)) -Destination (Join-Path $community $ui) -Force
$bridge=Join-Path $community 'fscopilot-bridge.disabled'
New-Item -ItemType Directory -Path $bridge -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $bridge 'manifest.json'),'{}')
$result=Inspect;Check (-not $result.passed -and @($result.checks | Where-Object name -EQ 'Conflicting package').Count -eq 1) 'Renaming a live bridge inside Community escaped conflict detection.'
Remove-Item -LiteralPath (Join-Path $bridge 'manifest.json')
[IO.File]::WriteAllText((Join-Path $kit 'TEST RESULTS.txt'),'P1 fixture failure: safe to collect')
$result=Inspect;Check $result.passed 'Editing the intended test card invalidated protected program files.'
$manifestPath=Join-Path $kit 'test-kit.json';$originalManifest=[IO.File]::ReadAllText($manifestPath)
$malformed=$originalManifest | ConvertFrom-Json
$malformed.payloads+= [pscustomobject]@{path='../outside.txt';bytes=1;sha256=('0'*64)}
[IO.File]::WriteAllText($manifestPath,($malformed | ConvertTo-Json -Depth 10))
$result=Inspect;Check (-not $result.passed -and ($result.checks.detail -join ' ').Contains('escapes')) 'Manifest traversal was not rejected before file inspection.'
[IO.File]::WriteAllText($manifestPath,$originalManifest)
$malformed=$originalManifest | ConvertFrom-Json;$malformed.payloads+= $malformed.payloads[0]
[IO.File]::WriteAllText($manifestPath,($malformed | ConvertTo-Json -Depth 10))
$result=Inspect;Check (-not $result.passed -and ($result.checks.detail -join ' ').Contains('duplicate')) 'Duplicate manifest payload was accepted.'
[IO.File]::WriteAllText($manifestPath,$originalManifest)

# Exercise the real CMD -> PowerShell -> ZIP chain in an isolated app-data tree.
$priorApp=$env:APPDATA;$priorLocal=$env:LOCALAPPDATA
try{
    $env:APPDATA=Join-Path $fixture 'AppData';$env:LOCALAPPDATA=Join-Path $fixture 'LocalAppData'
    $logs=Join-Path $env:LOCALAPPDATA 'FsCopilot/Efb/Logs'
    $work=Join-Path $env:APPDATA 'Microsoft Flight Simulator 2024/WASM/MSFS2020/fscopilot-controls-test/work'
    New-Item -ItemType Directory -Path $logs,$work -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $logs 'helper-fixture.log'),'fixture helper session-ended reason')
    [IO.File]::WriteAllText((Join-Path $work 'controls-test.log'),'fixture native held-mask reason')
    [IO.File]::WriteAllText((Join-Path $work 'connection-settings.0.bin'),'DO NOT EXPORT SETTINGS')
    & (Join-Path $kit 'Collect test logs.ps1') -KitRoot $kit -Destination $reports
    $zip=Get-ChildItem -LiteralPath $reports -Filter 'FsCopilot-diagnostics-*.zip' -File | Select-Object -First 1
    Check ($null -ne $zip) 'No diagnostic ZIP was created.'
    $archive=[IO.Compression.ZipFile]::OpenRead($zip.FullName)
    try{
        $names=@($archive.Entries.FullName)
        Check (@($names | Where-Object {$_ -like '*.bin'}).Count -eq 0) 'Saved settings entered diagnostic ZIP.'
        Check (@($names | Where-Object {$_ -like '*native-1.log'}).Count -eq 1) 'Native log was not collected.'
        Check (@($names | Where-Object {$_ -like '*helper-fixture.log'}).Count -eq 1) 'Helper log was not collected.'
        Check ($names -contains 'Test kit/TEST RESULTS.txt') 'Test card missing from diagnostics.'
        Check ($names -contains 'Test kit/test-kit.json') 'Kit provenance missing from diagnostics.'
        Check (@($names | Where-Object {$_ -like 'Test kit/setup-*.json'}).Count -gt 0) 'Preflight report missing from diagnostics.'
    }finally{$archive.Dispose()}
}finally{$env:APPDATA=$priorApp;$env:LOCALAPPDATA=$priorLocal}
Write-Output 'PASS test kit: exact file integrity, named missing includes, installed UI mismatch, disabled-name bridge detection, editable test card, traversal/duplicate refusal and real CMD/ZIP collection with helper/native logs, provenance and no saved settings. No simulator/network access.'
