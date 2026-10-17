# Windows PowerShell 5.1 adapter; the original v024 package stays byte-identical.
$ErrorActionPreference='Stop'

function Test-BO6OriginalPackage([string]$Package) {
    $expected=@{
        'BO6_Personal_Progress.ps1'='41223b26c04ebcdda2a9f4049638ba1d54bdbc20c17f5e9ef5ff6b8934c3f13e'
        'patch-spec.json'='40e62f7eab7fa5a8f4e03eb0dc106f8ccf3fd3ec64488de0cef0a1d3e5e73800'
        'source/CaptionFixture.cs'='7f6d8c6626673b725d7dd5baf8f6384d12b2088be400b539a83c1e93fa71ceac'
        'source/CaptionNativePreflight.cs'='04583a4831d370f689959d87c062e7b093bce24b8ad676e307cc2352078aded1'
        'source/CaptionTimeout.cs'='296c3bcf30c9de6150ddbec36fd8c5c52d71cc2ea8eda8bc69b9b5006bac4e40'
        'source/CheckpointBridge.cs'='f12eaee03db18010354f40c9e1e790f7d95450918c02577e94ea734ebe028030'
        'source/Preflight_Process.ps1'='e2a86aee0c24534cd2f1559e951597db264ad4d8a208a66c07aca5c0e2cc8eed'
        'source/Progression.cs'='739306bcf2f6b3231883f38961f927d3dc7d5516890dcf3d15243bf3dec431e8'
        'source/Run_Preflight.ps1'='be20757ee872d1dbf3e6ca7ebd0047ba38f3036e7d0cd4501c92229c79e4751b'
        'source/SelfTest.cs'='25049c52fc1af092680fdc9e884a020dfc088bf37ab816bf8516cb412a1f507f'
        'source/StartupProgression.cs'='61da92ad7bc7d01dda8f154588cab9216f4b23a79fad2f9a9e9cbac77e9d763c'
    }
    foreach ($relative in $expected.Keys) {
        $path=Join-Path $Package $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {throw ('Missing original v024 package file: '+$relative)}
        $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $expected[$relative]) {throw ('Original v024 package file differs: '+$relative+'. Use the complete matching package; do not replace saved progress.')}
    }
}

function Invoke-BO6OriginalScript([string]$Script,[string]$GameRoot) {
    function Read-Host {
        [CmdletBinding()]
        param([string]$Prompt)
        if ($Prompt -cne 'Press ENTER to close this window') {
            throw ('Unexpected interactive prompt; not automatically accepted: '+$Prompt)
        }
        # Only this exact, hash-verified v024 terminal prompt is omitted.
        # An error still exits1 and the EXE retains its error dialog and logs.
    }
    $global:LASTEXITCODE=0
    & $Script -GameRoot $GameRoot | Out-Host
    if ($null -eq $global:LASTEXITCODE -or $global:LASTEXITCODE -isnot [int]) {throw 'Original script returned an invalid process exit status'}
    return $global:LASTEXITCODE
}

function Start-BO6FromExe {
    try {
        $principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {throw 'The EXE adapter requires the elevated child; game not launched'}
        if (-not [Environment]::Is64BitProcess) {throw '64-bit Windows PowerShell is required'}
        $package=Split-Path -Parent $PSScriptRoot
        Test-BO6OriginalPackage $package
        $script=Join-Path $package 'BO6_Personal_Progress.ps1'
        $root=Split-Path -Parent $package
        return (Invoke-BO6OriginalScript $script $root)
    } catch {
        Write-Host ('EXE adapter error: '+$_.Exception.Message) -ForegroundColor Red
        return 1
    }
}

if ($MyInvocation.InvocationName -ne '.') {exit (Start-BO6FromExe)}
