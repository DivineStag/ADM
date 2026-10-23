#Requires -Version 7.0
param([string]$ReportPath = 'artifacts/dependency-security.json')
$ErrorActionPreference = 'Stop'
$report = & dotnet list ADM.sln package --vulnerable --include-transitive --format json
if ($LASTEXITCODE -ne 0) { throw 'NuGet vulnerability query failed; no clean result can be claimed.' }
$text = $report -join "`n"
$result = $text | ConvertFrom-Json
if (-not $result.projects) { throw 'NuGet returned no project results.' }
$parent = Split-Path $ReportPath -Parent
if ($parent) { $null = New-Item -ItemType Directory -Path $parent -Force }
$text | Set-Content -LiteralPath $ReportPath
$findings = @(
    foreach ($project in $result.projects) {
        foreach ($framework in $project.frameworks) {
            foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                if ($package.vulnerabilities) { $package.id }
            }
        }
    }
)
if ($findings.Count) { throw ('Vulnerable packages require review: ' + (($findings | Sort-Object -Unique) -join ', ')) }
Write-Output ('PASS: NuGet reports no known vulnerable direct/transitive packages across ' + $result.projects.Count + ' projects.')
