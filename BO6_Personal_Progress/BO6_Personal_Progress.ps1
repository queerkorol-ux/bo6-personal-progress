param([string]$GameRoot = (Split-Path -Parent $PSScriptRoot))
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
$ErrorActionPreference = 'Stop'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    try {
        $ps = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $arguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -GameRoot "{1}"' -f $PSCommandPath,$GameRoot
        Start-Process -FilePath $ps -ArgumentList $arguments -Verb RunAs | Out-Null
        exit 0
    } catch { Write-Host $_.Exception.Message -ForegroundColor Red; Read-Host 'ENTER' | Out-Null; exit 1 }
}
$bridge=$null; $mutex=$null; $ownsMutex=$false; $transcribing=$false; $runDir=$null; $installed=$false; $failed=$false
try {
    if (-not [Environment]::Is64BitProcess) { throw 'Use Run_BO6_Personal_Progress.cmd (64-bit PowerShell is required).' }
    $GameRoot=(Resolve-Path -LiteralPath $GameRoot).Path.TrimEnd('\')
    $exe=Join-Path $GameRoot 'sp24\sp24-cod.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Put the BO6_Personal_Progress folder inside the game root, beside !start_sp24.bat.' }
    $mutex=New-Object Threading.Mutex($false,'Local\BO6_Local_Checkpoints_v1')
    try { $ownsMutex=$mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $ownsMutex=$true }
    if (-not $ownsMutex) { throw 'The checkpoint bridge is already running. Keep the first window open.' }
    $dataDir=Join-Path $GameRoot 'BO6_Personal_Progress_Data'
    New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
    $runId=(Get-Date -Format 'yyyyMMdd_HHmmss')+'_'+[Guid]::NewGuid().ToString('N').Substring(0,6)
    $runDir=Join-Path $dataDir ('logs\'+$runId)
    New-Item -ItemType Directory -Path $runDir -Force | Out-Null
    Start-Transcript -Path (Join-Path $runDir 'console.txt') | Out-Null
    $transcribing=$true
    Write-Host 'BO6 ЛИЧНЫЙ ПРОГРЕСС — ЭКСПЕРИМЕНТАЛЬНЫЙ ПАТЧ 0.2.4 — СКРЫТИЕ НАДПИСИ ЧЕРЕЗ 2 СЕКУНДЫ' -ForegroundColor Cyan
    Write-Host 'This changes save-related code in the running game. EXE/DLL files stay unchanged.'
    Write-Host 'Отдельный профиль: свои контрольные точки, деньги и прогресс.'
    Write-Host 'Анимация сохранения остаётся; только эта надпись скрывается примерно через 2 секунды. Это таймер видимости, не подтверждение записи на диск.' -ForegroundColor Yellow
    Write-Host 'Восстановление обычных условий доступа и локального сохранения. Полное прохождение не проверено.' -ForegroundColor Yellow
    Write-Host 'Keep this window open while playing. Close BO6 normally before closing it.'
    $spec=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'patch-spec.json') -Raw | ConvertFrom-Json
    $exeHash=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($exeHash -ne $spec.ExeSHA256) { throw 'EXE differs from the examined build. Nothing was patched.' }
    $sources=@('CheckpointBridge.cs','Progression.cs','SelfTest.cs','StartupProgression.cs','CaptionTimeout.cs','CaptionFixture.cs','CaptionNativePreflight.cs') | ForEach-Object {Join-Path (Join-Path $PSScriptRoot 'source') $_}
    $sources | ForEach-Object {Get-FileHash -LiteralPath $_ -Algorithm SHA256 | Select-Object @{Name='File';Expression={Split-Path -Leaf $_.Path}},Hash} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runDir 'source-manifest.json') -Encoding UTF8
    Add-Type -Path $sources
    [BO6ProfileSelfTest]::Run()
    Write-Host 'Проверка формата профиля и контрольных сумм пройдена.'
    # An isolated child validates fixture hashes and executes the leaf against its own allocations.
    $preflightScript=Join-Path $PSScriptRoot 'source\Run_Preflight.ps1'
    . (Join-Path $PSScriptRoot 'source\Preflight_Process.ps1')
    $null=Invoke-BO6PreflightProcess -Executable (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -ScriptPath $preflightScript -OutputDirectory $runDir -CaptionHash ([BO6CaptionFixture]::PatchedHash) -LeafHash ([BO6CaptionFixture]::LeafHash)
    Write-Host 'Автоматическая проверка Lua-фикстур и машинного обработчика пройдена в отдельном процессе.' -ForegroundColor Green
    $profilePath=Join-Path $dataDir 'campaign.bo6profile'
    $isNewProfile=-not (Test-Path -LiteralPath $profilePath)
    if ($isNewProfile) {
        if (@(Get-ChildItem -LiteralPath $dataDir -Filter '*.bo6local').Count -gt 0) { throw 'Profile file is missing but checkpoints exist. Restore campaign.bo6profile from backup; no automatic reset.' }
        $profiles=[BO6CheckpointBridge]::NewProfile()
    } else { $profiles=[BO6CheckpointBridge]::ReadProfiles($profilePath,$exeHash) }
    $profileSaved=$profiles.Hash; $profileMisses=0; $lastProfileWrite=(Get-Date).AddSeconds(-10)

    $restores=New-Object 'System.Collections.Generic.List[BO6CheckpointBridge+Restore]'
    $lastSaved=@{}; $candidate=@{}
    for ($slot=1; $slot -le 3; $slot++) {
        $path=Join-Path $dataDir ('slot{0}.bo6local' -f $slot)
        if (Test-Path -LiteralPath $path) {
            [byte[]]$raw=[BO6CheckpointBridge]::ReadFile($path,$exeHash,$slot)
            $r=New-Object 'BO6CheckpointBridge+Restore'; $r.Slot=$slot; $r.Raw=$raw
            $restores.Add($r); $lastSaved[$slot]=[BO6CheckpointBridge]::Hash($raw)
        }
    }
    function Find-BO6 {
        $found=@(Get-Process -Name 'sp24-cod' -ErrorAction SilentlyContinue | Where-Object {
            [string]::Equals($_.Path,$exe,[StringComparison]::OrdinalIgnoreCase)
        })
        if ($found.Count -gt 1) { throw 'More than one BO6 process was found.' }
        if ($found.Count -eq 1) { return $found[0] }
        return $null
    }
    $game=Find-BO6
    if ($game) { throw 'Сначала закрой BO6 обычным способом и старые окна патчей. Затем снова запусти этот CMD.' }
    # Snapshot only fixed progress files before launching or installing anything in BO6.
    $backupDir=Join-Path $dataDir ('backups\v023_'+$runId)
    if (Test-Path -LiteralPath $backupDir) {throw 'Каталог новой резервной копии уже существует; его содержимое не изменено.'}
    New-Item -ItemType Directory -Path $backupDir | Out-Null
    $backupEntries=@()
    foreach ($backupName in @('campaign.bo6profile','campaign.bo6profile.bak','slot1.bo6local','slot1.bo6local.bak','slot2.bo6local','slot2.bo6local.bak','slot3.bo6local','slot3.bo6local.bak')) {
        $backupSource=Join-Path $dataDir $backupName
        if (Test-Path -LiteralPath $backupSource -PathType Leaf) {
            $beforeHash=(Get-FileHash -LiteralPath $backupSource -Algorithm SHA256).Hash
            $backupTarget=Join-Path $backupDir $backupName
            [IO.File]::Copy($backupSource,$backupTarget,$false)
            $copyHash=(Get-FileHash -LiteralPath $backupTarget -Algorithm SHA256).Hash
            $afterHash=(Get-FileHash -LiteralPath $backupSource -Algorithm SHA256).Hash
            if ($beforeHash -ne $copyHash -or $beforeHash -ne $afterHash) {throw 'Резервная копия не прошла проверку; игра не запущена, исходные данные не изменены.'}
            $backupEntries+= [pscustomobject]@{File=$backupName;SHA256=$copyHash.ToLowerInvariant();Bytes=(Get-Item -LiteralPath $backupTarget).Length}
        }
    }
    [pscustomobject]@{Directory=$backupDir;Scope='fixed_progress_files_only';Files=$backupEntries} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runDir 'backup-manifest.json') -Encoding UTF8
    Write-Host ('Резервная копия прогресса проверена: '+$backupDir) -ForegroundColor Green
    if (-not $game) {
        $launcher=Join-Path $GameRoot '!start_sp24.bat'
        if (-not (Test-Path -LiteralPath $launcher)) { throw 'Original !start_sp24.bat was not found. Put this package folder beside the original launcher.' }
        Write-Host 'Starting the original game launcher...'
        Start-Process -FilePath $launcher -WorkingDirectory $GameRoot | Out-Null
    }
    Write-Host 'Ранний этап готовится автоматически до загрузки safehouse Lua. Не запускай миссию до надписи ПАТЧ АКТИВЕН.' -ForegroundColor Yellow
    Write-Host 'Waiting for the game and checkpoint storage (up to 10 minutes)...'
    $deadline=(Get-Date).AddMinutes(10)
    $ready=$false; $earlyDeadline=$null; $earlyLogged=$false
    $lastStartupNotice=(Get-Date).AddSeconds(-20)
    while ((Get-Date) -lt $deadline) {
        if (-not $game) { $game=Find-BO6 }
        if ($game) {
            $game.Refresh()
            if ($game.HasExited) { throw 'BO6 exited before installation.' }
            if (-not $bridge) {
                [long]$image=$game.MainModule.BaseAddress.ToInt64()
                $earlyDeadline=(Get-Date).AddSeconds(60)
                $bridge=New-Object BO6CheckpointBridge($game.Id,$image)
                [pscustomobject]@{ProcessId=$game.Id;ImageBase=('0x{0:X}' -f $image);ExeSHA256=$exeHash;Stage='Waiting'} |
                    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runDir 'startup.json') -Encoding UTF8
            }
            if (-not $bridge.EarlyProgressionPrepared) {
                [long]$dllBase=0
                try {
                    foreach ($module in $game.Modules) {
                        if ([string]::Equals($module.ModuleName,'discord_game_sdk.dll',[StringComparison]::OrdinalIgnoreCase)) { $dllBase=$module.BaseAddress.ToInt64();break }
                    }
                } catch {
                    $cause=$_.Exception
                    while ($cause.InnerException) { $cause=$cause.InnerException }
                    if ($cause -isnot [ComponentModel.Win32Exception] -or $cause.NativeErrorCode -notin @(299,487,998)) { throw }
                }
                if ($dllBase -gt 0) { $null=$bridge.TryPrepareStartupProgression($dllBase) }
                if (-not $bridge.EarlyProgressionPrepared -and (Get-Date) -gt $earlyDeadline) { throw ('Ранний этап не выполнен за 60 секунд. Закрой BO6 обычным способом и старые окна патча; профиль и сейвы не удаляй. Last status: '+$bridge.LastReadinessMessage) }
            }
            if ($bridge.EarlyProgressionPrepared) {
                $bridge.VerifyStartupProgression()
                if (-not $earlyLogged) {
                    [pscustomobject]@{Version='personal-progress-0.2.4-caption-timeout';Stage=$bridge.Stage;ProcessId=$game.Id;DllBase=('0x{0:X}' -f $dllBase);PreparedBeforeSafehouse=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runDir 'early-startup.json') -Encoding UTF8
                    Write-Host 'РАННИЙ ЭТАП ГОТОВ: подмена функций safehouse отключена до загрузки.' -ForegroundColor Green
                    $earlyLogged=$true
                }
                if ($bridge.PollProgressionReady()) { $ready=$true; break }
            }
            if (((Get-Date)-$lastStartupNotice).TotalSeconds -ge 15) {
                Write-Host ('Ожидание готовности игры: '+$bridge.LastReadinessMessage) -ForegroundColor Yellow
                $lastStartupNotice=Get-Date
            }
        }
        Start-Sleep -Milliseconds 100
    }
    if (-not $ready) { throw ('Хранилище контрольных точек не стало готово. '+$(if ($bridge -and $bridge.EarlyProgressionPrepared) {'Основной этап не установлен; раннее изменение действует до закрытия игры. '} else {'Изменения не применены. '})+'Last status: '+$(if ($bridge) { $bridge.LastReadinessMessage } else { 'Game process not found.' })) }
    Write-Host 'Хранилище готово. Профиль и код проверяются и устанавливаются автоматически — ждать меню и нажимать ENTER не нужно.' -ForegroundColor Cyan
    $game.Refresh();if ($game.HasExited) { throw 'BO6 closed before automatic installation.' }
    Write-Host 'INSTALLING: checking code and game threads. Please wait...' -ForegroundColor Cyan
    $guards=New-Object 'System.Collections.Generic.List[BO6CheckpointBridge+Guard]'
    foreach ($item in $spec.Guards) {
        $g=New-Object 'BO6CheckpointBridge+Guard';$g.Address=$image+[long]$item.Rva;$g.Length=$item.Size;$g.Hash=$item.SHA256;$guards.Add($g)
    }
    $patches=New-Object 'System.Collections.Generic.List[BO6CheckpointBridge+Patch]'
    foreach ($item in $spec.Patches) {
        $p=New-Object 'BO6CheckpointBridge+Patch';$p.Name=$item.Name;$p.Address=$image+[long]$item.Rva
        $p.Before=[BO6CheckpointBridge]::Hex($item.Original);$p.After=[BO6CheckpointBridge]::Hex($item.Replacement)
        foreach ($relocation in @($item.Relocations)) {
            if ($null -ne $relocation) {
                [byte[]]$address=[BitConverter]::GetBytes([long]($image+[long]$relocation.Rva))
                [Array]::Copy($address,0,$p.After,[int]$relocation.Offset,8)
            }
        }
        $patches.Add($p)
    }
    [pscustomobject]@{Version='personal-progress-0.2.4-caption-timeout';ProcessId=$game.Id;ImageBase=('0x{0:X}' -f $image);ExeSHA256=$exeHash;PlannedRestoreSlots=@($restores | ForEach-Object {$_.Slot});UICompletionHook=$false;CaptionVisualTimeoutSeconds=2;CaptionFixtureSHA256=[BO6CaptionFixture]::PatchedHash} |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runDir 'session.json') -Encoding UTF8
    $bridge.InstallProgression($patches.ToArray(),$guards.ToArray(),$restores.ToArray(),$profiles)
    $installed=$true
    if ($isNewProfile) { [BO6CheckpointBridge]::SaveProfiles($profilePath,$exeHash,$profiles) }
    Write-Host 'ПАТЧ АКТИВЕН. Обычные условия прогресса и локальная запись включены.' -ForegroundColor Green
    if ($isNewProfile) {
        Write-Host 'Начни новую кампанию в первом слоте. Если такой кнопки нет — выбери ПЕРВУЮ миссию.'
    } else { Write-Host 'Профиль восстановлен. Контрольные точки сохранены.' }
    Write-Host 'После ПАТЧ АКТИВЕН вернись на один уровень назад и снова открой меню кампании: старый список мог сохранить прежние разблокировки.' -ForegroundColor Yellow
    Write-Host 'Затем выбери свой слот и Продолжить (или первую миссию для нового профиля).' 
    Write-Host 'Оставь оба окна открытыми. Выходи из игры обычным способом.'
    Write-Host ([BO6CheckpointBridge]::ProfileSummary($profiles))
    Write-Host ('Local files: '+$dataDir)
    $lastVerify=Get-Date;$lastCaptionStatus='';$lastCaptionCheck=(Get-Date).AddSeconds(-10);$captionWaitStart=Get-Date;$captionWaitNoticed=$false
    while ($true) {
        $game.Refresh();if ($game.HasExited) { break }
        try {
            for ($slot=1; $slot -le 3; $slot++) {
                $snap=$bridge.Capture($slot)
                if ($null -eq $snap) { $candidate.Remove($slot); continue }
                if ($lastSaved[$slot] -eq $snap.Hash) { continue }
                # Require two consecutive polls plus native checksum validation.
                if ($candidate[$slot] -eq $snap.Hash) {
                    $path=Join-Path $dataDir ('slot{0}.bo6local' -f $slot)
                    [BO6CheckpointBridge]::SaveFile($path,$exeHash,$slot,$snap.Raw)
                    $lastSaved[$slot]=$snap.Hash
                    Write-Host ('[{0}] SAVED slot {1}: {2} bytes' -f (Get-Date -Format 'HH:mm:ss'),$slot,$snap.Raw.Length) -ForegroundColor Green
                    ('{0} slot={1} bytes={2} sha256={3}' -f [DateTime]::UtcNow.ToString('o'),$slot,$snap.Raw.Length,$snap.Hash) |
                        Add-Content -LiteralPath (Join-Path $runDir 'saved.txt') -Encoding UTF8
                }
                $candidate[$slot]=$snap.Hash
            }
            $profileSnap=$bridge.CaptureProfiles()
            if ($null -eq $profileSnap) {
                $profileMisses++
                if ($profileMisses -ge 20) { throw 'Профиль недоступен более 10 секунд. Сохранение остановлено; закрой BO6 обычным способом.' }
            } else {
                $profileMisses=0
                # CaptureProfiles already requires two equal immediate reads.
                # Do not wait for equality across polls: gameplay timers may keep changing.
                if ($profileSnap.Hash -ne $profileSaved -and ((Get-Date)-$lastProfileWrite).TotalSeconds -ge 2) {
                    [BO6CheckpointBridge]::SaveProfiles($profilePath,$exeHash,$profileSnap)
                    $profileSaved=$profileSnap.Hash; $lastProfileWrite=Get-Date
                    $state=[BO6CheckpointBridge]::ProfileSummary($profileSnap)
                    Write-Host ('[{0}] ПРОФИЛЬ СОХРАНЁН: {1}' -f (Get-Date -Format 'HH:mm:ss'),$state) -ForegroundColor Green
                    ('{0} sha256={1} {2}' -f [DateTime]::UtcNow.ToString('o'),$profileSnap.Hash,$state) |
                        Add-Content -LiteralPath (Join-Path $runDir 'profiles-saved.txt') -Encoding UTF8
                }
            }
            if (((Get-Date)-$lastCaptionCheck).TotalSeconds -ge 5) {
                try {
                    $captionState=$bridge.ReadCaptionState();$captionSummary=$captionState.Summary
                    if (-not $captionWaitNoticed -and $captionState.Observed -eq 0 -and ((Get-Date)-$captionWaitStart).TotalSeconds -ge 30) {Write-Host 'Ожидается загрузка интерфейса контрольной точки; изменение надписи ещё не отмечено. Сохранение работает.' -ForegroundColor Yellow;$captionWaitNoticed=$true}
                    if ($captionSummary -ne $lastCaptionStatus) {
                        ('{0} {1}' -f [DateTime]::UtcNow.ToString('o'),$captionSummary) | Add-Content -LiteralPath (Join-Path $runDir 'caption-transform.txt') -Encoding UTF8
                        $captionState | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runDir 'caption-state.json') -Encoding UTF8
                        if ($captionState.Mismatched -gt 0) {Write-Host 'Lua надписи отличается: её изменение пропущено. Локальная запись продолжается; подробности в LOG ZIP.' -ForegroundColor Yellow}
                        elseif ($captionState.Applied -gt 0 -or $captionState.AlreadyPatched -gt 0) {Write-Host 'Только надпись сохранения: проверенный таймер скрытия применён.' -ForegroundColor Green}
                        $lastCaptionStatus=$captionSummary
                    }
                } catch {Write-Host ('Не удалось прочитать диагностику надписи; локальная запись продолжается. '+$_.Exception.Message) -ForegroundColor Yellow}
                $lastCaptionCheck=Get-Date
            }
            if (((Get-Date)-$lastVerify).TotalSeconds -ge 10) { $bridge.VerifyPatches($patches.ToArray());$lastVerify=Get-Date }
        } catch {
            $saveFailure=$_
            $game.Refresh();if ($game.HasExited) { break };throw $saveFailure
        }
        Start-Sleep -Milliseconds 500
    }
    Write-Host 'BO6 закрыта. Последние записанные профиль и контрольные точки остались на диске.' -ForegroundColor Green
} catch {
    $failed=$true
    $failure=$_
    Write-Host ('ERROR: '+$failure.Exception.Message) -ForegroundColor Red
    if ($runDir) {
        $details=($failure | Out-String)+[Environment]::NewLine+$failure.Exception.ToString()+[Environment]::NewLine+$failure.ScriptStackTrace
        if ($bridge) { $details+=[Environment]::NewLine+'Bridge stage: '+$bridge.Stage }
        $details | Set-Content -LiteralPath (Join-Path $runDir 'error.txt') -Encoding UTF8
        $cause=$failure.Exception
        while ($cause.InnerException) { $cause=$cause.InnerException }
        Write-Host ('DETAIL: '+$cause.Message) -ForegroundColor Yellow
        if ($cause -is [System.ComponentModel.Win32Exception]) {
            ('NativeErrorCode: '+$cause.NativeErrorCode) | Add-Content -LiteralPath (Join-Path $runDir 'error.txt') -Encoding UTF8
        }
    }
    if ($installed) {
        Write-Host 'Локальная запись остановлена. Последний новый прогресс НЕ подтверждён, независимо от надписи сохранения в игре.' -ForegroundColor Red
        Write-Host 'Закрой BO6 обычным способом; сохрани .bak и LOG ZIP для разбора ошибки.' -ForegroundColor Yellow
    }
    elseif ($bridge -and $bridge.EarlyProgressionPrepared) { Write-Host 'Основной этап не установлен; раннее изменение действует до закрытия игры. Закрой BO6 обычным способом.' -ForegroundColor Yellow }
} finally {
    if ($bridge) {
        if ($runDir) {
            try { $bridge.Diagnostics | Set-Content -LiteralPath (Join-Path $runDir 'install-trace.txt') -Encoding UTF8 }
            catch { Write-Host ('Could not write install trace: '+$_.Exception.Message) -ForegroundColor Yellow }
        }
        $bridge.Dispose()
    }
    if ($ownsMutex) { $mutex.ReleaseMutex() }
    if ($mutex) { $mutex.Dispose() }
    if ($transcribing) { Stop-Transcript | Out-Null }
}
if ($runDir) {
    try { $zip=$runDir+'.zip';Compress-Archive -LiteralPath $runDir -DestinationPath $zip;Write-Host ('LOG ZIP: '+$zip) -ForegroundColor Cyan }
    catch { Write-Host ('Log folder: '+$runDir) }
}
Read-Host 'Press ENTER to close this window' | Out-Null
if ($failed) { exit 1 }
