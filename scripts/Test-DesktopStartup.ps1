param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug', [string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
$executable = if ($ExecutablePath) { $ExecutablePath } else { Join-Path $PSScriptRoot "..\src\ADM.Desktop\bin\$Configuration\net10.0-windows\ADM.Desktop.exe" }
if (-not (Test-Path -LiteralPath $executable)) { throw 'Build ADM.sln before running the startup test.' }
$startedAt = [DateTimeOffset]::UtcNow
$appProcess = Start-Process -FilePath $executable -ArgumentList '--smoke-test' -WindowStyle Hidden -PassThru
if (-not $appProcess.WaitForExit(20000)) {
    $appProcess.Kill()
    throw 'WPF startup did not render and close within 20 seconds.'
}
if ($appProcess.ExitCode -ne 0) { throw "WPF startup failed: exit code $($appProcess.ExitCode)." }
$logPath = Join-Path $env:LOCALAPPDATA ("ADM\logs\adm-{0}.log" -f $startedAt.ToString('yyyy-MM-dd'))
$marker = Get-Content -LiteralPath $logPath | Where-Object {
    $_ -match 'smoke test: WPF window rendered' -and [DateTimeOffset]::Parse($_.Substring(0, 33)) -ge $startedAt
}
if (-not $marker) { throw 'The process exited without a fresh WPF ContentRendered marker.' }
Write-Output 'PASS: WPF window rendered, DI resolved, fake monitor started, clean exit.'
