param([string]$DotNetPath = "dotnet")
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $repoRoot "artifacts\ownership-protocol"
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $repoRoot "artifacts\toolchain\dotnet-home"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = "false"
$env:NUGET_PACKAGES = Join-Path $repoRoot "artifacts\toolchain\nuget-packages"
$project = Join-Path $PSScriptRoot "FsCopilot.Ownership.Tests.csproj"
& $DotNetPath run --project $project -c Release -p:NuGetAudit=false 2>&1 |
    Tee-Object -FilePath (Join-Path $outputRoot "tests.log")
if ($LASTEXITCODE -ne 0) { throw "Ownership protocol checks failed." }
