# stop-services.ps1: Dung toan bo cac service va Docker containers
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
chcp 65001 >$null

Write-Host "Stopping dotnet and node processes..." -ForegroundColor Yellow
Get-Process -Name "dotnet", "node" -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "Stopping docker containers..." -ForegroundColor Yellow
docker compose down

Write-Host "Stopped all services." -ForegroundColor Green