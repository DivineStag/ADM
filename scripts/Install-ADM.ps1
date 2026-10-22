#Requires -Version 5.1
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\ADM'),
    [switch]$Launch,
    [switch]$NoShortcuts,
    [switch]$NoRegistration
)
$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess) { throw 'ADM requires Windows x64. Run the 64-bit installer.' }
$payloadRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'payload'))
$targetRoot = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
if ($targetRoot -eq [IO.Path]::GetPathRoot($targetRoot).TrimEnd('\') -or $targetRoot -eq $env:USERPROFILE -or $targetRoot -eq $env:LOCALAPPDATA) { throw 'Choose a dedicated ADM installation folder.' }
function Assert-ContainedPath([string]$root, [string]$relative) {
    if ([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative)) { throw 'Invalid package path.' }
    $full = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $full.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Package path escapes the installation folder.' }
    $walk = [IO.Path]::GetDirectoryName($full)
    while ($walk -and $walk.Length -ge $root.Length) {
        if ((Test-Path -LiteralPath $walk) -and ((Get-Item -LiteralPath $walk).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Installation through a directory link is not supported.' }
        $walk = [IO.Path]::GetDirectoryName($walk)
    }
    return $full
}
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'package-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.Product -ne 'ADM' -or $manifest.Format -ne 1) { throw 'This is not an ADM package.' }
$markerPath = Join-Path $targetRoot '.adm-install.json'
$previous = $null
if (Test-Path -LiteralPath $targetRoot) {
    if (-not (Test-Path -LiteralPath $markerPath) -and @(Get-ChildItem -LiteralPath $targetRoot -Force).Count -gt 0) { throw 'The selected folder contains other files. Choose an empty folder or an existing ADM installation.' }
    if (Test-Path -LiteralPath $markerPath) {
        $previous = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
        if ($previous.Product -ne 'ADM') { throw 'The existing installation marker is invalid.' }
    }
}
foreach ($running in @(Get-Process -Name 'ADM.Desktop' -ErrorAction SilentlyContinue)) {
    if ($running.Path -and $running.Path.StartsWith($targetRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Close ADM, then run setup again. Your flight data will be kept.' }
}
# Check every source hash and destination before changing any file.
foreach ($entry in $manifest.Files) {
    $source = Assert-ContainedPath $payloadRoot $entry.Path
    $null = Assert-ContainedPath $targetRoot $entry.Path
    if (-not (Test-Path -LiteralPath $source) -or (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.Sha256) { throw ('Package verification failed: ' + $entry.Path) }
}
$rollbackRoot = Join-Path ([IO.Path]::GetTempPath()) ('adm-install-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $rollbackRoot
$null = New-Item -ItemType Directory -Path $targetRoot -Force
$writes = [Collections.Generic.List[object]]::new()
$installed = [Collections.Generic.List[string]]::new()
try {
    foreach ($entry in $manifest.Files) {
        $destination = Assert-ContainedPath $targetRoot $entry.Path
        $backup = $null
        if (Test-Path -LiteralPath $destination) { $backup = Join-Path $rollbackRoot ($writes.Count.ToString() + '.bak'); Copy-Item -LiteralPath $destination -Destination $backup }
        $writes.Add([pscustomobject]@{ Path = $destination; Backup = $backup })
        $null = New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force
        Copy-Item -LiteralPath (Assert-ContainedPath $payloadRoot $entry.Path) -Destination $destination -Force
        $installed.Add($entry.Path)
    }
    foreach ($old in @($previous.Files)) { if ($old) { $null = Assert-ContainedPath $targetRoot $old; $installed.Add($old) } }
    [pscustomobject]@{ Product='ADM'; Format=1; Version=$manifest.Version; Files=@($installed | Select-Object -Unique); InstallDirectory=$targetRoot; InstallerSource=$PSScriptRoot } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath ($markerPath + '.new') -Encoding UTF8
    Move-Item -LiteralPath ($markerPath + '.new') -Destination $markerPath -Force
} catch {
    foreach ($write in @($writes | Sort-Object { $_.Path.Length } -Descending)) {
        if ($write.Backup) { Copy-Item -LiteralPath $write.Backup -Destination $write.Path -Force }
        elseif (Test-Path -LiteralPath $write.Path) { Remove-Item -LiteralPath $write.Path -Force }
    }
    throw
} finally {
    foreach ($backup in @(Get-ChildItem -LiteralPath $rollbackRoot -File)) { Remove-Item -LiteralPath $backup.FullName -Force }
    Remove-Item -LiteralPath $rollbackRoot
}
if (-not $NoShortcuts) {
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Aircraft Dispatch Manager.lnk'
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
    $shortcut.TargetPath = Join-Path $targetRoot 'ADM.Desktop.exe'; $shortcut.WorkingDirectory = $targetRoot; $shortcut.Save()
}
if (-not $NoRegistration) {
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ADM'
    $null = New-Item -Path $key -Force
    New-ItemProperty -Path $key -Name DisplayName -Value 'Aircraft Dispatch Manager' -Force | Out-Null
    New-ItemProperty -Path $key -Name DisplayVersion -Value $manifest.Version -Force | Out-Null
    New-ItemProperty -Path $key -Name InstallLocation -Value $targetRoot -Force | Out-Null
    New-ItemProperty -Path $key -Name UninstallString -Value ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $targetRoot 'Uninstall-ADM.ps1') + '"') -Force | Out-Null
}
Write-Output ('ADM installed: ' + (Join-Path $targetRoot 'ADM.Desktop.exe'))
Write-Output 'Profiles and flight history are kept separately in LocalAppData\ADM. This is a private beta, not a qualified public release.'

if ($Launch) { Start-Process -FilePath (Join-Path $targetRoot 'ADM.Desktop.exe') -WorkingDirectory $targetRoot -WindowStyle Normal }
