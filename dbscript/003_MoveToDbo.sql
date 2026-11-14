BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031060130_MoveToDbo'
)
BEGIN
    ALTER SCHEMA [dbo] TRANSFER [gst].[tb_users];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031060130_MoveToDbo'
)
BEGIN
    ALTER SCHEMA [dbo] TRANSFER [gst].[tb_otp_codes];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031060130_MoveToDbo'
)
BEGIN
    ALTER SCHEMA [dbo] TRANSFER [gst].[tb_companies];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031060130_MoveToDbo'
)
BEGIN
    IF SCHEMA_ID(N'gst') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID(N'gst')) EXEC(N'DROP SCHEMA [gst]');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031060130_MoveToDbo'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261031060130_MoveToDbo', N'10.0.14');
END;

COMMIT;
GO

