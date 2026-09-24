$ErrorActionPreference = 'Stop'
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " Gerando Pacote de Instalacao Autossuficiente" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

$OutDir = Join-Path $PSScriptRoot "PacoteInstalacao"
if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutDir | Out-Null

Write-Host "1. Compilando o projeto (Self-Contained / win-x64)..." -ForegroundColor Yellow
$ProjPath = Join-Path $PSScriptRoot "..\src\LabelPrinter.Worker\LabelPrinter.Worker.csproj"
dotnet publish $ProjPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $OutDir

Write-Host "2. Adicionando SumatraPDF ao pacote..." -ForegroundColor Yellow
$SumatraExe = Join-Path $PSScriptRoot "..\publish\SumatraPDF.exe"
if (Test-Path $SumatraExe) {
    Copy-Item $SumatraExe -Destination $OutDir -Force
} else {
    Write-Warning "SumatraPDF.exe nao encontrado na pasta publish!"
}

Write-Host "3. Copiando script de instalacao..." -ForegroundColor Yellow
$InstallScriptContent = @"
`$ErrorActionPreference = 'Stop'
if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Warning "Execute como Administrador!"
    Pause
    exit
}
`$InstallDir = "C:\LabelPrinter"

Write-Host "Copiando arquivos para `$InstallDir..." -ForegroundColor Yellow
if (Test-Path `$InstallDir) {
    Stop-Service -Name "LabelPrinterService" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
} else {
    New-Item -ItemType Directory -Path `$InstallDir | Out-Null
}
Copy-Item -Path "`$PSScriptRoot\*" -Destination `$InstallDir -Recurse -Force

Write-Host "Registrando Servico..." -ForegroundColor Yellow
`$serviceExe = Join-Path `$InstallDir "LabelPrinter.Worker.exe"
`$service = Get-Service -Name "LabelPrinterService" -ErrorAction SilentlyContinue
if (-not `$service) {
    New-Service -Name "LabelPrinterService" -BinaryPathName `$serviceExe -DisplayName "LabelPrinter Service" -Description "Impressao Automatica de Etiquetas" -StartupType Automatic | Out-Null
} else {
    Set-Service -Name "LabelPrinterService" -StartupType Automatic
}
Write-Host "Iniciando Servico..." -ForegroundColor Yellow
Start-Service -Name "LabelPrinterService"
Write-Host "INSTALACAO CONCLUIDA! O sistema esta rodando em segundo plano." -ForegroundColor Green
Pause
"@
Set-Content -Path (Join-Path $OutDir "install_no_cliente.ps1") -Value $InstallScriptContent

Write-Host "4. Compactando pacote..." -ForegroundColor Yellow
$ZipPath = Join-Path $PSScriptRoot "Instalador_Etiquetas.zip"
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path "$OutDir\*" -DestinationPath $ZipPath

Remove-Item $OutDir -Recurse -Force
Write-Host "Pacote gerado com sucesso em: $ZipPath" -ForegroundColor Green

