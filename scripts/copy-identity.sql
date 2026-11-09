-- Run only during the maintenance window with both identity writers stopped.
-- Called after verified backups by split-identity.ps1. TripDb is authoritative
-- during this one-time cutover; do not run after IdentityService accepts traffic.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Hold source and destination locks until the copy is complete.
DECLARE @count bigint;
SELECT @count = COUNT_BIG(*) FROM dbo.Users WITH (TABLOCKX, HOLDLOCK);
SELECT @count = COUNT_BIG(*) FROM dbo.OutboxMessages WITH (TABLOCKX, HOLDLOCK);
SELECT @count = COUNT_BIG(*) FROM UserDb.dbo.Users WITH (TABLOCKX, HOLDLOCK);
SELECT @count = COUNT_BIG(*) FROM UserDb.dbo.OutboxMessages WITH (TABLOCKX, HOLDLOCK);

INSERT UserDb.dbo.Roles (Id, Name)
SELECT s.Id, s.Name FROM dbo.Roles s
WHERE NOT EXISTS (SELECT 1 FROM UserDb.dbo.Roles d WHERE d.Id = s.Id);
IF EXISTS (SELECT Id, Name FROM dbo.Roles EXCEPT SELECT Id, Name FROM UserDb.dbo.Roles)
    THROW 51001, 'Role definitions conflict; no data was moved.', 1;

IF EXISTS (
    SELECT 1 FROM dbo.Users s JOIN UserDb.dbo.Users d ON d.Phone = s.Phone AND d.Id <> s.Id
) THROW 51001, 'User phone conflicts with a different ID in UserDb; no data was moved.', 1;

UPDATE d SET RoleId=s.RoleId, FullName=s.FullName, Phone=s.Phone,
    PasswordHash=s.PasswordHash, IsActive=s.IsActive, CreatedAt=s.CreatedAt
FROM UserDb.dbo.Users d JOIN dbo.Users s ON d.Id=s.Id;
INSERT UserDb.dbo.Users (Id, RoleId, FullName, Phone, PasswordHash, IsActive, CreatedAt)
SELECT s.Id, s.RoleId, s.FullName, s.Phone, s.PasswordHash, s.IsActive, s.CreatedAt
FROM dbo.Users s WHERE NOT EXISTS (SELECT 1 FROM UserDb.dbo.Users d WHERE d.Id=s.Id);

IF EXISTS (
    SELECT s.Id, s.Type, s.Payload, s.CorrelationId, s.OccurredAt, s.ProcessedAt
    FROM dbo.OutboxMessages s JOIN UserDb.dbo.OutboxMessages d ON s.Id=d.Id
    EXCEPT SELECT Id, Type, Payload, CorrelationId, OccurredAt, ProcessedAt FROM UserDb.dbo.OutboxMessages
) THROW 51001, 'Outbox content conflicts; no data was moved.', 1;
INSERT UserDb.dbo.OutboxMessages (Id, Type, Payload, CorrelationId, OccurredAt, ProcessedAt)
SELECT s.Id, s.Type, s.Payload, s.CorrelationId, s.OccurredAt, s.ProcessedAt
FROM dbo.OutboxMessages s WHERE NOT EXISTS (SELECT 1 FROM UserDb.dbo.OutboxMessages d WHERE d.Id=s.Id);

COMMIT;
