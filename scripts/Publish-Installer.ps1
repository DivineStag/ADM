#Requires -Version 7.0
param(
    [string]$Version = '0.8.0-beta.4',
    [string]$Repository = 'DivineStag/ADM',
    [string]$CompilerPath = '',
    [string]$SignCommand = ''
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Use a semantic version.' }
if ($Repository -and $Repository -notmatch '^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9][A-Za-z0-9._-]{0,99}$') { throw 'Use owner/repository.' }
$workspaceRoot = Split-Path $PSScriptRoot -Parent
if (-not $CompilerPath) {
    $CompilerPath = Join-Path $workspaceRoot 'artifacts\tools\InnoSetup\ISCC.exe'
    if (-not (Test-Path -LiteralPath $CompilerPath)) { $CompilerPath = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
}
if (-not (Test-Path -LiteralPath $CompilerPath)) { throw 'Install the official Inno Setup 6.7+ compiler, or pass -CompilerPath.' }
$releaseRoot = Join-Path $workspaceRoot ('artifacts\releases\ADM-' + $Version + '-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-setup')
if (Test-Path -LiteralPath $releaseRoot) { throw 'Output already exists.' }
$payload = Join-Path $releaseRoot 'payload'
$null = New-Item -ItemType Directory -Path $payload -Force
& dotnet publish (Join-Path $workspaceRoot 'src\ADM.Desktop\ADM.Desktop.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishReadyToRun=false -p:UseSharedCompilation=false "-p:Version=$Version" "-p:GitHubRepository=$Repository" -m:1 -nodeReuse:false -o $payload
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($file in @('SimConnect.dll','Microsoft.FlightSimulator.SimConnect.dll','coreclr.dll','PresentationFramework.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $file))) { throw ('Missing runtime: ' + $file) }
}
Get-ChildItem -LiteralPath $payload -Filter '*.pdb' -File | ForEach-Object { Remove-Item -LiteralPath $_.FullName }
$documentation = Join-Path $payload 'Documentation'
$null = New-Item -ItemType Directory -Path $documentation
foreach ($file in @('privacy.md','windows-beta-setup.md','github-distribution.md','gsx-readiness-policy.md','release-checklist.md','beta-test-matrix.md')) {
    Copy-Item -LiteralPath (Join-Path $workspaceRoot ('docs\' + $file)) -Destination $documentation
}
Copy-Item -LiteralPath (Join-Path $workspaceRoot 'docs\licensing') -Destination (Join-Path $documentation 'Licensing') -Recurse
foreach ($runtime in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
    $runtimeRoot = Join-Path (Join-Path $env:USERPROFILE '.nuget\packages') $runtime
    $runtimeVersion = Get-ChildItem -LiteralPath $runtimeRoot -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if ($runtimeVersion) {
        $noticeTarget = Join-Path $documentation ('Licensing\' + $runtime)
        $null = New-Item -ItemType Directory -Path $noticeTarget
        Get-ChildItem -LiteralPath $runtimeVersion.FullName -File | Where-Object { $_.Name -match 'LICENSE|NOTICE' } | Copy-Item -Destination $noticeTarget
    }
}
$compilerArguments = @('/Qp',"/DPayload=$payload","/DOutput=$releaseRoot","/DVersion=$Version","/DRepository=$Repository")
if ($SignCommand) { $compilerArguments += @('/DSignCommand=yes',('/sADMSign=' + $SignCommand)) }
& $CompilerPath @compilerArguments (Join-Path $workspaceRoot 'installer\ADM.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compile failed.' }
$setup = Join-Path $releaseRoot 'ADM-Setup.exe'
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
$hash | Set-Content -LiteralPath ($setup + '.sha256')
[pscustomobject]@{ Installer=$setup; Sha256=$hash; Payload=$payload; Version=$Version; Repository=$Repository; Distribution='Local qualification only until release gates pass' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $workspaceRoot 'artifacts\m8-setup.json')
Write-Output ('Windows setup ready: ' + $setup)
Write-Output 'No public upload performed. Public-release licensing, signing and live qualification remain separate gates.'
