function Assert-BO6PreflightResult {
    param([AllowNull()][object]$ExitCode,[string]$ReportPath,[string]$RunToken,[string]$CaptionHash,[string]$LeafHash)
    if ($null -eq $ExitCode -or $ExitCode -isnot [int]) {throw 'Код завершения проверки отсутствует или недействителен; игра не запущена.'}
    if ($ExitCode -ne 0) {throw ('Автоматическая проверка завершилась с ошибкой; игра не запущена. ExitCode='+$ExitCode)}
    if (-not (Test-Path -LiteralPath $ReportPath -PathType Leaf) -or (Get-Item -LiteralPath $ReportPath).Length -gt 16384) {throw 'Отчёт автоматической проверки отсутствует или слишком велик; игра не запущена.'}
    $report=Get-Content -LiteralPath $ReportPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($report.Passed -isnot [bool] -or -not $report.Passed -or $report.GameAccess -isnot [bool] -or $report.GameAccess -or
        ($report.NativeCases -isnot [int] -and $report.NativeCases -isnot [long]) -or $report.NativeCases -ne 12 -or
        $report.Platform -isnot [string] -or $report.RunToken -isnot [string] -or
        $report.CaptionFixtureSHA256 -isnot [string] -or $report.LeafSHA256 -isnot [string] -or
        $report.Platform -cne 'Windows own-process leaf execution' -or $report.RunToken -cne $RunToken -or
        $report.CaptionFixtureSHA256 -cne $CaptionHash -or $report.LeafSHA256 -cne $LeafHash) {
        throw 'Отчёт проверки не соответствует этому запуску, ожидаемым фикстурам или 12 успешным тестам; игра не запущена.'
    }
    return $report
}
function Invoke-BO6PreflightProcess {
    param([string]$Executable,[string]$ScriptPath,[string]$OutputDirectory,[string]$CaptionHash,[string]$LeafHash,[int]$TimeoutMilliseconds=30000)
    if ($TimeoutMilliseconds -lt 100 -or $TimeoutMilliseconds -gt 30000) {throw 'Invalid preflight timeout'}
    $reportPath=Join-Path $OutputDirectory 'preflight.json'
    if (Test-Path -LiteralPath $reportPath) {throw 'В каталоге нового запуска уже есть отчёт проверки; он не использован.'}
    $token=[Guid]::NewGuid().ToString('N')
    $info=New-Object Diagnostics.ProcessStartInfo
    $info.FileName=$Executable
    $info.Arguments='-NoProfile -ExecutionPolicy Bypass -File "{0}" -OutputDirectory "{1}" -RunToken "{2}"' -f $ScriptPath,$OutputDirectory,$token
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $info.StandardOutputEncoding=New-Object Text.UTF8Encoding($false)
    $info.StandardErrorEncoding=New-Object Text.UTF8Encoding($false)
    $process=New-Object Diagnostics.Process
    $process.StartInfo=$info
    $started=$false;$timedOut=$false;$exitCode=$null;$stdout=$null;$stderr=$null;$childId=$null
    $clock=[Diagnostics.Stopwatch]::StartNew()
    try {
        if (-not $process.Start()) {throw 'Не удалось запустить отдельную проверку; игра не запущена.'}
        $started=$true;$childId=$process.Id
        # Process.Start owns the original process handle; retain it until Dispose,
        # including for a child that exits before the first polling iteration.
        $retainedHandle=$process.Handle
        if ($retainedHandle -eq [IntPtr]::Zero) {throw 'Process handle unavailable; game was not launched'}
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        while (-not $process.WaitForExit(100)) {
            if ($clock.ElapsedMilliseconds -ge $TimeoutMilliseconds) {
                $timedOut=$true
                try {$process.Kill()} catch {if (-not $process.HasExited) {throw}}
                if (-not $process.WaitForExit(3000)) {throw 'Дочерняя проверка не завершилась после тайм-аута; игра не запущена.'}
                break
            }
        }
        if (-not $process.WaitForExit(3000)) {throw 'Проверка ещё не завершена; игра не запущена.'}
        if (-not $stdout.Wait(3000) -or -not $stderr.Wait(3000)) {throw 'Потоки вывода проверки не закрылись; игра не запущена.'}
        [IO.File]::WriteAllText((Join-Path $OutputDirectory 'preflight_stdout.txt'),$stdout.GetAwaiter().GetResult(),(New-Object Text.UTF8Encoding($false)))
        [IO.File]::WriteAllText((Join-Path $OutputDirectory 'preflight_stderr.txt'),$stderr.GetAwaiter().GetResult(),(New-Object Text.UTF8Encoding($false)))
        $exitCode=$process.ExitCode
        if ($timedOut) {throw 'Автоматическая проверка остановлена по тайм-ауту до запуска игры. Сейвы оставлены без изменений.'}
        $report=Assert-BO6PreflightResult -ExitCode $exitCode -ReportPath $reportPath -RunToken $token -CaptionHash $CaptionHash -LeafHash $LeafHash
        return $report
    } finally {
        try {
        if ($started -and -not $process.HasExited) {
            try {$process.Kill();$null=$process.WaitForExit(3000)} catch {Write-Warning ('Не удалось остановить только дочернюю проверку: '+$_.Exception.Message)}
        }
        if ($stdout -and $stdout.IsCompleted -and -not $stdout.IsFaulted) {[IO.File]::WriteAllText((Join-Path $OutputDirectory 'preflight_stdout.txt'),$stdout.GetAwaiter().GetResult(),(New-Object Text.UTF8Encoding($false)))}
        if ($stderr -and $stderr.IsCompleted -and -not $stderr.IsFaulted) {[IO.File]::WriteAllText((Join-Path $OutputDirectory 'preflight_stderr.txt'),$stderr.GetAwaiter().GetResult(),(New-Object Text.UTF8Encoding($false)))}
        [pscustomobject]@{ChildId=$childId;ExitCode=$exitCode;TimedOut=$timedOut;RunToken=$token;ElapsedMilliseconds=$clock.ElapsedMilliseconds;GameAccess=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'preflight-process.json') -Encoding UTF8
        } finally {$process.Dispose()}
    }
}
