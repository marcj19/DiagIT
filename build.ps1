param([string]$OutDir = "dist")
# Compila o DiagIT com o compilador C# que ja vem no Windows (.NET Framework 4.x).
# Nao precisa instalar nada. Uso:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "Compilador C# nao encontrado em $csc" }

$dist = Join-Path $root $OutDir
New-Item -ItemType Directory -Force $dist | Out-Null

$icon = Join-Path $root 'assets\diagit.ico'
$iconArg = @()
if (Test-Path $icon) { $iconArg = @("/win32icon:$icon") }

$src = Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.cs | ForEach-Object { $_.FullName }
$out = Join-Path $dist 'DiagIT.exe'
$manifest = Join-Path $root 'assets\app.manifest'

$vendor = Join-Path $root 'vendor\lhm'
$refs = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Management.dll',
          (Join-Path $vendor 'System.Numerics.Vectors.dll'))
$cscArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001','/warn:3','/unsafe',
             "/out:$out", "/win32manifest:$manifest")
$cscArgs += ($refs | ForEach-Object { "/reference:$_" })
$cscArgs += $iconArg
$cscArgs += $src

& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "Falha na compilacao" }

# primeira compilacao: gera o icone com o proprio app e recompila com ele embutido
if (-not (Test-Path $icon)) {
    New-Item -ItemType Directory -Force (Split-Path $icon) | Out-Null
    Start-Process -FilePath $out -ArgumentList @("--make-icon", $icon) -Wait
    if (Test-Path $icon) {
        $cscArgs += "/win32icon:$icon"
        & $csc $cscArgs
        if ($LASTEXITCODE -ne 0) { throw "Falha na compilacao (com icone)" }
    }
}
# bibliotecas de sensores (LibreHardwareMonitor) ao lado do exe
$sens = Join-Path $dist 'sensores'
New-Item -ItemType Directory -Force $sens | Out-Null
Copy-Item (Join-Path $vendor '*.dll') $sens -Force
Copy-Item (Join-Path $vendor 'System.Numerics.Vectors.dll') $dist -Force
Write-Host "OK -> $out" -ForegroundColor Green
