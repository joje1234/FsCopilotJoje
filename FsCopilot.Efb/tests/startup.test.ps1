param([Parameter(Mandatory=$true)][string]$HelperPath)
$ErrorActionPreference='Stop'
$configure=Join-Path (Split-Path -Parent $PSScriptRoot) 'TestKit/Configure startup.ps1'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('fscopilot-startup-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$target=Join-Path $fixture 'exe.xml'
function Check($Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Read-Xml { $doc=New-Object Xml.XmlDocument;$doc.PreserveWhitespace=$true;$doc.Load($target);return ,$doc }
function Reject([scriptblock]$Action){$rejected=$false;try{& $Action | Out-Null}catch{$rejected=$true};Check $rejected 'Unsafe configuration was accepted.'}
function Hash { (Get-FileHash -LiteralPath $target).Hash }
$original='<?xml version="1.0"?><SimBase.Document Type="Launch" version="1,0"><Disabled>False</Disabled><Launch.ManualLoad>False</Launch.ManualLoad><!-- preserve --><Launch.Addon><Name>Vanilla</Name><Path>C:\Apps\FsCopilot.exe</Path></Launch.Addon><Launch.Addon><Name>Another addon</Name><Path>C:\Other &amp; Tools\App.exe</Path><CommandLine>keep this</CommandLine></Launch.Addon><Launch.Addon><Name>Old EFB</Name><Path>C:\Old\FsCopilot.Efb.exe</Path></Launch.Addon><Launch.Addon><Name>FS Copilot EFB</Name><Path>C:\Prior\FsCopilot.Efb.exe</Path></Launch.Addon></SimBase.Document>'
[IO.File]::WriteAllText($target,$original)
$before=Hash
$validated=& $configure -HelperPath $HelperPath -ExeXmlPath $target -ValidateOnly
Check ((Hash) -eq $before -and $validated.Validated) 'ValidateOnly changed the configuration.'
$result=& $configure -HelperPath $HelperPath -ExeXmlPath $target
Check $result.Changed 'Installation did not change the fixture.'
Check ((Get-FileHash -LiteralPath $result.Backup).Hash -eq $before) 'Backup is not byte-identical.'
$doc=Read-Xml;$addons=@($doc.SelectNodes('/SimBase.Document/Launch.Addon'))
Check ($addons.Count -eq 3) 'Existing EFB entries were not deduplicated.'
Check ($addons[0].OuterXml -match 'FsCopilot.exe' -and $addons[1].OuterXml -match 'keep this') 'Unrelated addons changed.'
$entry=$addons[2]
Check ($entry.SelectSingleNode('Path').InnerText -eq [IO.Path]::GetFullPath($HelperPath)) 'Wrong helper path.'
Check ($entry.SelectSingleNode('ManualLoad').InnerText -eq 'False') 'Automatic loading is not explicit.'
Check ($doc.OuterXml.Contains('<!-- preserve -->')) 'Existing comments lost.'
$installed=Hash
$again=& $configure -HelperPath $HelperPath -ExeXmlPath $target
Check (-not $again.Changed -and (Hash) -eq $installed) 'Repeated install is not idempotent.'
$removed=& $configure -Mode Remove -ExeXmlPath $target
Check ($removed.Changed -and @((Read-Xml).SelectNodes('/SimBase.Document/Launch.Addon')).Count -eq 2) 'Removal affected unrelated addons.'
$again=& $configure -Mode Remove -ExeXmlPath $target
Check (-not $again.Changed) 'Repeated removal is not idempotent.'
foreach($xml in @('<broken>', $original.Replace('<Disabled>False','<Disabled>True'),$original.Replace('<Launch.ManualLoad>False','<Launch.ManualLoad>True'))){
    [IO.File]::WriteAllText($target,$xml);$before=Hash
    Reject {& $configure -HelperPath $HelperPath -ExeXmlPath $target}
    Check ((Hash) -eq $before) 'Rejected configuration was modified.'
}
$newTarget=Join-Path $fixture 'new config & spaces/exe.xml'
& $configure -HelperPath $HelperPath -ExeXmlPath $newTarget -ValidateOnly | Out-Null
Check (-not (Test-Path -LiteralPath $newTarget)) 'ValidateOnly created a file.'
$created=& $configure -HelperPath $HelperPath -ExeXmlPath $newTarget
Check ($created.Changed -and (Test-Path -LiteralPath $newTarget)) 'New configuration was not created.'
Write-Output 'PASS startup configuration: install/deduplication, exact backup, unrelated/vanilla entries, idempotence, removal, validation-only, malformed/disabled refusal and new configuration. Temporary fixtures only; no simulator changes.'
Write-Output ('Fixtures: '+$fixture)
