param([string]$OutputDirectory='')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$package=Join-Path $repo 'BO6_Personal_Progress'
if (-not $OutputDirectory) {$OutputDirectory=$package}
$null=New-Item -ItemType Directory -Path $OutputDirectory -Force
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $csc -PathType Leaf)) {throw 'Windows x64 .NET Framework 4.x csc.exe is required'}
$source=Join-Path $package 'launcher_source/Launcher.cs'
$manifest=Join-Path $package 'launcher_source/Launcher.manifest'
$output=Join-Path $OutputDirectory 'Run_BO6_Personal_Progress.exe'
& $csc /nologo /warnaserror+ /target:winexe /platform:x64 /optimize+ (('/win32manifest:{0}' -f $manifest)) (('/out:{0}' -f $output)) $source
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $output -PathType Leaf)) {throw 'Launcher compilation failed'}
$config=Join-Path $package 'Run_BO6_Personal_Progress.exe.config'
$configOutput=Join-Path $OutputDirectory 'Run_BO6_Personal_Progress.exe.config'
if (-not [string]::Equals([IO.Path]::GetFullPath($config),[IO.Path]::GetFullPath($configOutput),[StringComparison]::OrdinalIgnoreCase)) {[IO.File]::Copy($config,$configOutput,$true)}
Write-Host ('Built Windows x64 .NET4 GUI launcher: '+$output)
Write-Host 'This reproducible build procedure uses your Windows compiler; byte-for-byte output can differ by compiler version/timestamp.'
