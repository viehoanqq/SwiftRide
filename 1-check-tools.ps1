# 1-check-tools.ps1: Kiem tra cong nghe yeu cau va xuat bang Markdown
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
chcp 65001 >$null
$ErrorActionPreference = "SilentlyContinue"

$env:Path = [Environment]::GetEnvironmentVariable("Path", "User") + ";" + [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + $env:Path

$results = @()
$missingList = @()

# 1. .NET 10 SDK
$dotnetVersion = (dotnet --version) 2>$null
if ($LASTEXITCODE -eq 0 -and $dotnetVersion -match "^10\.") {
    $results += [PSCustomObject]@{
        "CongNghe"  = ".NET 10 SDK"
        "TinhTrang" = "Installed"
        "ChiTiet"   = "v$dotnetVersion"
    }
} else {
    $results += [PSCustomObject]@{
        "CongNghe"  = ".NET 10 SDK"
        "TinhTrang" = "Missing"
        "ChiTiet"   = if ($dotnetVersion) { "Current: v$dotnetVersion (Need 10.x)" } else { "Not installed" }
    }
    $missingList += "dotnet-sdk-10"
}

# 2. dotnet-ef CLI
$efList = (dotnet tool list --global) 2>$null
if ($LASTEXITCODE -eq 0 -and $efList -match "dotnet-ef") {
    $efVer = ($efList | Select-String "dotnet-ef\s+([0-9\.]+)").Matches.Groups[1].Value
    $results += [PSCustomObject]@{
        "CongNghe"  = "dotnet-ef CLI"
        "TinhTrang" = "Installed"
        "ChiTiet"   = if ($efVer) { "v$efVer" } else { "Ready" }
    }
} else {
    $results += [PSCustomObject]@{
        "CongNghe"  = "dotnet-ef CLI"
        "TinhTrang" = "Missing"
        "ChiTiet"   = "dotnet tool install --global dotnet-ef"
    }
    $missingList += "dotnet-ef"
}

# 3. Docker Desktop
$dockerVer = (docker --version) 2>$null
$dockerRunning = $false
if ($LASTEXITCODE -eq 0) {
    docker ps >$null 2>&1
    if ($LASTEXITCODE -eq 0) {
        $dockerRunning = $true
    }
}

if ($dockerRunning) {
    $results += [PSCustomObject]@{
        "CongNghe"  = "Docker Desktop"
        "TinhTrang" = "Running"
        "ChiTiet"   = "$dockerVer"
    }
} elseif ($dockerVer) {
    $results += [PSCustomObject]@{
        "CongNghe"  = "Docker Desktop"
        "TinhTrang" = "Stopped"
        "ChiTiet"   = "Daemon is not running"
    }
    $missingList += "docker-start"
} else {
    $results += [PSCustomObject]@{
        "CongNghe"  = "Docker Desktop"
        "TinhTrang" = "Missing"
        "ChiTiet"   = "Not installed"
    }
    $missingList += "docker-desktop"
}

# 4. Node.js
$nodeVer = (node --version) 2>$null
if ($LASTEXITCODE -eq 0 -and $nodeVer) {
    $results += [PSCustomObject]@{
        "CongNghe"  = "Node.js"
        "TinhTrang" = "Installed"
        "ChiTiet"   = "$nodeVer"
    }
} else {
    $results += [PSCustomObject]@{
        "CongNghe"  = "Node.js"
        "TinhTrang" = "Missing"
        "ChiTiet"   = "Not installed (Need >= 20)"
    }
    $missingList += "nodejs"
}

# 5. npm
$npmVer = (npm --version) 2>$null
if ($LASTEXITCODE -eq 0 -and $npmVer) {
    $results += [PSCustomObject]@{
        "CongNghe"  = "npm"
        "TinhTrang" = "Installed"
        "ChiTiet"   = "v$npmVer"
    }
} else {
    $results += [PSCustomObject]@{
        "CongNghe"  = "npm"
        "TinhTrang" = "Missing"
        "ChiTiet"   = "Not installed"
    }
    $missingList += "npm"
}

# 6. Git
$gitVer = (git --version) 2>$null
if ($LASTEXITCODE -eq 0 -and $gitVer) {
    $results += [PSCustomObject]@{
        "CongNghe"  = "Git"
        "TinhTrang" = "Installed"
        "ChiTiet"   = "$gitVer"
    }
} else {
    $results += [PSCustomObject]@{
        "CongNghe"  = "Git"
        "TinhTrang" = "Missing"
        "ChiTiet"   = "Not installed"
    }
    $missingList += "git"
}

$missingList | ConvertTo-Json | Set-Content -Path "$PSScriptRoot\.missing-tools.json" -Encoding UTF8

Write-Host ""
Write-Host "| Cong nghe | Tinh trang | Chi tiet |"
Write-Host "|---|---|---|"
foreach ($item in $results) {
    Write-Host "| $($item.CongNghe) | $($item.TinhTrang) | $($item.ChiTiet) |"
}
Write-Host ""

if ($missingList.Count -eq 0) {
    Write-Host "All prerequisites ready. Run: .\3-run-services.ps1" -ForegroundColor Green
} else {
    Write-Host "Missing items: $($missingList -join ', ')" -ForegroundColor Yellow
    Write-Host "Run .\2-install-missing.ps1 to install." -ForegroundColor Cyan
}