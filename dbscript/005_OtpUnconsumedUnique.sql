-- 005_OtpUnconsumedUnique: ek user + purpose ka sirf ek unconsumed OTP (parallel resend / login race).
-- PEHLE dbscript/005_OtpUnconsumedUnique_precheck.sql chalao; duplicate rows hon to wahan ka cleanup, warna CREATE INDEX fail hoga.
-- XACT_ABORT: koi bhi error -> poora rollback (warna index fail hone par bhi history row commit ho jaati).
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102072120_OtpUnconsumedUnique'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_tb_otp_codes_UserId_Purpose_Unconsumed] ON [dbo].[tb_otp_codes] ([UserId], [Purpose]) WHERE [ConsumedOn] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102072120_OtpUnconsumedUnique'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261102072120_OtpUnconsumedUnique', N'10.0.14');
END;

COMMIT;
GO

