<#
.SYNOPSIS
Instalador Automático - LabelPrinterService
#>

# Verifica se é Administrador
if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Warning "Você precisa executar este script como Administrador!"
    Write-Host "Por favor, clique com o botão direito no arquivo install.ps1 e selecione 'Executar com o PowerShell' -> 'Sim'."
    Pause
    exit
}

$InstallDir = "C:\LabelPrinter"
$SourceDir = Join-Path $PSScriptRoot "..\publish"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " Instalador - Serviço de Etiquetas Shopee" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# 1. Instalar GhostScript (se houver na pasta instalar)
$gsInstaller = Get-ChildItem -Path $PSScriptRoot -Filter "gs*w64.exe" | Select-Object -First 1
if ($gsInstaller) {
    Write-Host "[1/4] Instalando GhostScript silenciosamente..." -ForegroundColor Yellow
    Start-Process -FilePath $gsInstaller.FullName -ArgumentList "/S" -Wait -NoNewWindow
    Write-Host "GhostScript instalado." -ForegroundColor Green
} else {
    Write-Host "[1/4] Instalador do GhostScript não encontrado na pasta atual. Ignorando." -ForegroundColor Yellow
}

# 2. Criar diretório e copiar arquivos
Write-Host "[2/4] Copiando arquivos do sistema para $InstallDir..." -ForegroundColor Yellow
if (Test-Path $InstallDir) {
    # Para o serviço se já existir para não dar erro de arquivo em uso
    $service = Get-Service -Name "LabelPrinterService" -ErrorAction SilentlyContinue
    if ($service) {
        Stop-Service -Name "LabelPrinterService" -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
} else {
    New-Item -ItemType Directory -Path $InstallDir | Out-Null
}

Copy-Item -Path "$SourceDir\*" -Destination $InstallDir -Recurse -Force
Write-Host "Arquivos copiados com sucesso." -ForegroundColor Green

# 3. Criar Serviço do Windows
Write-Host "[3/4] Registrando Serviço do Windows..." -ForegroundColor Yellow
$serviceExe = Join-Path $InstallDir "LabelPrinter.Worker.exe"

$service = Get-Service -Name "LabelPrinterService" -ErrorAction SilentlyContinue
if (-not $service) {
    New-Service -Name "LabelPrinterService" -BinaryPathName $serviceExe -DisplayName "LabelPrinter Service" -Description "Serviço de Impressão Automática de Etiquetas Shopee" -StartupType Automatic | Out-Null
} else {
    Set-Service -Name "LabelPrinterService" -StartupType Automatic
}
Write-Host "Serviço registrado." -ForegroundColor Green

# 4. Iniciar Serviço
Write-Host "[4/4] Iniciando o serviço..." -ForegroundColor Yellow
Start-Service -Name "LabelPrinterService"
Write-Host "Serviço iniciado com sucesso!" -ForegroundColor Green

Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " INSTALAÇÃO CONCLUÍDA COM SUCESSO!       " -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "O sistema já está rodando em segundo plano."
Write-Host "Qualquer PDF ou ZIP baixado na pasta Downloads será impresso automaticamente."
Pause
