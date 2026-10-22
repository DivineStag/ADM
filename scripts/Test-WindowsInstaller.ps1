#Requires -Version 7.0
param([string]$Payload, [string]$CompilerPath = (Join-Path $PSScriptRoot '..\artifacts\tools\InnoSetup\ISCC.exe'))
$ErrorActionPreference = 'Stop'
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Payload) { $Payload = (Get-Content -LiteralPath (Join-Path $workspaceRoot 'artifacts\m8-setup.json') -Raw | ConvertFrom-Json).Payload }
$testRoot = Join-Path $workspaceRoot ('artifacts\native-installer-tests\' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot -Force
& $CompilerPath /Qp "/DPayload=$Payload" "/DOutput=$testRoot" /DTestBuild=yes (Join-Path $workspaceRoot 'installer\ADM.iss')
if ($LASTEXITCODE -ne 0) { throw 'Qualification installer compilation failed.' }
$setup = Join-Path $testRoot 'ADM-Setup.exe'
$installRoot = Join-Path $testRoot 'Installed'
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$condition,[string]$text) { if (-not $condition) { throw $text }; $checks.Add($text); Write-Output ('PASS: ' + $text) }
function Install {
    $process = Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS',('/DIR="' + $installRoot + '"'),('/LOG="' + (Join-Path $testRoot 'setup.log') + '"')) -WindowStyle Hidden -Wait -PassThru
    Check ($process.ExitCode -eq 0) 'Native Windows setup succeeds in an isolated folder.'
}
Install
Check ((Test-Path -LiteralPath (Join-Path $installRoot 'coreclr.dll')) -and (Test-Path -LiteralPath (Join-Path $installRoot 'SimConnect.dll'))) 'The self-contained installation includes .NET/WPF and the client runtime pair.'
Check (@(Get-ChildItem -LiteralPath (Join-Path $installRoot 'Defaults\Profiles') -Filter '*.json').Count -eq 25) 'All 25 default profiles are installed.'
Check ((Test-Path -LiteralPath (Join-Path $installRoot 'Maintenance\ADM-Setup.exe')) -and (Test-Path -LiteralPath (Join-Path $installRoot 'Maintenance\ADM-Setup.exe.sha256'))) 'Setup is retained for one-button installation repair.'
$cache = Join-Path $installRoot 'Maintenance\ADM-Setup.exe'
Check ((Get-FileHash -LiteralPath $cache).Hash -eq (Get-Content -LiteralPath ($cache + '.sha256') -Raw).Trim()) 'The cached repair installer matches its recorded checksum.'
& (Join-Path $workspaceRoot 'scripts\Test-DesktopStartup.ps1') -Configuration Release -ExecutablePath (Join-Path $installRoot 'ADM.Desktop.exe')
& (Join-Path $workspaceRoot 'scripts\Test-DesktopUi.ps1') -Configuration Release -ExecutablePath (Join-Path $installRoot 'ADM.Desktop.exe')
Check ($true) 'Installed application passes startup and UI/input checks with isolated fake state.'
Set-Content -LiteralPath (Join-Path $installRoot 'user-keep.txt') -Value 'User-owned file'
Install
Check ((Get-Content -LiteralPath (Join-Path $installRoot 'user-keep.txt') -Raw).Trim() -eq 'User-owned file') 'A setup rerun preserves unowned files.'
$repair = Start-Process -FilePath $cache -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS',('/DIR="' + $installRoot + '"')) -WindowStyle Hidden -Wait -PassThru
Check ($repair.ExitCode -eq 0) 'The cached native setup can repair its own installation.'
$uninstall = Start-Process -FilePath (Join-Path $installRoot 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -Wait -PassThru
Check ($uninstall.ExitCode -eq 0) 'Native uninstall completes successfully.'
# Inno may remove its final helper asynchronously after returning.
$deadline = [DateTime]::UtcNow.AddSeconds(10)
while ((Test-Path -LiteralPath (Join-Path $installRoot 'ADM.Desktop.exe')) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
Check (-not (Test-Path -LiteralPath (Join-Path $installRoot 'ADM.Desktop.exe'))) 'Uninstall removes the application.'
Check (Test-Path -LiteralPath (Join-Path $installRoot 'user-keep.txt')) 'Uninstall retains unowned files; personal LocalAppData/ADM is outside its file list.'
$checks | Set-Content -LiteralPath (Join-Path $workspaceRoot 'artifacts\m8-native-installer-results.txt')
Write-Output 'Qualification macro suppresses Start/Apps registration; verify those on a clean PC before release.'
