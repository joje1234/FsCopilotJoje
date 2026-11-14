param([string]$MSBuildPath = "")
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$nativeOutput = Join-Path $repoRoot "artifacts\efb-control-test\native"

if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw "Install Node.js 16 or later to run the dependency-free package scripts." }
if (-not $MSBuildPath) {
    $installed = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($installed) { $MSBuildPath = $installed.Source }
    else {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
        if (Test-Path $vswhere) {
            $MSBuildPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        }
    }
}
if (-not $MSBuildPath -or -not (Test-Path $MSBuildPath)) { throw "Install Visual Studio C++ build tools and the MSFS 2024 SDK/toolset, or pass -MSBuildPath." }

& node (Join-Path $PSScriptRoot "scripts\generate-events.mjs")
if ($LASTEXITCODE -ne 0) { throw "Event generation failed." }
& node (Join-Path $PSScriptRoot "scripts\build-ui.mjs")
if ($LASTEXITCODE -ne 0) { throw "EFB build failed." }
New-Item -ItemType Directory -Force $nativeOutput | Out-Null
& $MSBuildPath (Join-Path $PSScriptRoot "Wasm\FsCopilotControls.vcxproj") /t:Rebuild /p:Configuration=Release /p:Platform=MSFS "/p:OutDir=$nativeOutput\" "/p:IntDir=$nativeOutput\obj\" /nologo
if ($LASTEXITCODE -ne 0) { throw "WASM build failed. Verify the MSFS2024 Visual Studio toolset is installed." }
$moduleSources = Join-Path $PSScriptRoot "PackageSources\Modules"
New-Item -ItemType Directory -Force $moduleSources | Out-Null
Copy-Item (Join-Path $nativeOutput "FsCopilotControls.wasm") (Join-Path $moduleSources "FsCopilotControls.wasm") -Force
& node (Join-Path $PSScriptRoot "scripts\package.mjs") --wasm (Join-Path $nativeOutput "FsCopilotControls.wasm")
if ($LASTEXITCODE -ne 0) { throw "Community packaging failed." }
$package = Join-Path $repoRoot "artifacts\efb-control-test\fscopilot-controls-test"
$zip = Join-Path $repoRoot "artifacts\efb-control-test\fscopilot-controls-test.zip"
Compress-Archive -Path $package -DestinationPath $zip -Force
Write-Host "Ready to copy to Community: $package"
Write-Host "Shareable package: $zip"
