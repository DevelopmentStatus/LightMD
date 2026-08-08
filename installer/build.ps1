<#
.SYNOPSIS
    Publishes LightMD and packages it into an MSI.

.DESCRIPTION
    Produces installer/bin/LightMD-<version>-x64.msi.

    Requires the WiX v7 CLI:
        dotnet tool install --global wix

.PARAMETER Version
    Product version baked into the MSI. Must be a.b.c.d.
#>
[CmdletBinding()]
param(
    [string]$Version = '1.0.0.0',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

# The version is what Windows compares to decide an install is an upgrade, so a
# malformed one silently produces a package that won't replace the old install.
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Version must be four numeric parts (e.g. 1.1.0.0), got '$Version'."
}

$repoRoot  = Split-Path -Parent $PSScriptRoot
$project   = Join-Path $repoRoot 'LightMD\LightMD.csproj'
$publishDir = Join-Path $PSScriptRoot 'obj\publish'
$outputDir = Join-Path $PSScriptRoot 'bin'
$msiPath   = Join-Path $outputDir "LightMD-$Version-x64.msi"

Write-Host '==> Publishing LightMD (framework-dependent, win-x64)' -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

dotnet publish $project `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained false `
    -p:DebugType=none `
    -p:GenerateDocumentationFile=false `
    --output $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# Reference XML docs ship with the WebView2 package but add nothing at runtime.
Get-ChildItem $publishDir -Filter *.xml -Recurse | Remove-Item -Force
Get-ChildItem $publishDir -Filter *.pdb -Recurse | Remove-Item -Force

Write-Host '==> Building MSI' -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

wix build (Join-Path $PSScriptRoot 'LightMD.wxs') `
    -arch x64 `
    -define "PublishDir=$publishDir" `
    -define "Version=$Version" `
    -out $msiPath
if ($LASTEXITCODE -ne 0) { throw "wix build failed ($LASTEXITCODE)" }

$sizeMb = [math]::Round((Get-Item $msiPath).Length / 1MB, 2)
Write-Host ''
Write-Host "Built $msiPath ($sizeMb MB)" -ForegroundColor Green
