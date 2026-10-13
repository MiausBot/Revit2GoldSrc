[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet(2024,2025,2026,2027)][int]$Version)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$package = Join-Path $workspace "artifacts/$Version"
if (-not (Test-Path -LiteralPath (Join-Path $package 'Revit2GoldSrc.addin') -PathType Leaf)) { throw "Ejecute primero scripts/Build.ps1 -Versions $Version." }
$running = Get-Process Revit -ErrorAction SilentlyContinue | Where-Object {
    # Windows can return a shortened 8.3 path; identify the executable by version.
    if (-not $_.Path) { return $true }
    try { return [System.Diagnostics.FileVersionInfo]::GetVersionInfo($_.Path).FileMajorPart -eq ($Version - 2000) }
    catch { return $true }
}
if ($running) { throw "Cierre Revit $Version antes de instalar y vuelva a ejecutar este script." }
$addins = Join-Path $env:APPDATA "Autodesk/Revit/Addins/$Version"
$destination = Join-Path $addins 'Revit2GoldSrc'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $package 'Revit2GoldSrc') -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $destination -Force }
# Use an absolute assembly path for reliable loading regardless of Revit's working directory.
[xml]$manifest = Get-Content -LiteralPath (Join-Path $package 'Revit2GoldSrc.addin') -Raw
$manifest.RevitAddIns.AddIn.Assembly = Join-Path $destination 'Revit2GoldSrc.Revit.dll'
$manifest.Save((Join-Path $addins 'Revit2GoldSrc.addin'))
Write-Host "Instalado para el usuario actual: $addins. Abra Revit $Version."
