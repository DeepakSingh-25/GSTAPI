SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    DROP INDEX [IX_tb_users_Email] ON [gst].[tb_users];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    ALTER TABLE [gst].[tb_users] ADD [CompanyId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    ALTER TABLE [gst].[tb_users] ADD [EmailVerified] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    ALTER TABLE [gst].[tb_users] ADD [FailedLoginCount] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    ALTER TABLE [gst].[tb_users] ADD [FirstName] nvarchar(50) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    ALTER TABLE [gst].[tb_users] ADD [JobRole] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    ALTER TABLE [gst].[tb_users] ADD [LastName] nvarchar(50) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    ALTER TABLE [gst].[tb_users] ADD [LockoutEnd] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    ALTER TABLE [gst].[tb_users] ADD [MobileNo] nvarchar(10) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    ALTER TABLE [gst].[tb_users] ADD [OnboardingCompleted] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    ALTER TABLE [gst].[tb_users] ADD [PasswordChangedOn] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    EXEC(N'UPDATE [gst].[tb_users] SET [Role] = N''Owner / Admin'' WHERE [Role] = N''Admin'';')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    EXEC(N'UPDATE [gst].[tb_users] SET [FirstName] = LEFT(CASE WHEN CHARINDEX(N'' '', [FullName]) > 0 THEN LEFT([FullName], CHARINDEX(N'' '', [FullName]) - 1) ELSE [FullName] END, 50), [LastName] = LEFT(CASE WHEN CHARINDEX(N'' '', [FullName]) > 0 THEN LTRIM(SUBSTRING([FullName], CHARINDEX(N'' '', [FullName]) + 1, 150)) ELSE N'''' END, 50), [EmailVerified] = 1 WHERE [FirstName] = N'''';')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    CREATE TABLE [gst].[tb_companies] (
        [Id] uniqueidentifier NOT NULL,
        [LegalName] nvarchar(100) NOT NULL,
        [TradeName] nvarchar(100) NULL,
        [Gstin] nvarchar(15) NOT NULL,
        [Pan] nvarchar(10) NOT NULL,
        [RegistrationType] nvarchar(20) NOT NULL,
        [State] nvarchar(50) NOT NULL,
        [StateCode] nvarchar(2) NOT NULL,
        [BusinessSize] nvarchar(20) NULL,
        [Industry] nvarchar(100) NULL,
        [AddressLine1] nvarchar(200) NULL,
        [AddressLine2] nvarchar(200) NULL,
        [City] nvarchar(100) NULL,
        [Pincode] nvarchar(6) NULL,
        [Email] nvarchar(256) NULL,
        [Phone] nvarchar(15) NULL,
        [Website] nvarchar(200) NULL,
        [BankName] nvarchar(100) NULL,
        [AccountNo] nvarchar(20) NULL,
        [Ifsc] nvarchar(11) NULL,
        [Branch] nvarchar(100) NULL,
        [TermsAndConditions] nvarchar(2000) NULL,
        [CreatedOn] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [ModifiedOn] datetime2 NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_tb_companies] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    CREATE TABLE [gst].[tb_otp_codes] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [Purpose] nvarchar(20) NOT NULL,
        [CodeHash] varchar(64) NOT NULL,
        [ExpiresOn] datetime2 NOT NULL,
        [Attempts] int NOT NULL,
        [VerifiedOn] datetime2 NULL,
        [ConsumedOn] datetime2 NULL,
        [CreatedOn] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [ModifiedOn] datetime2 NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_tb_otp_codes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_tb_otp_codes_tb_users_UserId] FOREIGN KEY ([UserId]) REFERENCES [gst].[tb_users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    CREATE INDEX [IX_tb_users_CompanyId] ON [gst].[tb_users] ([CompanyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_tb_users_Email] ON [gst].[tb_users] ([Email]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_tb_users_MobileNo] ON [gst].[tb_users] ([MobileNo]) WHERE [MobileNo] IS NOT NULL AND [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_tb_companies_Gstin] ON [gst].[tb_companies] ([Gstin]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    CREATE INDEX [IX_tb_otp_codes_Email_Purpose_CreatedOn] ON [gst].[tb_otp_codes] ([Email], [Purpose], [CreatedOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    CREATE INDEX [IX_tb_otp_codes_UserId] ON [gst].[tb_otp_codes] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    ALTER TABLE [gst].[tb_users] ADD CONSTRAINT [FK_tb_users_tb_companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [gst].[tb_companies] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031051124_AuthRegister'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261031051124_AuthRegister', N'10.0.14');
END;

COMMIT;
GO

