# Move the local UserDb to its dedicated Docker SQL Server. Stop Identity first.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Set-Location $repoRoot
if (Get-NetTCPConnection -LocalPort 5004 -State Listen -ErrorAction SilentlyContinue) {
    throw 'Stop IdentityService on port 5004 before moving UserDb.'
}
$config = Get-Content 'src/IdentityService/IdentityService.Api/appsettings.json' -Raw | ConvertFrom-Json
$sourceBuilder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($config.ConnectionStrings.UserDb)
$sourceBuilder['Data Source'] = 'localhost,1434'
$sourceBuilder['Initial Catalog'] = 'master'
$targetBuilder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($sourceBuilder.ConnectionString)
$targetBuilder['Data Source'] = 'localhost,1435'
$source = New-Object System.Data.SqlClient.SqlConnection($sourceBuilder.ConnectionString)
$target = New-Object System.Data.SqlClient.SqlConnection($targetBuilder.ConnectionString)
function Sql($connection, [string]$text) {
    $cmd = $connection.CreateCommand()
    $cmd.CommandTimeout = 120
    $cmd.CommandText = $text
    return $cmd.ExecuteScalar()
}
function Invoke-DockerCommand([string[]]$DockerArgs) {
    & docker.exe @DockerArgs
    if ($LASTEXITCODE -ne 0) { throw 'Docker operation failed.' }
}
try {
    $source.Open()
    $target.Open()
    if ((Sql $source "SELECT DB_ID(N'UserDb')") -is [DBNull]) {
        if ((Sql $target "SELECT DB_ID(N'UserDb')") -is [DBNull]) { throw 'UserDb is missing on both servers.' }
        Write-Host 'UserDb is already on the dedicated server.'
        return
    }
    if ((Sql $target "SELECT DB_ID(N'UserDb')") -isnot [DBNull]) {
        throw 'Target UserDb already exists. Refusing to overwrite it.'
    }
    if ([int](Sql $source "SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE is_user_process=1 AND database_id=DB_ID(N'UserDb')") -gt 0) {
        throw 'Close connections to the old UserDb before moving it.'
    }
    $name = 'UserDb_move_' + [Guid]::NewGuid().ToString('N') + '.bak'
    $backupPath = '/var/opt/mssql/data/' + $name
    Sql $source "BACKUP DATABASE [UserDb] TO DISK=N'$backupPath' WITH COPY_ONLY,CHECKSUM; RESTORE VERIFYONLY FROM DISK=N'$backupPath' WITH CHECKSUM;" | Out-Null
    $localBackup = Join-Path ([System.IO.Path]::GetTempPath()) $name
    Invoke-DockerCommand @('cp', "swiftride-sqlserver:$backupPath", $localBackup)
    Invoke-DockerCommand @('cp', $localBackup, "swiftride-identity-sqlserver:$backupPath")
    Sql $target "RESTORE VERIFYONLY FROM DISK=N'$backupPath' WITH CHECKSUM; RESTORE DATABASE [UserDb] FROM DISK=N'$backupPath';" | Out-Null
    foreach ($table in @('Users','Roles','OutboxMessages','__EFMigrationsHistory')) {
        $order = if ($table -eq '__EFMigrationsHistory') { 'MigrationId' } else { 'Id' }
        $query = "SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(nvarchar(max),(SELECT * FROM UserDb.dbo.[$table] ORDER BY [$order] FOR XML RAW,BINARY BASE64))),2)"
        if ((Sql $source $query) -cne (Sql $target $query)) { throw "Verification failed for $table. Original database retained." }
    }
    # Do not leave two writable copies. The verified backup remains in both containers.
    Sql $source 'DROP DATABASE [UserDb];' | Out-Null
    Write-Host "UserDb moved and all tables verified. Backup retained: $backupPath"
} finally {
    $source.Dispose()
    $target.Dispose()
}
