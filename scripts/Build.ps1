[CmdletBinding()]
param(
    [ValidateSet(2024,2025,2026,2027)][int[]]$Versions = @(2024,2025),
    [string]$RevitInstallDir = '',
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
if ($RevitInstallDir -and $Versions.Count -ne 1) { throw 'RevitInstallDir requiere un único año.' }
foreach ($year in $Versions) {
    $install = if ($RevitInstallDir) { $RevitInstallDir } else { "C:\Program Files\Autodesk\Revit $year" }
    foreach ($dll in @('RevitAPI.dll','RevitAPIUI.dll')) {
        $api = Join-Path $install $dll
        if (-not (Test-Path -LiteralPath $api -PathType Leaf)) { throw "Falta $api; no se sustituye por otra versión." }
        $identity = [System.Reflection.AssemblyName]::GetAssemblyName($api)
        if ($identity.Version.Major -ne ($year - 2000)) { throw "$api no corresponde a Revit $year." }
    }
    & dotnet build (Join-Path $workspace 'Revit2GoldSrc.Revit/Revit2GoldSrc.Revit.csproj') "-p:RevitVersion=$year" "-p:RevitInstallDir=$install" -c $Configuration -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Falló la compilación para $year." }
    $framework = switch ($year) { 2024 {'net48'} 2027 {'net10.0-windows'} default {'net8.0-windows'} }
    $source = Join-Path $workspace "Revit2GoldSrc.Revit/bin/$year/$Configuration/$framework"
    $package = Join-Path $workspace "artifacts/$year"
    $payload = Join-Path $package 'Revit2GoldSrc'
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    foreach ($name in @('Revit2GoldSrc.Revit','Revit2GoldSrc.Core','Revit2GoldSrc.GoldSrc','Revit2GoldSrc.Compilation','Revit2Source','Revit2Source.Compilation')) {
        Copy-Item -LiteralPath (Join-Path $source "$name.dll") -Destination $payload -Force
        if (Test-Path -LiteralPath (Join-Path $source "$name.pdb")) { Copy-Item -LiteralPath (Join-Path $source "$name.pdb") -Destination $payload -Force }
    }
    $deps = Join-Path $source 'Revit2GoldSrc.Revit.deps.json'
    if (Test-Path -LiteralPath $deps) { Copy-Item -LiteralPath $deps -Destination $payload -Force }
    Copy-Item -LiteralPath (Join-Path $workspace "Addins/Revit2GoldSrc.$year.addin") -Destination (Join-Path $package 'Revit2GoldSrc.addin') -Force
    Write-Host "Paquete preparado: $package"
}
