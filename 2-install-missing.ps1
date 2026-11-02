# 2-install-missing.ps1: Cai dat cong nghe con thieu
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
chcp 65001 >$null

$missingFile = "$PSScriptRoot\.missing-tools.json"
if (-not (Test-Path $missingFile)) {
    & "$PSScriptRoot\1-check-tools.ps1"
}

$missingList = Get-Content $missingFile -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $missingList -or $missingList.Count -eq 0) {
    Write-Host "No missing tools. You can run: .\3-run-services.ps1" -ForegroundColor Green
    exit 0
}

Write-Host "Installing missing tools: $($missingList -join ', ')" -ForegroundColor Yellow

foreach ($item in $missingList) {
    switch ($item) {
        "dotnet-sdk-10" {
            Write-Host "Installing .NET 10 SDK..." -ForegroundColor Yellow
            winget install Microsoft.DotNet.SDK.10 --accept-source-agreements --accept-package-agreements
        }
        "dotnet-ef" {
            Write-Host "Installing dotnet-ef CLI..." -ForegroundColor Yellow
            dotnet tool install --global dotnet-ef
            if ($LASTEXITCODE -ne 0) {
                dotnet tool update --global dotnet-ef
            }
        }
        "docker-desktop" {
            Write-Host "Installing Docker Desktop..." -ForegroundColor Yellow
            winget install Docker.DockerDesktop --accept-source-agreements --accept-package-agreements
        }
        "docker-start" {
            Write-Host "Starting Docker Desktop..." -ForegroundColor Yellow
            $dockerExe = "$env:LOCALAPPDATA\Programs\DockerDesktop\Docker Desktop.exe"
            if (Test-Path $dockerExe) {
                Start-Process $dockerExe
            } else {
                Start-Process "Docker Desktop"
            }
        }
        "nodejs" {
            Write-Host "Installing Node.js LTS..." -ForegroundColor Yellow
            winget install OpenJS.NodeJS.LTS --accept-source-agreements --accept-package-agreements
        }
        "npm" {
            Write-Host "Installing Node.js / npm..." -ForegroundColor Yellow
            winget install OpenJS.NodeJS.LTS --accept-source-agreements --accept-package-agreements
        }
        "git" {
            Write-Host "Installing Git..." -ForegroundColor Yellow
            winget install Git.Git --accept-source-agreements --accept-package-agreements
        }
    }
}

Write-Host "
Re-checking prerequisites..." -ForegroundColor Cyan
& "$PSScriptRoot\1-check-tools.ps1"