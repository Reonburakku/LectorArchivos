<#
.SYNOPSIS
    Genera el instalador de Lector de Archivos para publicarlo en una GitHub Release.

.DESCRIPTION
    1. Publica la app para Windows x64 incluyendo .NET (el usuario no necesita instalar nada).
    2. Empaqueta con Velopack (https://velopack.io): crea un Setup.exe que instala la app
       para el usuario actual, sin permisos de administrador, con acceso directo en el menú Inicio
       y en el escritorio, y con desinstalación desde «Aplicaciones instaladas» de Windows.

    El resultado queda en artifacts\releases\. Los archivos para subir a la release son:
      - LectorArchivos-win-Setup.exe     (instalador)
      - LectorArchivos-win-Portable.zip  (versión portable, sin instalar)

.PARAMETER Version
    Versión a generar (por ejemplo 1.0.1). Si no se indica, se usa <Version> de LectorArchivos.csproj.

.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -Version 1.0.1
#>
param([string]$Version)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$project = 'LectorArchivos\LectorArchivos.csproj'
if (-not $Version) {
    $Version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if (-not $Version) { throw "No se encontró <Version> en $project. Indica -Version." }

# vpk: herramienta de línea de comandos de Velopack (misma versión que el paquete NuGet Velopack).
$env:PATH += ";$env:USERPROFILE\.dotnet\tools"
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host 'Instalando la herramienta vpk de Velopack...'
    dotnet tool install -g vpk
}

$publishDir = 'artifacts\publish'
$releaseDir = 'artifacts\releases'
Remove-Item -Recurse -Force $publishDir -ErrorAction SilentlyContinue

Write-Host "Publicando Lector de Archivos $Version..."
dotnet publish $project -c Release -r win-x64 --self-contained -p:Version=$Version -o $publishDir
if ($LASTEXITCODE) { throw 'Falló dotnet publish.' }

Write-Host 'Generando el instalador con Velopack...'
vpk pack `
    --packId LectorArchivos `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe LectorArchivos.exe `
    --runtime win-x64 `
    --packTitle 'Lector de Archivos' `
    --packAuthors 'Reonburakku' `
    --outputDir $releaseDir
if ($LASTEXITCODE) { throw 'Falló vpk pack.' }

Write-Host ''
Write-Host "Listo. Archivos para la GitHub Release v$Version en $releaseDir :" -ForegroundColor Green
Get-ChildItem $releaseDir -File | Where-Object { $_.Name -match 'Setup\.exe$|Portable\.zip$' } |
    ForEach-Object { '  {0}  ({1:N1} MB)' -f $_.Name, ($_.Length / 1MB) }
