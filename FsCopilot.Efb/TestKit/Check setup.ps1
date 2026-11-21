param(
    [string]$KitRoot = $PSScriptRoot,
    [string]$CommunityPath = '',
    [ValidateSet('Ready','Files')][string]$Mode = 'Ready',
    [string]$ReportDirectory = '',
    [switch]$PassThru
)
$ErrorActionPreference = 'Stop'
$KitRoot = [IO.Path]::GetFullPath($KitRoot)
if (-not $ReportDirectory) { $ReportDirectory = Join-Path $KitRoot 'Reports' }
$checks = New-Object 'System.Collections.Generic.List[object]'
function Add-Check([string]$Status,[string]$Name,[string]$Detail) {
    $checks.Add([pscustomobject]@{status=$Status;name=$Name;detail=$Detail})
}
function Resolve-Payload([string]$Root,[string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative)) { throw 'Invalid absolute/empty payload path.' }
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    $resolved = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (-not $resolved.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Payload path escapes its named folder.' }
    return $resolved
}
function Check-Payload([string]$Root,[string]$Relative,$Payload,[string]$Label) {
    try {
        $file = Resolve-Payload $Root $Relative
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw ('Missing file: ' + $file) }
        if ((Get-Item -LiteralPath $file).Length -ne $Payload.bytes -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $Payload.sha256) { throw ('File differs from this kit: ' + $file) }
        return $true
    } catch { Add-Check 'FAIL' $Label $_.Exception.Message; return $false }
}
$manifest = $null
try {
    $manifest = Get-Content -LiteralPath (Join-Path $KitRoot 'test-kit.json') -Raw | ConvertFrom-Json
    if ($manifest.schema -ne 1 -or $manifest.version -ne '0.9.12' -or @($manifest.payloads).Count -lt 7) { throw 'Unsupported or incomplete test-kit manifest.' }
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($payload in $manifest.payloads) {
        if (-not $seen.Add($payload.path) -or $payload.sha256 -notmatch '^[0-9a-fA-F]{64}$' -or $payload.bytes -lt 0) { throw 'Invalid or duplicate manifest entry.' }
        [void](Resolve-Payload $KitRoot $payload.path)
    }
    foreach ($required in @('Helper/FsCopilot.Efb.exe','Helper/Definitions/bksq-aircraft-baronpropress.yaml','Helper/Definitions/controls/bksq-aircraft-baronpropress.json','Helper/Definitions/cockpit/bksq-aircraft-baronpropress.json','Community/fscopilot-controls-test/manifest.json','Community/fscopilot-controls-test/modules/FsCopilotControls.wasm')) {
        if (-not $seen.Contains($required)) { throw ('Required manifest entry missing: ' + $required) }
    }
    $good = $true
    foreach ($payload in $manifest.payloads) { if (-not (Check-Payload $KitRoot $payload.path $payload 'Kit files')) { $good=$false } }
    if ($good) { Add-Check 'PASS' 'Kit files' ('All '+@($manifest.payloads).Count+' protected files match version '+$manifest.version+'.') }
} catch { Add-Check 'FAIL' 'Kit manifest' $_.Exception.Message; $manifest=$null }

