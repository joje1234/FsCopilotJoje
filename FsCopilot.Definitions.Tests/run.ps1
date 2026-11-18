param([string]$DotNetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repo
$stage = Join-Path $repo 'artifacts/vanilla-reuse-audit/tests'
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $repo 'artifacts/toolchain/dotnet-home'
$env:NUGET_PACKAGES = Join-Path $repo 'artifacts/toolchain/nuget-packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$ErrorActionPreference = 'Continue'
& $DotNetPath run --project FsCopilot.Definitions.Tests -c Release -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true *> (Join-Path $stage 'definition-tests.log')
$result = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($result -ne 0) {
    Get-Content (Join-Path $stage 'definition-tests.log') -Tail 15
    throw 'Definition checks failed.'
}
Get-Content (Join-Path $stage 'definition-tests.log') -Tail 1
