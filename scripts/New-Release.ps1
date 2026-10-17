param([string]$OutputDirectory='')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) {$OutputDirectory=Join-Path $repo 'build'}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$null=New-Item -ItemType Directory -Path $OutputDirectory -Force
$stage=Join-Path $OutputDirectory ('stage_'+[Guid]::NewGuid().ToString('N'))
$package=Join-Path $stage 'BO6_Personal_Progress'
$null=New-Item -ItemType Directory -Path $package
$source=Join-Path $repo 'BO6_Personal_Progress'
$manifest=Get-Content -LiteralPath (Join-Path $repo 'runtime-files.json') -Encoding UTF8 -Raw | ConvertFrom-Json
# Explicit tracked-file allowlist: never copy Data, saves, logs, arbitrary binaries or local experiments.
foreach ($file in $manifest.Files) {
    $relative=[string]$file.Path
    if ($relative -match '(^/|^[A-Za-z]:|(^|/)\.\.(/|$)|\\)' -or $relative -match '(?i)(^|/)(Data|logs|backups|BO6_Personal_Progress_Data)(/|$)' -or $relative -match '(?i)\.(exe|dll|sys|bin|zip|bo6profile|bo6local)$') {throw ('Unsafe release input: '+$relative)}
    $runtimeSourcePath=Join-Path $source $relative
    if ((Get-FileHash -LiteralPath $runtimeSourcePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$file.SHA256) {throw ('Runtime source differs: '+$relative)}
    $target=Join-Path $package $relative
    $null=New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force
    [IO.File]::Copy($runtimeSourcePath,$target,$false)
}
& (Join-Path $PSScriptRoot 'Build-Launcher.ps1') -OutputDirectory $package
foreach ($name in @('README.md','README.ru.md','LICENSE','THIRD_PARTY_NOTICES.md')) {[IO.File]::Copy((Join-Path $repo $name),(Join-Path $stage $name),$false)}
$zip=Join-Path $OutputDirectory 'BO6_Personal_Progress_EXE.zip'
Compress-Archive -LiteralPath @((Join-Path $stage 'BO6_Personal_Progress'),(Join-Path $stage 'README.md'),(Join-Path $stage 'README.ru.md'),(Join-Path $stage 'LICENSE'),(Join-Path $stage 'THIRD_PARTY_NOTICES.md')) -DestinationPath $zip -CompressionLevel Optimal -Force
Write-Host ('Release ZIP: '+$zip)
Get-FileHash -LiteralPath $zip -Algorithm SHA256 | Format-List
