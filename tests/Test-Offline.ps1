$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$package=Join-Path $repo 'BO6_Personal_Progress'
foreach ($file in Get-ChildItem -LiteralPath $repo -Recurse -Filter '*.ps1') {
    $tokens=$null;$parseErrors=$null
    $null=[Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$parseErrors)
    if ($parseErrors.Count) {throw ('PowerShell parse failed: '+$file.FullName+': '+($parseErrors | Out-String))}
}
. (Join-Path $package 'launcher_source/Start_From_Exe.ps1')
Test-BO6OriginalPackage $package
$manifest=Get-Content -LiteralPath (Join-Path $repo 'runtime-files.json') -Encoding UTF8 -Raw | ConvertFrom-Json
foreach ($file in $manifest.Files) {
    if ((Get-FileHash -LiteralPath (Join-Path $package $file.Path) -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$file.SHA256) {throw ('Runtime manifest differs: '+$file.Path)}
}
$sources=@('CheckpointBridge.cs','Progression.cs','SelfTest.cs','StartupProgression.cs','CaptionTimeout.cs','CaptionFixture.cs','CaptionNativePreflight.cs') | ForEach-Object {Join-Path (Join-Path $package 'source') $_}
Add-Type -Path $sources
[BO6ProfileSelfTest]::Run()
[BO6CaptionFixture]::Validate()
Write-Host 'PASS parser, 11 original runtime hashes, C# compilation, profile/checksum and caption format/hash self-tests'
Write-Host 'Game access=false; no native preflight execution, process patching, game launch or personal save read.'
