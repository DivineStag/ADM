#Requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$workspaceRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$testRoot = Join-Path $workspaceRoot ('artifacts/installer-tests/' + [Guid]::NewGuid().ToString('N'))
$installRoot = Join-Path $testRoot 'Installed-ADM'
$null = New-Item -ItemType Directory -Path $testRoot -Force
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$condition,[string]$message) { if (-not $condition) { throw $message }; $checks.Add($message); Write-Output ('PASS: ' + $message) }
& (Join-Path $PackageDirectory 'Install-ADM.ps1') -InstallDirectory $installRoot -NoShortcuts -NoRegistration
Check (Test-Path -LiteralPath (Join-Path $installRoot 'ADM.Desktop.exe')) 'Per-user application installation succeeds in an isolated folder.'
Check ((Test-Path -LiteralPath (Join-Path $installRoot 'coreclr.dll')) -and (Test-Path -LiteralPath (Join-Path $installRoot 'wpfgfx_cor3.dll'))) 'The installed application contains its .NET and WPF runtime.'
Check ((Test-Path -LiteralPath (Join-Path $installRoot 'SimConnect.dll')) -and (Test-Path -LiteralPath (Join-Path $installRoot 'Microsoft.FlightSimulator.SimConnect.dll'))) 'The matching client runtime pair is installed without any SDK prerequisite.'
Check (@(Get-ChildItem -LiteralPath $installRoot -Filter '*.lib' -File -Recurse).Count -eq 0) 'Developer SDK libraries and tools are absent from the payload.'
$config = Get-Content -LiteralPath (Join-Path $installRoot 'ADM.Desktop.runtimeconfig.json') -Raw | ConvertFrom-Json
Check (@($config.runtimeOptions.includedFrameworks).Count -ge 2) 'Runtime configuration confirms self-contained deployment.'
& (Join-Path $workspaceRoot 'scripts/Test-DesktopStartup.ps1') -ExecutablePath (Join-Path $installRoot 'ADM.Desktop.exe') -Configuration Release
$checks.Add('Installed self-contained application renders and closes in isolated smoke-test mode.')
Set-Content -LiteralPath (Join-Path $installRoot 'keep-me.txt') -Value 'User-owned extra file'
& (Join-Path $PackageDirectory 'Install-ADM.ps1') -InstallDirectory $installRoot -NoShortcuts -NoRegistration
Check ((Get-Content -LiteralPath (Join-Path $installRoot 'keep-me.txt') -Raw).Trim() -eq 'User-owned extra file') 'Upgrade preserves files outside the owned manifest.'
& (Join-Path $installRoot 'Uninstall-ADM.ps1') -NoShortcuts -NoRegistration
Check (-not (Test-Path -LiteralPath (Join-Path $installRoot 'ADM.Desktop.exe'))) 'Uninstall removes application files.'
Check (Test-Path -LiteralPath (Join-Path $installRoot 'keep-me.txt')) 'Uninstall preserves extra user files and does not touch personal ADM data.'
$badRoot = Join-Path $testRoot 'BadPackage'; $null = New-Item -ItemType Directory -Path (Join-Path $badRoot 'payload') -Force
Copy-Item -LiteralPath (Join-Path $PackageDirectory 'Install-ADM.ps1') -Destination $badRoot
[pscustomobject]@{ Product='ADM'; Format=1; Version='test'; Files=@([pscustomobject]@{Path='../escape.txt';Sha256='bad'}) } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $badRoot 'package-manifest.json')
$rejected = $false
try { & (Join-Path $badRoot 'Install-ADM.ps1') -InstallDirectory (Join-Path $testRoot 'ShouldNotExist') -NoShortcuts -NoRegistration } catch { $rejected = $_.Exception.Message -match 'escapes' }
Check $rejected 'A traversal path in a package is rejected before installation.'
Set-Content -LiteralPath (Join-Path $badRoot 'payload/file.txt') -Value 'Damaged'
[pscustomobject]@{ Product='ADM'; Format=1; Version='test'; Files=@([pscustomobject]@{Path='file.txt';Sha256='bad'}) } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $badRoot 'package-manifest.json')
$rejected = $false
try { & (Join-Path $badRoot 'Install-ADM.ps1') -InstallDirectory (Join-Path $testRoot 'CorruptTarget') -NoShortcuts -NoRegistration } catch { $rejected = $_.Exception.Message -match 'verification' }
Check ($rejected -and -not (Test-Path -LiteralPath (Join-Path $testRoot 'CorruptTarget'))) 'A corrupt payload is rejected before any destination is created.'
$rollbackTarget = Join-Path $testRoot 'RollbackTarget'; $null = New-Item -ItemType Directory -Path $rollbackTarget
Set-Content -LiteralPath (Join-Path $badRoot 'payload/a.txt') -Value 'New A'; Set-Content -LiteralPath (Join-Path $badRoot 'payload/b.txt') -Value 'New B'
Set-Content -LiteralPath (Join-Path $rollbackTarget 'b.txt') -Value 'Original B'
[pscustomobject]@{Product='ADM';InstallDirectory=$rollbackTarget;Files=@('b.txt')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $rollbackTarget '.adm-install.json')
[pscustomobject]@{ Product='ADM'; Format=1; Version='test'; Files=@(@('a.txt','b.txt') | ForEach-Object { [pscustomobject]@{Path=$_;Sha256=(Get-FileHash -LiteralPath (Join-Path $badRoot ('payload/' + $_))).Hash} }) } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $badRoot 'package-manifest.json')
$locked = [IO.File]::Open((Join-Path $rollbackTarget 'b.txt'),[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
$rejected = $false
try { & (Join-Path $badRoot 'Install-ADM.ps1') -InstallDirectory $rollbackTarget -NoShortcuts -NoRegistration } catch { $rejected = $true } finally { $locked.Dispose() }
Check ($rejected -and -not (Test-Path -LiteralPath (Join-Path $rollbackTarget 'a.txt')) -and (Get-Content -LiteralPath (Join-Path $rollbackTarget 'b.txt') -Raw).Trim() -eq 'Original B') 'A failed upgrade rolls back earlier file writes and preserves the existing installation.'
$badInstall = Join-Path $testRoot 'BadInstall'; $null = New-Item -ItemType Directory -Path $badInstall
Copy-Item -LiteralPath (Join-Path $PackageDirectory 'payload/Uninstall-ADM.ps1') -Destination $badInstall
$outside = Join-Path $testRoot 'outside.txt'; Set-Content -LiteralPath $outside -Value 'Keep'
[pscustomobject]@{Product='ADM';InstallDirectory=$badInstall;Files=@('../outside.txt')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $badInstall '.adm-install.json')
$rejected = $false
try { & (Join-Path $badInstall 'Uninstall-ADM.ps1') -NoShortcuts -NoRegistration } catch { $rejected = $_.Exception.Message -match 'escapes' }
Check ($rejected -and (Test-Path -LiteralPath $outside)) 'Uninstall rejects an escaping manifest without deleting outside files.'
$checks | Set-Content -LiteralPath (Join-Path $workspaceRoot 'artifacts/m8-installer-results.txt')
