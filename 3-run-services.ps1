# 3-run-services.ps1: Khoi chay docker, backend services va frontend tren cac terminal rieng
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
chcp 65001 >$null

$env:Path = [Environment]::GetEnvironmentVariable("Path", "User") + ";" + [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + $env:Path

Write-Host "Starting SwiftRide..." -ForegroundColor Cyan

# 1. Docker
Write-Host "[1/4] Checking Docker..." -ForegroundColor Yellow
docker ps >$null 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Docker is not running. Starting Docker Desktop..." -ForegroundColor Red
    $dockerExe = "$env:LOCALAPPDATA\Programs\DockerDesktop\Docker Desktop.exe"
    if (Test-Path $dockerExe) {
        Start-Process $dockerExe
    } else {
        Start-Process "Docker Desktop"
    }
    Write-Host "Please wait for Docker Desktop to start, then re-run this script." -ForegroundColor Yellow
    exit 1
}

docker compose up -d
if ($LASTEXITCODE -ne 0) {
    Write-Host "docker compose up failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Docker containers ready." -ForegroundColor Green

# 2. Frontend dependencies
Write-Host "[2/4] Checking Frontend..." -ForegroundColor Yellow
if (-not (Test-Path "$PSScriptRoot\frontend\node_modules")) {
    Push-Location "$PSScriptRoot\frontend"
    npm install
    Pop-Location
}
Write-Host "Frontend ready." -ForegroundColor Green

# 3. Backend Services
Write-Host "[3/4] Launching Backend Services..." -ForegroundColor Yellow

# Identity Service (:5004)
$identityCmd = "dotnet run --project '$PSScriptRoot\src\IdentityService\IdentityService.Api'"
Start-Process powershell -WindowStyle Hidden -ArgumentList "-NoExit", "-Command", $identityCmd

# Trip Service (:5001)
$tripCmd = "[Console]::Title = 'SwiftRide - Trip Service (:5001)'; Write-Host 'TRIP SERVICE (:5001)' -ForegroundColor Green; dotnet run --project '$PSScriptRoot\src\TripService\TripService.Api'"
Start-Process powershell -ArgumentList "-NoExit", "-Command", $tripCmd

# Matching Service (:5002)
$matchCmd = "[Console]::Title = 'SwiftRide - Matching Service (:5002)'; Write-Host 'MATCHING SERVICE (:5002)' -ForegroundColor Cyan; dotnet run --project '$PSScriptRoot\src\MatchingService\MatchingService.Api'"
Start-Process powershell -ArgumentList "-NoExit", "-Command", $matchCmd

# Payment Service (:5003)
$payCmd = "[Console]::Title = 'SwiftRide - Payment Service (:5003)'; Write-Host 'PAYMENT SERVICE (:5003)' -ForegroundColor Yellow; dotnet run --project '$PSScriptRoot\src\PaymentService\PaymentService.Api'"
Start-Process powershell -ArgumentList "-NoExit", "-Command", $payCmd

# API Gateway (:5000)
$gwCmd = "[Console]::Title = 'SwiftRide - API Gateway (:5000)'; Write-Host 'API GATEWAY (:5000)' -ForegroundColor Magenta; dotnet run --project '$PSScriptRoot\src\ApiGateway'"
Start-Process powershell -ArgumentList "-NoExit", "-Command", $gwCmd

Write-Host "Backend services launched." -ForegroundColor Green

# 4. Frontend (:5173)
Write-Host "[4/4] Launching Frontend..." -ForegroundColor Yellow
$feCmd = "[Console]::Title = 'SwiftRide - Frontend (:5173)'; Set-Location '$PSScriptRoot\frontend'; Write-Host 'FRONTEND (:5173)' -ForegroundColor Blue; npm run dev"
Start-Process powershell -ArgumentList "-NoExit", "-Command", $feCmd

Write-Host "Frontend launched." -ForegroundColor Green

# URLs
Write-Host "
System URLs:" -ForegroundColor Cyan
Write-Host "Web Frontend:       http://localhost:5173" -ForegroundColor White
Write-Host "Gateway Health:     http://localhost:5000/health" -ForegroundColor White
Write-Host "Identity Swagger:   http://localhost:5004/swagger" -ForegroundColor White
Write-Host "Trip Swagger:       http://localhost:5001/swagger" -ForegroundColor White
Write-Host "Matching Swagger:   http://localhost:5002/swagger" -ForegroundColor White
Write-Host "Payment Swagger:    http://localhost:5003/swagger" -ForegroundColor White
Write-Host "RabbitMQ Dashboard: http://localhost:15672 (swiftride / swiftride)" -ForegroundColor White
Write-Host "Stop command:       .\stop-services.ps1" -ForegroundColor DarkGray