$possible = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$configs = @((Join-Path $env:APPDATA 'Microsoft Flight Simulator 2024/UserCfg.opt'),(Join-Path $env:LOCALAPPDATA 'Packages/Microsoft.Limitless_8wekyb3d8bbwe/LocalCache/UserCfg.opt'))
foreach ($config in $configs) {
    if (-not (Test-Path -LiteralPath $config -PathType Leaf)) { continue }
    foreach ($line in [IO.File]::ReadAllLines($config)) {
        if ($line -match '^\s*InstalledPackagesPath\s+"([^"]+)"') {
            foreach ($name in @('Community2024','Community')) {
                $folder = Join-Path $matches[1] $name
                if (Test-Path -LiteralPath $folder -PathType Container) { [void]$possible.Add([IO.Path]::GetFullPath($folder)) }
            }
        }
    }
}
if ($CommunityPath) { $CommunityPath=[IO.Path]::GetFullPath($CommunityPath) }
elseif ($Mode -eq 'Ready') {
    $installed = @($possible | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'fscopilot-controls-test/manifest.json') -PathType Leaf })
    if ($installed.Count -eq 1) { $CommunityPath=$installed[0] }
    elseif ($installed.Count -gt 1) { Add-Check 'FAIL' 'Community folder' ('More than one FS Copilot install found. Choose the active folder explicitly with -CommunityPath: '+($installed -join '; ')) }
    elseif ($possible.Count -eq 1) { $CommunityPath=@($possible)[0] }
    else { Add-Check 'FAIL' 'Community folder' 'Cannot identify one active Community folder. Run this script with -CommunityPath pointing to the folder where the prior EFB package was installed.' }
}
if ($CommunityPath -and $manifest) {
    $installed = Join-Path $CommunityPath 'fscopilot-controls-test'
    $prefix = 'Community/fscopilot-controls-test/'
    $packageFiles = @($manifest.payloads | Where-Object {$_.path.StartsWith($prefix,[StringComparison]::Ordinal)})
    $good = $packageFiles.Count -eq 7
    if (-not $good) { Add-Check 'FAIL' 'Installed package' 'The manifest must contain exactly seven Community files.' }
    foreach ($payload in $packageFiles) { if (-not (Check-Payload $installed $payload.path.Substring($prefix.Length) $payload 'Installed package')) { $good=$false } }
    if ($good) { Add-Check 'PASS' 'Installed package' ('Seven files match 0.9.12 in '+$installed) }
    # Renaming a package inside Community does not disable it: its manifest still loads.
    if (Test-Path -LiteralPath $CommunityPath -PathType Container) {
        foreach ($folder in Get-ChildItem -LiteralPath $CommunityPath -Directory) {
            if ($folder.Name -eq 'fscopilot-controls-test') { continue }
            $entry = Join-Path $folder.FullName 'manifest.json'
            if (-not (Test-Path -LiteralPath $entry -PathType Leaf)) { continue }
            try { $title=(Get-Content -LiteralPath $entry -Raw | ConvertFrom-Json).title } catch { $title='' }
            if ($folder.Name -like 'fscopilot-bridge*' -or $title -match 'FS\s*Copilot.*(Bridge|Controls Test)') {
                Add-Check 'FAIL' 'Conflicting package' ('Move this entire folder OUTSIDE Community while MSFS is closed: '+$folder.FullName)
            }
        }
    }
}
if ($Mode -eq 'Ready') {
    $vanilla = @(Get-Process -Name 'FsCopilot' -ErrorAction SilentlyContinue)
    if ($vanilla.Count) { Add-Check 'FAIL' 'Vanilla application' ('Close vanilla FsCopilot.exe before testing. PID(s): '+(($vanilla | Select-Object -ExpandProperty Id)-join ', ')) }
    $helpers = @(Get-Process -Name 'FsCopilot.Efb' -ErrorAction SilentlyContinue)
    if ($helpers.Count -ne 1) { Add-Check 'FAIL' 'Running helper' ('Expected exactly one FsCopilot.Efb.exe; found '+$helpers.Count+'. Start Helper/FsCopilot.Efb.exe once.') }
    elseif ($manifest) {
        try {
            $helperFolder=Split-Path -Parent $helpers[0].Path
            $good=$true
            foreach ($payload in $manifest.payloads | Where-Object {$_.path.StartsWith('Helper/',[StringComparison]::Ordinal)}) {
                if (-not (Check-Payload $helperFolder $payload.path.Substring(7) $payload 'Running helper files')) { $good=$false }
            }
            $definitionNames=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
            foreach($payload in $manifest.payloads | Where-Object {$_.path.StartsWith('Helper/Definitions/',[StringComparison]::Ordinal)}){[void]$definitionNames.Add($payload.path.Substring(7).Replace('/','\'))}
            $definitionFolder=Join-Path $helperFolder 'Definitions'
            if(Test-Path -LiteralPath $definitionFolder -PathType Container){foreach($file in Get-ChildItem -LiteralPath $definitionFolder -Recurse -File){
                $relative=$file.FullName.Substring($helperFolder.Length+1)
                if(-not $definitionNames.Contains($relative)){
                    $good=$false;Add-Check 'FAIL' 'Running helper definitions' ('Unexpected extra definition; use the complete fresh Helper folder from this kit: '+$file.FullName)
                }
            }}
            if ($good) { Add-Check 'PASS' 'Running helper' ('Matching helper and complete definitions; PID '+$helpers[0].Id+'; '+$helpers[0].Path) }
        } catch { Add-Check 'FAIL' 'Running helper' $_.Exception.Message }
    }
    Add-Check 'INFO' 'Simulator checks' 'This checker does not contact MSFS or the relay. Both EFBs must pass the wizard, show all four output groups checked and confirm a shared connection in the live test.'
}
$report = [pscustomobject]@{schema=1;version='0.9.12';timestamp=[DateTimeOffset]::Now.ToString('o');mode=$Mode;kit=$KitRoot;community=$CommunityPath;passed=@($checks | Where-Object status -EQ 'FAIL').Count -eq 0;simulatorContacted=$false;networkContacted=$false;checks=$checks.ToArray()}
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$reportPath=Join-Path $ReportDirectory ('setup-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6)+'.json')
[IO.File]::WriteAllText($reportPath,($report | ConvertTo-Json -Depth 8)+[Environment]::NewLine)
if ($PassThru) { return $report }
foreach ($check in $checks) { Write-Host ('['+$check.status+'] '+$check.name+': '+$check.detail) }
Write-Host ('Report: '+$reportPath)
if (-not $report.passed) { throw 'Setup check failed. Resolve the named items before pressing Join shared session.' }
if($Mode -eq 'Files'){Write-Host 'File check passed. Running-helper, simulator and relay readiness have not been checked.'}
else{Write-Host 'Local file/process check passed. Continue the live checklist in START HERE.txt.'}
