param([Parameter(Mandatory=$true)][string]$OutputDirectory,[Parameter(Mandatory=$true)][ValidatePattern('^[a-f0-9]{32}$')][string]$RunToken)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
try {
    if (-not [Environment]::Is64BitProcess) {throw 'Preflight requires64-bitPowerShell'}
    $sources=@('CheckpointBridge.cs','Progression.cs','SelfTest.cs','StartupProgression.cs','CaptionTimeout.cs','CaptionFixture.cs','CaptionNativePreflight.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
    Add-Type -Path $sources
    $count=[BO6CaptionNativePreflight]::Run()
    [pscustomobject]@{RunToken=$RunToken;Passed=$true;NativeCases=$count;Platform='Windows own-process leaf execution';GameAccess=$false;CaptionFixtureSHA256=[BO6CaptionFixture]::PatchedHash;LeafSHA256=[BO6CaptionFixture]::LeafHash} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'preflight.json') -Encoding UTF8
    Write-Output ('PASS nativecaptionpreflight cases='+$count+';gameaccess=false')
    exit 0
} catch {
    $_.Exception.ToString() | Set-Content -LiteralPath (Join-Path $OutputDirectory 'preflight-error.txt') -Encoding UTF8
    [Console]::Error.WriteLine($_.Exception.ToString())
    exit 1
}
