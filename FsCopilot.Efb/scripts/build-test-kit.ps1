param(
    [string]$CandidateRoot='',
    [string]$Stage='',
    [switch]$Freeze
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if(-not $CandidateRoot){$CandidateRoot=Join-Path $repo 'artifacts/rejoin-command-scope'}
if(-not $Stage){$Stage=Join-Path $repo 'artifacts/rejoin-command-scope/test-kit'}
$CandidateRoot=[IO.Path]::GetFullPath($CandidateRoot);$Stage=[IO.Path]::GetFullPath($Stage)
$source=Join-Path $CandidateRoot 'bundle';$bundle=Join-Path $Stage 'TestKit'
$zipPath=Join-Path $Stage 'fscopilot-0.9.12-two-player-test.zip'
if(Test-Path -LiteralPath $zipPath){throw 'The test ZIP is already frozen. Do not overwrite it.'}
$candidate=Get-Content -LiteralPath (Join-Path $CandidateRoot 'candidate.json') -Raw | ConvertFrom-Json
if($candidate.version -ne '0.9.12'){throw 'Expected the frozen 0.9.12 candidate.'}
New-Item -ItemType Directory -Path $bundle -Force | Out-Null
$paths=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($payload in $candidate.payloads){
    $inputPath=Join-Path $source $payload.path
    if((Get-FileHash -LiteralPath $inputPath).Hash -ne $payload.sha256){throw ('Original candidate changed: '+$payload.path)}
    $outputPath=Join-Path $bundle $payload.path
    New-Item -ItemType Directory -Path (Split-Path -Parent $outputPath) -Force | Out-Null
    Copy-Item -LiteralPath $inputPath -Destination $outputPath -Force
    [void]$paths.Add($payload.path)
}
$tools=Join-Path (Split-Path -Parent $PSScriptRoot) 'TestKit'
foreach($file in Get-ChildItem -LiteralPath $tools -File){
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $bundle $file.Name) -Force
    [void]$paths.Add($file.Name)
}
Copy-Item -LiteralPath (Join-Path $bundle 'START HERE.txt') -Destination (Join-Path $bundle 'README.txt') -Force
$payloads=@(foreach($path in $paths | Sort-Object){
    if($path -eq 'TEST RESULTS.txt'){continue} # Deliberately editable; not a protected program file.
    $file=Get-Item -LiteralPath (Join-Path $bundle $path)
    [pscustomobject]@{path=$path;bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName).Hash}
})
$manifest=[pscustomobject]@{schema=1;version='0.9.12';sourceZipSha256=$candidate.sha256;mutableFiles=@('TEST RESULTS.txt','Reports/');payloads=$payloads;simulatorValidation='0.9.12 scopes Windows cockpit/AP send counters and retired-drop notifications to the current session binding. Production-client tests keep the SDK connection alive across rejoins; SDK I/O is simulated. Includes 0.9.11 control-value isolation. Actual two-PC MSFS rejoin/engine-start validation pending; saved setup and rates unchanged.'}
[IO.File]::WriteAllText((Join-Path $bundle 'test-kit.json'),($manifest | ConvertTo-Json -Depth 8)+[Environment]::NewLine)
Write-Output ('Prepared matching 0.9.12 test kit: '+$bundle)
if(-not $Freeze){return}
Add-Type -AssemblyName System.IO.Compression
$files=@($payloads.path)+@('test-kit.json','TEST RESULTS.txt')
$stream=[IO.File]::Open($zipPath,[IO.FileMode]::CreateNew)
$archive=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create,$false)
try{foreach($path in $files){
    $entry=$archive.CreateEntry($path,[IO.Compression.CompressionLevel]::Optimal)
    $output=$entry.Open();$input=[IO.File]::OpenRead((Join-Path $bundle $path))
    try{$input.CopyTo($output)}finally{$input.Dispose();$output.Dispose()}
}}finally{$archive.Dispose();$stream.Dispose()}
$stream=[IO.File]::OpenRead($zipPath);$archive=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Read,$false)
try{
    if($archive.Entries.Count -ne $files.Count){throw 'ZIP entry count mismatch.'}
    foreach($path in $files){
        $entry=$archive.GetEntry($path);if(-not $entry){throw ('ZIP entry missing: '+$path)}
        $input=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
        try{$hash=[BitConverter]::ToString($sha.ComputeHash($input)).Replace('-','')}finally{$input.Dispose();$sha.Dispose()}
        if($hash -ne (Get-FileHash -LiteralPath (Join-Path $bundle $path)).Hash){throw ('ZIP hash mismatch: '+$path)}
    }
}finally{$archive.Dispose();$stream.Dispose()}
$receipt=[pscustomobject]@{version='0.9.12';timestampUtc=[DateTimeOffset]::UtcNow.ToString('o');zip=$zipPath;bytes=(Get-Item -LiteralPath $zipPath).Length;sha256=(Get-FileHash -LiteralPath $zipPath).Hash;entries=$files.Count;sourceCandidateSha256=$candidate.sha256;unchangedBinaries=$true;twoPcSimulatorValidationPending=$true}
[IO.File]::WriteAllText((Join-Path $Stage 'test-kit.json'),($receipt | ConvertTo-Json)+[Environment]::NewLine)
Write-Output ('Frozen verified test ZIP: '+$receipt.bytes+' bytes; SHA256 '+$receipt.sha256)
