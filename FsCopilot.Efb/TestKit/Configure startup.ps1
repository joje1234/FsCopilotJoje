param(
    [ValidateSet('Install','Remove')][string]$Mode='Install',
    [string]$HelperPath='',
    [string]$ExeXmlPath='',
    [switch]$ValidateOnly
)
$ErrorActionPreference='Stop'
if(Get-Process FlightSimulator* -ErrorAction SilentlyContinue){throw 'Fully close MSFS before changing its automatic startup configuration.'}
if(-not $ExeXmlPath){
    $folders=@((Join-Path $env:APPDATA 'Microsoft Flight Simulator 2024'),
        (Join-Path $env:LOCALAPPDATA 'Packages/Microsoft.Limitless_8wekyb3d8bbwe/LocalCache'))
    $choices=@($folders | Where-Object {Test-Path -LiteralPath (Join-Path $_ 'exe.xml') -PathType Leaf})
    if(-not $choices.Count){$choices=@($folders | Where-Object {Test-Path -LiteralPath (Join-Path $_ 'UserCfg.opt') -PathType Leaf})}
    if(-not $choices.Count){$choices=@($folders | Where-Object {Test-Path -LiteralPath $_ -PathType Container})}
    if($choices.Count -ne 1){throw 'Cannot uniquely identify the MSFS 2024 configuration. Pass -ExeXmlPath with the active simulator exe.xml path.'}
    $ExeXmlPath=Join-Path $choices[0] 'exe.xml'
}
$ExeXmlPath=[IO.Path]::GetFullPath($ExeXmlPath)
if([IO.Path]::GetFileName($ExeXmlPath) -ine 'exe.xml'){throw 'The target configuration must be named exe.xml.'}
if($Mode -eq 'Install'){
    if(-not $HelperPath){$HelperPath=Join-Path $PSScriptRoot 'Helper/FsCopilot.Efb.exe'}
    $HelperPath=[IO.Path]::GetFullPath($HelperPath)
    if([IO.Path]::GetFileName($HelperPath) -ine 'FsCopilot.Efb.exe' -or -not (Test-Path -LiteralPath $HelperPath -PathType Leaf)){
        throw 'Select the extracted matching Helper/FsCopilot.Efb.exe, outside the ZIP.'
    }
    $version=[Diagnostics.FileVersionInfo]::GetVersionInfo($HelperPath)
    if($version.FileMajorPart -ne 0 -or $version.FileMinorPart -ne 9 -or $version.FileBuildPart -ne 12){throw 'Automatic startup requires this kit matching helper 0.9.12.'}
    if(-not (Test-Path -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($HelperPath)) 'Definitions/controls/bksq-aircraft-baronpropress.json'))){throw 'Keep the complete Definitions folder beside the helper.'}
}
function Load-Launch([string]$Path){
    $doc=New-Object Xml.XmlDocument;$doc.PreserveWhitespace=$true;$doc.XmlResolver=$null
    $settings=New-Object Xml.XmlReaderSettings;$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null
    $reader=[Xml.XmlReader]::Create($Path,$settings)
    try{$doc.Load($reader)}finally{$reader.Dispose()}
    return ,$doc
}
function Value($Node,[string]$Name){
    $child=$Node.SelectSingleNode($Name)
    if($child){return $child.InnerText}
    return ''
}
function Is-Helper($Node){
    return (Value $Node 'Name') -eq 'FS Copilot EFB' -or [IO.Path]::GetFileName((Value $Node 'Path')) -ieq 'FsCopilot.Efb.exe'
}
$exists=Test-Path -LiteralPath $ExeXmlPath -PathType Leaf
if($exists){$doc=Load-Launch $ExeXmlPath}
else{
    $doc=New-Object Xml.XmlDocument;$doc.PreserveWhitespace=$true;$doc.XmlResolver=$null
    $doc.LoadXml('<?xml version="1.0" encoding="utf-8"?><SimBase.Document Type="Launch" version="1,0"><Descr>Launch</Descr><Filename>exe.xml</Filename><Disabled>False</Disabled><Launch.ManualLoad>False</Launch.ManualLoad></SimBase.Document>')
}
$root=$doc.DocumentElement
if($root.LocalName -ne 'SimBase.Document' -or $root.NamespaceURI -ne '' -or $root.GetAttribute('Type') -ne 'Launch'){throw 'The existing XML is not a supported simulator launch configuration. Nothing changed.'}
if($Mode -eq 'Install' -and ([string]$root.Disabled -match '^\s*true\s*$' -or [string]$root.'Launch.ManualLoad' -match '^\s*true\s*$')){
    throw 'MSFS automatic launch is globally disabled/manual. Nothing changed; enabling all unrelated addons is outside this setup.'
}
$addons=@($root.SelectNodes('Launch.Addon'))
$owned=@($addons | Where-Object {Is-Helper $_})
$other=@($addons | Where-Object {-not (Is-Helper $_)} | ForEach-Object OuterXml)
$same=$Mode -eq 'Remove' -and -not $owned.Count
if($Mode -eq 'Install' -and $owned.Count -eq 1){
    $same=(Value $owned[0] 'Path') -ieq $HelperPath -and (Value $owned[0] 'Name') -eq 'FS Copilot EFB' -and
        (Value $owned[0] 'Disabled') -ieq 'False' -and (Value $owned[0] 'NewConsole') -ieq 'False' -and
        (Value $owned[0] 'ManualLoad') -ieq 'False' -and (Value $owned[0] 'CommandLine') -eq ''
}
if($same){return [pscustomobject]@{Mode=$Mode;Path=$ExeXmlPath;Helper=$HelperPath;Changed=$false;Backup=''}}
foreach($node in $owned){[void]$root.RemoveChild($node)}
if($Mode -eq 'Install'){
    $entry=$doc.CreateElement('Launch.Addon')
    foreach($pair in @(@('Name','FS Copilot EFB'),@('Disabled','False'),@('ManualLoad','False'),@('Path',$HelperPath),@('NewConsole','False'))){
        $node=$doc.CreateElement($pair[0]);$node.InnerText=$pair[1];[void]$entry.AppendChild($node)
    }
    [void]$root.AppendChild($entry)
}
if($ValidateOnly){return [pscustomobject]@{Mode=$Mode;Path=$ExeXmlPath;Helper=$HelperPath;Changed=$false;Backup='';Validated=$true}}
$folder=[IO.Path]::GetDirectoryName($ExeXmlPath)
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$stamp=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6)
$temporary=Join-Path $folder ('exe.xml.fscopilot-'+$stamp+'.tmp')
$backup=if($exists){Join-Path $folder ('exe.xml.fscopilot-'+$stamp+'.bak')}else{''}
$settings=New-Object Xml.XmlWriterSettings;$settings.Encoding=New-Object Text.UTF8Encoding($false);$settings.Indent=$false
$writer=[Xml.XmlWriter]::Create($temporary,$settings)
try{$doc.Save($writer)}finally{$writer.Dispose()}
$verified=Load-Launch $temporary
$newOther=@($verified.DocumentElement.SelectNodes('Launch.Addon') | Where-Object {-not (Is-Helper $_)} | ForEach-Object OuterXml)
if(($other -join "`0") -cne ($newOther -join "`0")){throw 'Unrelated launch entries changed; original configuration is untouched.'}
if($exists){[IO.File]::Replace($temporary,$ExeXmlPath,$backup)}else{[IO.File]::Move($temporary,$ExeXmlPath)}
[pscustomobject]@{Mode=$Mode;Path=$ExeXmlPath;Helper=$HelperPath;Changed=$true;Backup=$backup}
