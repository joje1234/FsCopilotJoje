param([string]$DotNetPath = 'dotnet', [switch]$NativeOnly, [switch]$ConnectionOnly, [switch]$InstrumentsOnly, [switch]$GuidanceOnly, [switch]$NativeGuidanceOnly)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot
$env:DOTNET_CLI_HOME = Join-Path $repoRoot 'artifacts/toolchain/dotnet-home'
$env:NUGET_PACKAGES = Join-Path $repoRoot 'artifacts/toolchain/nuget-packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if ($GuidanceOnly) {
    $ErrorActionPreference = 'Continue'
    & $DotNetPath run --project FsCopilot.Network.Tests -c Release -p:UseAppHost=false -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true -- --guidance-only
    $testExit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($testExit -ne 0) { throw 'Guidance relay checks failed.' }
    return
}
$output = Join-Path $repoRoot 'artifacts/native-coordinator/tests'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$source = Join-Path $repoRoot 'FsCopilot.Efb/tests/native_session_harness.cpp'
$stubs = Join-Path $repoRoot 'FsCopilot.Efb/tests/stubs'
if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
    if (-not (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
        $devCmd = 'C:/BuildTools2022/Common7/Tools/VsDevCmd.bat'
        if (-not (Test-Path -LiteralPath $devCmd)) {
            $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
            if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio C++ Build Tools or run from a developer shell.' }
            $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
            $devCmd = Join-Path $installation 'Common7/Tools/VsDevCmd.bat'
        }
        $environment = & cmd.exe /d /s /c ('""{0}" -no_logo -arch=x64 -host_arch=x64 && set"' -f $devCmd)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize the C++ toolchain.' }
        foreach ($line in $environment) {
            if ($line -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($matches[1], $matches[2], 'Process') }
        }
    }
    & cl.exe /nologo /std:c++14 /EHsc /W3 /WX ('/I' + $stubs) $source ('/Fo' + (Join-Path $output 'native_session_harness.obj')) ('/Fe' + (Join-Path $output 'native_session_harness.exe'))
} else {
    $compiler = Get-Command clang++ -ErrorAction SilentlyContinue
    if (-not $compiler) { $compiler = Get-Command g++ -ErrorAction Stop }
    & $compiler.Source -std=c++14 -Wall -Werror ('-I' + $stubs) $source -o (Join-Path $output 'native_session_harness')
}
if ($LASTEXITCODE -ne 0) { throw 'Native harness compilation failed.' }
$arguments = @('run', '--project', 'FsCopilot.Network.Tests', '-c', 'Release', '-p:UseAppHost=false', '-p:NuGetAudit=false', '-p:RestoreIgnoreFailedSources=true')
if ($NativeGuidanceOnly) { $arguments += @('--', '--native-guidance-only') }
elseif ($InstrumentsOnly) { $arguments += @('--', '--instruments-only') }
elseif ($ConnectionOnly) { $arguments += @('--', '--connection-only') }
elseif ($NativeOnly) { $arguments += @('--', '--native-only') }
$ErrorActionPreference = 'Continue'
& $DotNetPath @arguments
$testExit = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($testExit -ne 0) { throw 'Network/native coordinator checks failed.' }
