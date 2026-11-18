param([string]$DotNetPath = 'dotnet', [switch]$CppOnly)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot
$output = Join-Path $repoRoot 'artifacts/altimeter-native-command/tests'
foreach ($directory in @($output, 'artifacts/efb-control-test/tests', 'artifacts/native-session/tests', 'artifacts/cockpit-recovery/tests')) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
$env:DOTNET_CLI_HOME = Join-Path $repoRoot 'artifacts/toolchain/dotnet-home'
$env:NUGET_PACKAGES = Join-Path $repoRoot 'artifacts/toolchain/nuget-packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$windows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
if ($windows -and -not (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
    $devCmd = 'C:/BuildTools2022/Common7/Tools/VsDevCmd.bat'
    if (-not (Test-Path -LiteralPath $devCmd)) {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio C++ Build Tools or use a developer shell.' }
        $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        $devCmd = Join-Path $installation 'Common7/Tools/VsDevCmd.bat'
    }
    $environment = & cmd.exe /d /s /c ('""{0}" -no_logo -arch=x64 -host_arch=x64 && set"' -f $devCmd)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize the C++ toolchain.' }
    foreach ($line in $environment) {
        if ($line -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($matches[1], $matches[2], 'Process') }
    }
}
function Invoke-Checked([string]$Program, [string[]]$Arguments, [string]$LogPath) {
    $ErrorActionPreference = 'Continue'
    & $Program @Arguments *> $LogPath
    $result = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($result -ne 0) {
        Get-Content -LiteralPath $LogPath -Tail 15
        throw ('Command failed ({0}): {1}. Log: {2}' -f $result, $Program, $LogPath)
    }
}
function Invoke-CppTest([string]$Name) {
    $source = Join-Path $repoRoot ('FsCopilot.Efb/tests/' + $Name + '_test.cpp')
    $stubs = Join-Path $repoRoot 'FsCopilot.Efb/tests/stubs'
    $executable = Join-Path $output ($Name + '_test' + $(if ($windows) { '.exe' } else { '' }))
    if ($windows) {
        Invoke-Checked 'cl.exe' @('/nologo', '/std:c++14', '/EHsc', '/W3', '/WX', ('/I' + $stubs), $source,
            ('/Fo' + (Join-Path $output ($Name + '.obj'))), ('/Fe' + $executable)) (Join-Path $output ($Name + '-build.log'))
    } else {
        $compiler = Get-Command clang++ -ErrorAction SilentlyContinue
        if (-not $compiler) { $compiler = Get-Command g++ -ErrorAction Stop }
        Invoke-Checked $compiler.Source @('-std=c++14', '-Wall', '-Werror', ('-I' + $stubs), $source, '-o', $executable) (Join-Path $output ($Name + '-build.log'))
    }
    Invoke-Checked $executable @() (Join-Path $output ($Name + '-test.log'))
    Write-Output ('PASS C++ ' + $Name)
}
# These runs generate actual production-module packets before C# reads them.
New-Item -ItemType Directory -Path (Join-Path $repoRoot 'artifacts/guidance-channel/tests') -Force | Out-Null
Invoke-CppTest 'native'
Invoke-CppTest 'pickup'
Invoke-CppTest 'runtime'
Invoke-CppTest 'force_button'
Invoke-CppTest 'force_button_module'
Invoke-CppTest 'cockpit_refs'
Invoke-CppTest 'cockpit'
Invoke-CppTest 'instrument_monitor'
Invoke-CppTest 'shared_autopilot'
Invoke-CppTest 'targets'
Invoke-CppTest 'definition_output'
Invoke-CppTest 'output_check'
Invoke-CppTest 'output_validation_storage'
Invoke-CppTest 'mixed_brakes'
Invoke-CppTest 'setup_wizard'
Invoke-CppTest 'native_output_check'
Invoke-CppTest 'remote_writer'
Invoke-CppTest 'session'
Invoke-CppTest 'world'
Invoke-CppTest 'barometer_session'
Invoke-CppTest 'guidance_channel'
if ($CppOnly) { Write-Output 'C++ regression complete; C# and reciprocal checks not requested.'; return }
Invoke-Checked $DotNetPath @('run', '--project', 'FsCopilot.Native.Tests', '-c', 'Release',
    '-p:NuGetAudit=false', '-p:RestoreIgnoreFailedSources=true') (Join-Path $output 'native-contract.log')
Get-Content -LiteralPath (Join-Path $output 'native-contract.log') -Tail 1
# C# has now emitted its reciprocal command/status fixtures for C++ validation.
Invoke-CppTest 'barometer_channel'
Invoke-CppTest 'altimeter'
Invoke-CppTest 'guidance'
$guidanceExecutable = Join-Path $output ('guidance_channel_test' + $(if ($windows) { '.exe' } else { '' }))
Invoke-Checked $guidanceExecutable @('--validate-csharp') (Join-Path $output 'guidance-reciprocal.log')
Write-Output 'PASS reciprocal guidance wire'
Write-Output 'Native contracts passed with simulated SDK/input providers; no simulator was contacted.'
