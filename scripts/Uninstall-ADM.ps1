#Requires -Version 5.1
param([switch]$NoShortcuts, [switch]$NoRegistration)
$ErrorActionPreference = 'Stop'
$installRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$markerPath = Join-Path $installRoot '.adm-install.json'
if (-not (Test-Path -LiteralPath $markerPath)) { throw 'No ADM installation marker. Nothing was removed.' }
$marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
if ($marker.Product -ne 'ADM' -or $marker.InstallDirectory -ne $installRoot -or $installRoot -eq [IO.Path]::GetPathRoot($installRoot).TrimEnd('\')) { throw 'Invalid installation marker. Nothing was removed.' }
foreach ($running in @(Get-Process -Name 'ADM.Desktop' -ErrorAction SilentlyContinue)) {
    if ($running.Path -and $running.Path.StartsWith($installRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Close ADM before uninstalling.' }
}
$paths = @()
$directories = @()
foreach ($relative in $marker.Files) {
    if ([IO.Path]::IsPathRooted($relative)) { throw 'Invalid installed path. Nothing was removed.' }
    $path = [IO.Path]::GetFullPath((Join-Path $installRoot $relative))
    if (-not $path.StartsWith($installRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Installed path escapes ADM. Nothing was removed.' }
    $walk = [IO.Path]::GetDirectoryName($path)
    while ($walk -and $walk.Length -ge $installRoot.Length) {
        if ((Test-Path -LiteralPath $walk) -and ((Get-Item -LiteralPath $walk).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Directory links are not supported. Nothing was removed.' }
        if ($walk.Length -gt $installRoot.Length) { $directories += $walk }
        $walk = [IO.Path]::GetDirectoryName($walk)
    }
    $paths += $path
}
# No recursive deletion: remove only validated owned files. Extra user files and all LocalAppData\ADM data remain.
foreach ($path in $paths) { if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force } }
Remove-Item -LiteralPath $markerPath
foreach ($directory in @($directories | Select-Object -Unique | Sort-Object Length -Descending)) {
    if ((Test-Path -LiteralPath $directory) -and @(Get-ChildItem -LiteralPath $directory -Force).Count -eq 0) { Remove-Item -LiteralPath $directory }
}
if (-not $NoShortcuts) {
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Aircraft Dispatch Manager.lnk'
    if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath }
}
if (-not $NoRegistration) {
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ADM'
    if ((Get-ItemProperty -Path $key -ErrorAction SilentlyContinue).InstallLocation -eq $installRoot) { Remove-Item -LiteralPath $key }
}
if (@(Get-ChildItem -LiteralPath $installRoot -Force).Count -eq 0) { Remove-Item -LiteralPath $installRoot }
Write-Output 'ADM uninstalled. Your profiles, preferences and history were retained.'
