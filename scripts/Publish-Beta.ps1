#Requires -Version 7.0
param([string]$Version = '0.8.0-beta.1')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+-[a-zA-Z0-9.]+$') { throw 'Use a beta version.' }
$workspaceRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$releaseRoot = Join-Path $workspaceRoot ('artifacts\releases\ADM-' + $Version + '-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
if (Test-Path -LiteralPath $releaseRoot) { throw 'That package folder already exists. Try again in a moment.' }
$payload = Join-Path $releaseRoot 'payload'
$null = New-Item -ItemType Directory -Path $payload -Force
& dotnet publish (Join-Path $workspaceRoot 'src\ADM.Desktop\ADM.Desktop.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishReadyToRun=false -p:NuGetAudit=false -p:UseSharedCompilation=false -m:1 -nodeReuse:false -o $payload
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($runtime in @('SimConnect.dll','Microsoft.FlightSimulator.SimConnect.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $runtime))) { throw 'The developer build must include the matching official SimConnect runtime pair.' }
}
Get-ChildItem -LiteralPath $payload -Filter '*.pdb' -File | ForEach-Object { Remove-Item -LiteralPath $_.FullName }
# This folder stays local until owner licence, runtime redistribution and media rights are cleared.
$documentation = Join-Path $payload 'Documentation'
$null = New-Item -ItemType Directory -Path $documentation
foreach ($file in @('privacy.md','windows-beta-setup.md')) { Copy-Item -LiteralPath (Join-Path $workspaceRoot ('docs\' + $file)) -Destination $documentation }
$licences = Join-Path $documentation 'Licensing'
Copy-Item -LiteralPath (Join-Path $workspaceRoot 'docs\licensing') -Destination $licences -Recurse
foreach ($runtime in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
    $runtimeRoot = Join-Path (Join-Path $env:USERPROFILE '.nuget\packages') $runtime
    $runtimeVersion = Get-ChildItem -LiteralPath $runtimeRoot -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if ($runtimeVersion) {
        $noticeTarget = Join-Path $licences $runtime
        $null = New-Item -ItemType Directory -Path $noticeTarget
        Get-ChildItem -LiteralPath $runtimeVersion.FullName -File | Where-Object { $_.Name -match 'LICENSE|NOTICE' } | Copy-Item -Destination $noticeTarget
    }
}
Copy-Item -LiteralPath (Join-Path $workspaceRoot 'scripts\Uninstall-ADM.ps1') -Destination $payload
Copy-Item -LiteralPath (Join-Path $workspaceRoot 'scripts\Install-ADM.ps1') -Destination $releaseRoot
Set-Content -LiteralPath (Join-Path $releaseRoot 'Install.cmd') -Encoding ascii -Value '@powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-ADM.ps1" -Launch'
Copy-Item -LiteralPath (Join-Path $workspaceRoot 'docs\windows-beta-setup.md') -Destination (Join-Path $releaseRoot 'README.md')
$files = @(Get-ChildItem -LiteralPath $payload -File -Recurse | ForEach-Object {
    [pscustomobject]@{ Path=$_.FullName.Substring($payload.Length + 1); Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
[pscustomobject]@{ Product='ADM'; Format=1; Version=$Version; Distribution='Local qualification only'; InstallerSha256=(Get-FileHash -LiteralPath (Join-Path $releaseRoot 'Install-ADM.ps1') -Algorithm SHA256).Hash; Files=$files } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseRoot 'package-manifest.json') -Encoding utf8
$archive = $releaseRoot + '-local-beta.zip'
Compress-Archive -LiteralPath @(Get-ChildItem -LiteralPath $releaseRoot | Select-Object -ExpandProperty FullName) -DestinationPath $archive
(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash | Set-Content -LiteralPath ($archive + '.sha256')
[pscustomobject]@{ PackageDirectory=$releaseRoot; Archive=$archive; Payload=$payload } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $workspaceRoot 'artifacts\m8-package.json')
Write-Output ('Local beta ready: ' + $archive)
Write-Output 'Not a public release: live qualification and redistribution approvals remain open.'
