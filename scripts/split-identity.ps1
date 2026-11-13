# One-time local SQL Server cutover. Stop TripService and IdentityService first.
# Stage the legacy extraction on the Trip SQL Server, then move UserDb to port 1435.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Set-Location $repoRoot

foreach ($name in @('TripService.Api', 'IdentityService.Api')) {
    if (Get-Process -Name $name -ErrorAction SilentlyContinue) {
        throw "Stop $name before migrating."
    }
}
foreach ($process in Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'") {
    if ($process.CommandLine -match '(TripService|IdentityService)\.Api\.dll') {
        throw 'Stop the running Trip/Identity API before migrating.'
    }
}

function Invoke-Ef([string[]]$EfArgs) {
    & dotnet ef @EfArgs
    if ($LASTEXITCODE -ne 0) { throw 'EF migration failed; cutover stopped.' }
}

$tripProject = 'src/TripService/TripService.Infrastructure'
$tripStartup = 'src/TripService/TripService.Api'
$identityProject = 'src/IdentityService/IdentityService.Infrastructure'
$identityStartup = 'src/IdentityService/IdentityService.Api'
$tripConfig = Get-Content "$tripStartup/appsettings.json" -Raw | ConvertFrom-Json
$identityConfig = Get-Content "$identityStartup/appsettings.json" -Raw | ConvertFrom-Json
$tripConnection = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($tripConfig.ConnectionStrings.TripDb)
$identityConnection = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($identityConfig.ConnectionStrings.UserDb)
$identityConnection['Data Source'] = $tripConnection.DataSource
if ($tripConnection.InitialCatalog -ne 'TripDb' -or $identityConnection.InitialCatalog -ne 'UserDb' -or
    $tripConnection.DataSource -ne $identityConnection.DataSource) {
    throw 'This cutover supports TripDb and UserDb on the same configured SQL Server only.'
}

Invoke-Ef @('database', 'update', '--project', $identityProject, '--startup-project', $identityStartup, '--configuration', 'IdentitySplit', '--connection', $identityConnection.ConnectionString)
$masterBuilder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($tripConnection.ConnectionString)
$masterBuilder['Initial Catalog'] = 'master'
$master = New-Object System.Data.SqlClient.SqlConnection($masterBuilder.ConnectionString)
$master.Open()
try {
    $cmd = $master.CreateCommand()
    $cmd.CommandText = "SELECT DB_ID(N'TripDb')"
    $tripExists = $cmd.ExecuteScalar() -isnot [DBNull]
} finally { $master.Dispose() }
if (-not $tripExists) {
    Invoke-Ef @('database', 'update', 'AddDistanceToPickupKm', '--project', $tripProject, '--startup-project', $tripStartup, '--configuration', 'IdentitySplit')
}

$connection = New-Object System.Data.SqlClient.SqlConnection($tripConnection.ConnectionString)
$connection.Open()
try {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 120
    $command.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name = 'Users' AND schema_id = SCHEMA_ID('dbo')"
    if ([int]$command.ExecuteScalar() -gt 0) {
        # SQL Server chooses its configured default backup directory. Both backups are
        # verified before any data is copied or removed. Never overwrite an old backup.
        $stamp = [Guid]::NewGuid().ToString('N')
        foreach ($dbName in @('TripDb', 'UserDb')) {
            $command.CommandText = @"
DECLARE @dir nvarchar(4000) = CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultBackupPath'));
IF @dir IS NULL THROW 51001, 'SQL Server backup directory is not configured.', 1;
IF RIGHT(@dir, 1) NOT IN (N'/', N'\') SET @dir = @dir + CASE WHEN CHARINDEX(N'/', @dir) > 0 THEN N'/' ELSE N'\' END;
DECLARE @file nvarchar(4000) = @dir + N'${dbName}_before_identity_${stamp}.bak';
BACKUP DATABASE [$dbName] TO DISK = @file WITH COPY_ONLY, CHECKSUM;
RESTORE VERIFYONLY FROM DISK = @file WITH CHECKSUM;
SELECT @file;
"@
            $backupFile = $command.ExecuteScalar()
            Write-Host "Verified SQL Server backup: $backupFile"
        }
        $command.CommandText = Get-Content "$PSScriptRoot/copy-identity.sql" -Raw
        $command.ExecuteNonQuery() | Out-Null
        Write-Host 'Users, roles and registration outbox copied with original IDs.'
    }
} finally { $connection.Dispose() }

Invoke-Ef @('database', 'update', '--project', $tripProject, '--startup-project', $tripStartup, '--configuration', 'IdentitySplit')
& "$PSScriptRoot/move-userdb-container.ps1"
Write-Host 'Identity cutover complete. Restart Identity, Trip, Matching and Gateway.'
