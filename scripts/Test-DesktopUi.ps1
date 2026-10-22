param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug', [string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
$workspacePath = Split-Path $PSScriptRoot -Parent
if (-not $ExecutablePath) { $ExecutablePath = Join-Path $workspacePath "src\ADM.Desktop\bin\$Configuration\net10.0-windows\ADM.Desktop.exe" }
if (-not (Test-Path -LiteralPath $executablePath)) { throw 'Build ADM.sln before running UI verification.' }
$startedAt = [DateTimeOffset]::UtcNow
$uiProcess = Start-Process -FilePath $executablePath -ArgumentList '--ui-test' -WorkingDirectory $workspacePath -WindowStyle Hidden -PassThru
if (-not $uiProcess.WaitForExit(45000)) {
    $uiProcess.Kill()
    throw 'WPF UI verification did not finish within 45 seconds.'
}
$reportPath = Join-Path $workspacePath 'artifacts\ui\ui-results.txt'
$failurePath = Join-Path $workspacePath 'artifacts\ui\ui-failure.txt'
if ($uiProcess.ExitCode -ne 0) {
    if (Test-Path -LiteralPath $failurePath) { Get-Content -LiteralPath $failurePath }
    throw "UI verification failed with exit code $($uiProcess.ExitCode)."
}
if (-not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $startedAt.UtcDateTime) {
    throw 'UI verification did not produce a fresh report.'
}
Get-Content -LiteralPath $reportPath
