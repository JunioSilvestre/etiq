$ErrorActionPreference = 'Stop'
if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Warning "Execute como Administrador!"
    Pause
    exit
}
$InstallDir = "C:\LabelPrinter"

Write-Host "Instalando dependencias (GhostScript)..." -ForegroundColor Yellow
$gsInstaller = Get-ChildItem -Path $PSScriptRoot -Filter "gs*w64.exe" | Select-Object -First 1
if ($gsInstaller) {
    Start-Process -FilePath $gsInstaller.FullName -ArgumentList "/S" -Wait -NoNewWindow
    Write-Host "GhostScript instalado." -ForegroundColor Green
}

Write-Host "Copiando arquivos para $InstallDir..." -ForegroundColor Yellow
if (Test-Path $InstallDir) {
    Stop-Service -Name "LabelPrinterService" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
} else {
    New-Item -ItemType Directory -Path $InstallDir | Out-Null
}
Copy-Item -Path "$PSScriptRoot\*" -Destination $InstallDir -Recurse -Force

Write-Host "Registrando Servico..." -ForegroundColor Yellow
$serviceExe = Join-Path $InstallDir "LabelPrinter.Worker.exe"
$service = Get-Service -Name "LabelPrinterService" -ErrorAction SilentlyContinue
if (-not $service) {
    New-Service -Name "LabelPrinterService" -BinaryPathName $serviceExe -DisplayName "LabelPrinter Service" -Description "Impressao Automatica de Etiquetas" -StartupType Automatic | Out-Null
} else {
    Set-Service -Name "LabelPrinterService" -StartupType Automatic
}
Write-Host "Iniciando Servico..." -ForegroundColor Yellow
Start-Service -Name "LabelPrinterService"
Write-Host "INSTALACAO CONCLUIDA! O sistema esta rodando em segundo plano." -ForegroundColor Green
Pause
