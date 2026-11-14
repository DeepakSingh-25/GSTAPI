IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030071229_InitialCreate'
)
BEGIN
    IF SCHEMA_ID(N'gst') IS NULL EXEC(N'CREATE SCHEMA [gst];');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030071229_InitialCreate'
)
BEGIN
    CREATE TABLE [gst].[tb_users] (
        [Id] uniqueidentifier NOT NULL,
        [FullName] nvarchar(150) NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [PasswordHash] nvarchar(200) NOT NULL,
        [Role] nvarchar(50) NOT NULL,
        [IsActive] bit NOT NULL,
        [LastLoginOn] datetime2 NULL,
        [CreatedOn] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [ModifiedOn] datetime2 NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_tb_users] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030071229_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_tb_users_Email] ON [gst].[tb_users] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030071229_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261030071229_InitialCreate', N'10.0.14');
END;

COMMIT;
GO

