BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031105715_AuthLogin'
)
BEGIN
    CREATE TABLE [dbo].[tb_refresh_tokens] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [TokenHash] nvarchar(64) NOT NULL,
        [ExpiresOn] datetime2 NOT NULL,
        [RememberMe] bit NOT NULL,
        [RevokedOn] datetime2 NULL,
        [ReplacedByTokenId] uniqueidentifier NULL,
        [CreatedByIp] nvarchar(45) NULL,
        [CreatedOn] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [ModifiedOn] datetime2 NULL,
        [ModifiedBy] nvarchar(max) NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_tb_refresh_tokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_tb_refresh_tokens_tb_users_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[tb_users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031105715_AuthLogin'
)
BEGIN
    CREATE UNIQUE INDEX [IX_tb_refresh_tokens_TokenHash] ON [dbo].[tb_refresh_tokens] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031105715_AuthLogin'
)
BEGIN
    CREATE INDEX [IX_tb_refresh_tokens_UserId_RevokedOn] ON [dbo].[tb_refresh_tokens] ([UserId], [RevokedOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031105715_AuthLogin'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261031105715_AuthLogin', N'10.0.14');
END;

COMMIT;
GO

