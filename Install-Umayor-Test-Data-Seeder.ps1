<#
.SYNOPSIS
    Installs the Umayor Test Data Seeder plugin into your local XrmToolBox.

.DESCRIPTION
    Mismo patrón que DataverseMasterDataMigrator (y, antes que ese, Metadata Dataverse Document):
    el ensamblado principal va a la RAÍZ de la carpeta Plugins (donde el loader de XrmToolBox lo
    busca), y las dependencias propias de este plugin (DataverseMasterDataMigrator.Core.dll,
    Umayor.TestDataSeeder.Core.dll) van a una subcarpeta dedicada — nunca la raíz — para que
    nunca puedan chocar con la dependencia de otro plugin con el mismo nombre. El
    AssemblyResolveEventHandler de Plugin.cs es lo que las carga desde ahí en tiempo real.

    Deliberadamente NO se copian: Microsoft.Xrm.Sdk.dll, XrmToolBox.Extensibility.dll,
    McTools.Xrm.Connection*.dll, Newtonsoft.Json.dll, etc. — son dependencias del HOST (ya
    cargadas por XrmToolBox.exe antes de que cargue cualquier plugin); copiar una segunda vez
    arriesgaría un conflicto de versión en vez de evitarlo.

.PARAMETER SourceFolder
    Carpeta con los DLL compilados. Por defecto ".\bin" junto a este script (el layout que usa
    el .zip de instalación); pasar -SourceFolder explícitamente para instalar directo desde la
    salida de un build Release.

.PARAMETER Force
    Salta la confirmación de "XrmToolBox está corriendo".
#>
[CmdletBinding()]
param(
    [string]$SourceFolder,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($SourceFolder)) {
    $scriptDir = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($scriptDir) -and $MyInvocation.MyCommand.Path) {
        $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    }
    if ([string]::IsNullOrWhiteSpace($scriptDir)) {
        $scriptDir = (Get-Location).Path
    }
    $SourceFolder = Join-Path $scriptDir "bin"
}

$MainAssembly = "Umayor.TestDataSeeder.dll"
$OwnDependencies = @(
    "DataverseMasterDataMigrator.Core.dll",
    "Umayor.TestDataSeeder.Core.dll"
)

function Write-Step($message) {
    Write-Host "==> $message" -ForegroundColor Cyan
}

$mainAssemblyPath = Join-Path $SourceFolder $MainAssembly
if (-not (Test-Path $mainAssemblyPath)) {
    throw "Could not find '$MainAssembly' in '$SourceFolder'. Build in Release first, or pass -SourceFolder pointing at the folder with the compiled DLLs."
}
foreach ($dep in $OwnDependencies) {
    if (-not (Test-Path (Join-Path $SourceFolder $dep))) {
        throw "Could not find '$dep' in '$SourceFolder'. Build in Release first."
    }
}

$pluginsRoot = Join-Path $env:APPDATA "MscrmTools\XrmToolBox\Plugins"
$ownSubfolder = Join-Path $pluginsRoot "Umayor.TestDataSeeder"

if ((Get-Process -Name "XrmToolBox" -ErrorAction SilentlyContinue) -and -not $Force) {
    Write-Warning "XrmToolBox is running. If the destination file already exists and is in use, the copy below will fail - close XrmToolBox and re-run, or pass -Force to skip this prompt."
    if ([Environment]::UserInteractive -and -not ([Console]::IsInputRedirected)) {
        $answer = Read-Host "Continue anyway? (y/N)"
        if ($answer -notmatch '^[yY]') {
            Write-Host "Installation cancelled." -ForegroundColor Yellow
            exit 1
        }
    }
    else {
        Write-Host "Non-interactive session: continuing anyway (the copy will fail with a clear error if the file is actually locked)." -ForegroundColor Yellow
    }
}

Write-Step "Plugins folder: $pluginsRoot"
if (-not (Test-Path $pluginsRoot)) {
    New-Item -ItemType Directory -Path $pluginsRoot -Force | Out-Null
}

Write-Step "Copying $MainAssembly to the Plugins root"
Copy-Item $mainAssemblyPath (Join-Path $pluginsRoot $MainAssembly) -Force

Write-Step "Copying own dependencies to their dedicated subfolder (never the root)"
if (-not (Test-Path $ownSubfolder)) {
    New-Item -ItemType Directory -Path $ownSubfolder -Force | Out-Null
}
foreach ($dep in $OwnDependencies) {
    Copy-Item (Join-Path $SourceFolder $dep) (Join-Path $ownSubfolder $dep) -Force
}

Write-Host ""
Write-Host "Installation complete." -ForegroundColor Green
Write-Host "  $pluginsRoot\$MainAssembly"
Write-Host "  $ownSubfolder\ (+ $($OwnDependencies.Count) dependency DLLs)"
Write-Host ""
Write-Host "Open (or reopen) XrmToolBox and look for 'Umayor Test Data Seeder' in the tool list."
