param([string]$KitRoot=$PSScriptRoot,[string]$Destination='')
$ErrorActionPreference='Stop'
$KitRoot=[IO.Path]::GetFullPath($KitRoot)
if(-not $Destination){$Destination=Join-Path $KitRoot 'Reports'}
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$check=$null
try { $check=& (Join-Path $KitRoot 'Check setup.ps1') -KitRoot $KitRoot -ReportDirectory $Destination -PassThru }
catch { [IO.File]::WriteAllText((Join-Path $Destination 'setup-collection-error.txt'),$_.Exception.Message) }
$oldDestination=$env:FSC_EFB_DIAGNOSTICS_DESTINATION
try {
    $env:FSC_EFB_DIAGNOSTICS_DESTINATION=$Destination
    & (Join-Path $KitRoot 'Helper/Collect logs.cmd')
    if($LASTEXITCODE -ne 0){throw 'The helper log collector failed.'}
} finally { $env:FSC_EFB_DIAGNOSTICS_DESTINATION=$oldDestination }
$zip=Get-ChildItem -LiteralPath $Destination -Filter 'FsCopilot-diagnostics-*.zip' -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if(-not $zip){throw 'No diagnostics ZIP was created.'}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::Open($zip.FullName,[IO.Compression.ZipArchiveMode]::Update)
try {
    $files=@((Join-Path $KitRoot 'test-kit.json'),(Join-Path $KitRoot 'TEST RESULTS.txt'))
    $files+=@(Get-ChildItem -LiteralPath $Destination -Filter 'setup-*.json' -File | Sort-Object LastWriteTime -Descending | Select-Object -First 3 | Select-Object -ExpandProperty FullName)
    foreach($file in $files){if(Test-Path -LiteralPath $file -PathType Leaf){
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file,('Test kit/'+(Split-Path -Leaf $file)),[IO.Compression.CompressionLevel]::Optimal)
    }}
} finally {$archive.Dispose()}
Write-Host ('Send this ZIP with the failed test number: '+$zip.FullName)
Write-Host 'Collection is local only. No simulator commands, saved settings or uploads.'
