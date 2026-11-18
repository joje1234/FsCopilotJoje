param([string]$SdkRoot = 'C:\MSFS 2024 SDK', [string]$BuildTools = 'C:\BuildTools2022')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location -LiteralPath $repo
$stage = Join-Path $repo 'artifacts/native-output-check'
New-Item -ItemType Directory -Path (Join-Path $stage 'tests'), (Join-Path $stage 'live') -Force | Out-Null
$devCommand = Join-Path $BuildTools 'Common7/Tools/VsDevCmd.bat'
$environment = & cmd.exe /d /s /c ('""{0}" -no_logo -arch=x64 -host_arch=x64 && set"' -f $devCommand)
if ($LASTEXITCODE -ne 0) { throw 'C++ toolchain initialization failed.' }
foreach ($line in $environment) {
    if ($line -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($matches[1], $matches[2], 'Process') }
}
$include = '/I' + (Join-Path $SdkRoot 'SimConnect SDK/include')
$library = '/LIBPATH:' + (Join-Path $SdkRoot 'SimConnect SDK/lib')
& cl.exe /nologo /std:c++14 /EHsc /W3 /WX FsCopilot.Efb/tests/control_write_probe_test.cpp ('/Fo' + $stage + '\tests\guard.obj') ('/Fe' + $stage + '\tests\guard.exe') *> (Join-Path $stage 'tests/guard-build.log')
if ($LASTEXITCODE -ne 0) { throw 'Diagnostic guard compilation failed; see guard-build.log.' }
& cl.exe /nologo /std:c++14 /EHsc /W3 /WX $include FsCopilot.Efb/tests/control_write_probe_harness.cpp ('/Fo' + $stage + '\tests\harness.obj') ('/Fe' + $stage + '\tests\harness.exe') *> (Join-Path $stage 'tests/harness-build.log')
if ($LASTEXITCODE -ne 0) { throw 'Diagnostic SDK fake compilation failed; see harness-build.log.' }
& cl.exe /nologo /std:c++14 /EHsc /W3 /WX $include FsCopilot.Efb/tests/control_write_probe.cpp ('/Fo' + $stage + '\tests\control_write_probe.obj') ('/Fe' + $stage + '\live\control_write_probe.exe') /link $library SimConnect.lib *> (Join-Path $stage 'tests/sdk-build.log')
if ($LASTEXITCODE -ne 0) { throw 'Live SDK diagnostic compilation failed; see sdk-build.log.' }
& cl.exe /nologo /std:c++14 /EHsc /W3 /WX $include FsCopilot.Efb/tests/cockpit_state_probe.cpp ('/Fo' + $stage + '\tests\cockpit_state_probe.obj') ('/Fe' + $stage + '\live\cockpit_state_probe.exe') /link $library SimConnect.lib *> (Join-Path $stage 'tests/cockpit-build.log')
if ($LASTEXITCODE -ne 0) { throw 'Read-only cockpit capture compilation failed; see cockpit-build.log.' }
Copy-Item -LiteralPath FsCopilot/SimConnect.dll -Destination (Join-Path $stage 'live/SimConnect.dll') -Force
$checks = @('guard', 'same', 'success', 'jitter', 'noise-drift', 'no-brake', 'missing-profile', 'mask-failure', 'mask-timeout', 'hardware', 'ignored', 'echo', 'recaptured', 'partial-write', 'clear-failure',
    'candidate-idle', 'candidate-armed', 'candidate-active', 'candidate-missing', 'candidate-stale', 'candidate-wrong-native', 'candidate-frozen', 'candidate-lost',
    'standard-success', 'standard-ignored', 'standard-echo')
foreach ($name in $checks) {
    $ErrorActionPreference = 'Continue'
    if ($name -eq 'guard') { & (Join-Path $stage 'tests/guard.exe') *> (Join-Path $stage 'tests/guard-test.log') }
    else { & (Join-Path $stage 'tests/harness.exe') $name *> (Join-Path $stage ('tests/' + $name + '.log')) }
    $testExit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($testExit -ne 0) { throw ('Diagnostic check failed: ' + $name) }
    Write-Output ('PASS diagnostic check: ' + $name)
}
Write-Output 'Built live diagnostic, but did not run it. All executed checks used local SDK fakes; no simulator access.'
