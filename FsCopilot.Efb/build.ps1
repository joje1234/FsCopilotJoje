param([string]$MSBuildPath = "", [string]$NodePath = "node", [string]$ArtifactRoot = "", [string]$DotNetPath = "dotnet")
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ArtifactRoot) { $ArtifactRoot = Join-Path $repoRoot "artifacts\efb-control-test" }
elseif (-not [IO.Path]::IsPathRooted($ArtifactRoot)) { $ArtifactRoot = Join-Path $repoRoot $ArtifactRoot }
$ArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
$nativeOutput = Join-Path $ArtifactRoot "native"

if (-not (Get-Command $NodePath -ErrorAction SilentlyContinue)) { throw "Install Node.js 16 or later, or pass -NodePath to a portable node.exe." }
if (-not $env:MSFS2024_SDK) {
    # The current terminal may predate SDK installation.
    $env:MSFS2024_SDK = [Environment]::GetEnvironmentVariable("MSFS2024_SDK", "User")
    if (-not $env:MSFS2024_SDK) { $env:MSFS2024_SDK = [Environment]::GetEnvironmentVariable("MSFS2024_SDK", "Machine") }
}
if (-not $env:MSFS2024_SDK -or -not (Test-Path (Join-Path $env:MSFS2024_SDK "WASM\llvm\bin\clang-cl.exe"))) {
    throw "Install the MSFS 2024 SDK and set MSFS2024_SDK to its installation directory."
}
# The SDK toolset concatenates this value with WASM\vs\..., requiring a trailing slash.
$env:MSFS2024_SDK = $env:MSFS2024_SDK.TrimEnd('\', '/') + '\'
if (-not $MSBuildPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $instances = & $vswhere -all -products * -requires Microsoft.Component.MSBuild -format json | ConvertFrom-Json
        foreach ($instance in $instances) {
            $buildRoot = Join-Path $instance.installationPath "MSBuild"
            $candidate = Join-Path $buildRoot "Current\Bin\MSBuild.exe"
            if ((Test-Path $candidate) -and (Test-Path (Join-Path $buildRoot "Microsoft\VC\*\Platforms\MSFS\PlatformToolsets\MSFS2024\Toolset.props"))) {
                $MSBuildPath = $candidate
                break
            }
        }
    }
    if (-not $MSBuildPath) {
        $installed = Get-Command msbuild -ErrorAction SilentlyContinue
        if ($installed) { $MSBuildPath = $installed.Source }
    }
}
if (-not $MSBuildPath -or -not (Test-Path $MSBuildPath)) { throw "Install Visual Studio C++ build tools and the MSFS 2024 SDK/toolset, or pass -MSBuildPath." }

Write-Host "Using MSBuild: $MSBuildPath"
$env:DOTNET_CLI_HOME = Join-Path $repoRoot 'artifacts/toolchain/dotnet-home'
$env:NUGET_PACKAGES = Join-Path $repoRoot 'artifacts/toolchain/nuget-packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $DotNetPath run --project (Join-Path $repoRoot 'FsCopilot.Definitions.Compiler') -c Release -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true -- (Join-Path $PSScriptRoot 'Wasm/control_definition_catalog.h')
if ($LASTEXITCODE -ne 0) { throw "Vanilla control-definition compilation failed." }
& $NodePath (Join-Path $PSScriptRoot "scripts\generate-events.mjs")
if ($LASTEXITCODE -ne 0) { throw "Event generation failed." }
& $NodePath (Join-Path $PSScriptRoot "scripts\build-ui.mjs")
if ($LASTEXITCODE -ne 0) { throw "EFB build failed." }
New-Item -ItemType Directory -Force $nativeOutput | Out-Null
& $MSBuildPath (Join-Path $PSScriptRoot "Wasm\FsCopilotControls.vcxproj") /t:Rebuild /p:Configuration=Release /p:Platform=MSFS "/p:OutDir=$nativeOutput\" "/p:IntDir=$nativeOutput\obj\" /nologo
if ($LASTEXITCODE -ne 0) { throw "WASM build failed. Verify the MSFS2024 Visual Studio toolset is installed." }
& $NodePath (Join-Path $PSScriptRoot "tests/wasm_stack.test.cjs") (Join-Path $nativeOutput "FsCopilotControls.wasm")
if ($LASTEXITCODE -ne 0) { throw "Compiled WASM exceeds the MSFS callback stack budget." }
$moduleSources = Join-Path $PSScriptRoot "PackageSources\Modules"
New-Item -ItemType Directory -Force $moduleSources | Out-Null
Copy-Item (Join-Path $nativeOutput "FsCopilotControls.wasm") (Join-Path $moduleSources "FsCopilotControls.wasm") -Force
$package = Join-Path $ArtifactRoot "fscopilot-controls-test"
& $NodePath (Join-Path $PSScriptRoot "scripts\package.mjs") --wasm (Join-Path $nativeOutput "FsCopilotControls.wasm") --output $package
if ($LASTEXITCODE -ne 0) { throw "Community packaging failed." }
$zip = Join-Path $ArtifactRoot "fscopilot-controls-test.zip"
Compress-Archive -Path $package -DestinationPath $zip -Force
Write-Host "Ready to copy to Community: $package"
Write-Host "Shareable package: $zip"
