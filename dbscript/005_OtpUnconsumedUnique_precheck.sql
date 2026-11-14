-- 005_OtpUnconsumedUnique PRE-CHECK — 005_OtpUnconsumedUnique.sql se PEHLE chalao.
-- Naya filtered unique index (UserId, Purpose) WHERE ConsumedOn IS NULL tabhi banega
-- jab kisi user + purpose ke ek se zyada unconsumed OTP na hon (soft-deleted rows bhi gine jaate hain).

-- STEP 1 (read-only): duplicate unconsumed OTP dhoondo. 0 rows = seedha 005 chalao.
SELECT o.[UserId], o.[Purpose], COUNT(*) AS [UnconsumedCount],
       MIN(o.[CreatedOn]) AS [OldestCreatedOn], MAX(o.[CreatedOn]) AS [NewestCreatedOn]
FROM [dbo].[tb_otp_codes] AS o
WHERE o.[ConsumedOn] IS NULL
GROUP BY o.[UserId], o.[Purpose]
HAVING COUNT(*) > 1
ORDER BY [UnconsumedCount] DESC;
GO

-- STEP 2 (sirf STEP 1 mein rows aayi hon to): har UserId + Purpose ka sabse naya OTP chhod kar
-- baaki ko consume karo. App bhi resend pe yahi karta hai (purane consume, naya chalu).
-- Comment hata kar chalao; phir STEP 1 dobara chalao — 0 rows aani chahiye.
/*
SET XACT_ABORT ON;
BEGIN TRANSACTION;

WITH ranked AS (
    SELECT o.[Id],
           ROW_NUMBER() OVER (PARTITION BY o.[UserId], o.[Purpose]
                              ORDER BY o.[CreatedOn] DESC, o.[Id] DESC) AS rn
    FROM [dbo].[tb_otp_codes] AS o
    WHERE o.[ConsumedOn] IS NULL
)
UPDATE o
SET o.[ConsumedOn] = SYSUTCDATETIME(),
    o.[ModifiedOn] = SYSUTCDATETIME(),
    o.[ModifiedBy] = N'dbscript/005_precheck'
FROM [dbo].[tb_otp_codes] AS o
JOIN ranked AS r ON r.[Id] = o.[Id]
WHERE r.rn > 1;

SELECT @@ROWCOUNT AS [ConsumedRows];
COMMIT;
*/
GO
