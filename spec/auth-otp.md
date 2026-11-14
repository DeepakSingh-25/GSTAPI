# Spec: POST /api/auth/verify-otp + POST /api/auth/resend-otp (API)

Signup ke baad email OTP verify karna, aur OTP dobara bhejna. Verify hone par login jaisi hi session cookies set hoti hain.

Login spec: `spec/auth-login.md` (cookies, `MeDto`, rate limit). Register spec: `spec/auth-register.md` (OTP rules, `sentTo` masking).

---

## 1. Endpoints

| Route | Auth | Rate limit | File |
|---|---|---|---|
| `POST /api/auth/verify-otp` | `[AllowAnonymous]` | `auth` (10 / min / IP) + per-key (10 / 10 min / otpId) | `Features/Auth/VerifyOtpCommand.cs` |
| `POST /api/auth/resend-otp` | `[AllowAnonymous]` | `auth` (10 / min / IP) + per-key (10 / 10 min / otpId) | `Features/Auth/ResendOtpCommand.cs` |

Controller: `ReturnResult(await Mediator.Send(command, cancellationToken))`.

### Rate limits (do layer)

1. **Per-IP** `auth` policy (`Extensions/ServiceExtensions.cs`): 10 / min. Partition key `ClientPartitionKey(ip)` —
   IPv4 as-is, IPv4-mapped IPv6 -> IPv4, IPv6 -> `/64` prefix (`2001:db8:1:2::/64`), IP nahi -> `unknown`. `refresh` policy bhi yahi key use karti hai.
2. **Per-key** `[ServiceFilter<AuthKeyRateLimitFilter>]` (`Filters/`) on verify-otp, resend-otp, login — model binding ke baad command se key:
   `otp:{otpId}` (verify + resend ek hi bucket) / `user:{normalized userId}` (login). Khali otpId / userId pe limit nahi (validation 400 deta hai).
   Fixed window, `Auth:KeyRateLimitPermits = 10`, `Auth:KeyRateLimitWindowMinutes = 10`. Over limit -> **429** `Too many requests. Please try again later.`
   (key log nahi hoti). State process memory mein — multi-instance pe har instance ka alag counter.

Config (`AuthConfig`, `appsettings.json` -> `Auth`): `OtpResendCooldownSeconds = 60`, `OtpMaxPerHour = 5`. `OtpRules.MaxAttempts = 5` (constant).

## 2. verify-otp

### Request

```json
{ "otpId": "3f6c1e0a-6a1b-4c39-9f0e-2b7d6f1c9a11", "otpValue": "482913", "rememberMe": true }
```

```csharp
public record VerifyOtpCommand(Guid OtpId, string OtpValue, bool RememberMe) : IRequest<ApiResponse<OtpVerifyResultDto>>;
```

Normalize: `OtpValue` -> `Trim()`.

### Validation -> 400

| Field | Rule | Message |
|---|---|---|
| otpId | not empty | `OTP not found. Please log in again.` |
| otpValue | `^[0-9]{6}$` | `Enter the 6 digit OTP` |

### Handler steps (isi order mein)

1. OTP id se dhoondo (`Purpose = Signup`, tracked). Nahi mila / `ConsumedOn != null` / `ExpiresOn <= now` / `Attempts >= 5`
   -> **200 `Expired`**. Sab case mein same jawab, koi detail leak nahi. Save nahi.
2. Hash compare: `OtpGenerator.Hash(otp.Id, code)` vs `CodeHash`, `CryptographicOperations.FixedTimeEquals` (bytes pe).
   Galat -> `Attempts++`, save ek baar -> **200 `IncorrectOTP`**, `attemptsLeft = 5 - Attempts`. Warning log (sirf `UserId`).
3. User (tracked + Company). `null` (soft-deleted) -> **200 `Expired`**. `!IsActive` -> **403** (OTP consume nahi, save nahi).
4. Success: `otp.VerifiedOn = otp.ConsumedOn = now`; user ke baaki unconsumed Signup OTP bhi consume;
   `EmailVerified = true`; `LastLoginOn = now`; `sessions.CreateAsync(user, rememberMe)`; **save ek baar**;
   `sessions.WriteCookies(session)` -> **200 `Success`** + `user` (`MeDto`).

### Response (200)

```json
{ "response": { "status": "Success", "user": { "...MeDto..." } }, "code": 200, "description": "OK", "error": null }
{ "response": { "status": "IncorrectOTP", "attemptsLeft": 3 }, "code": 200, "description": "OK", "error": null }
{ "response": { "status": "Expired" }, "code": 200, "description": "OK", "error": null }
```

```csharp
public static class OtpVerifyStatus { Success = "Success"; IncorrectOtp = "IncorrectOTP"; Expired = "Expired"; }
// Null fields JSON mein nahi aate
public record OtpVerifyResultDto(string Status, MeDto? User = null, int? AttemptsLeft = null);
```

### Errors

| Code | Kab | `error` |
|---|---|---|
| 400 | Validation | Section 2 ke messages |
| 403 | User inactive | `Your account is inactive. Please contact your administrator.` |
| 429 | Rate limit | `Too many requests. Please try again later.` |

## 3. resend-otp

Input `otpId` hai, email **nahi** — taaki kisi bhi inbox ko spam / email enumeration na ho.

### Request

```json
{ "otpId": "3f6c1e0a-6a1b-4c39-9f0e-2b7d6f1c9a11" }
```

```csharp
public record ResendOtpCommand(Guid OtpId) : IRequest<ApiResponse<OtpSentDto>>;
```

### Handler steps

1. Purana OTP id se (Signup, kisi bhi state mein). Nahi mila, ya user nahi mila -> **404** `OTP not found. Please log in again.`
2. `user.EmailVerified` -> **409** `Email is already verified. Please log in.`
3. Limits ((Email, Purpose, CreatedOn) index pe):
   - user ka latest Signup OTP `OtpResendCooldownSeconds` (60 s) se kam purana -> **429** `Please wait {n} seconds before requesting a new OTP.` (`n` = bache seconds, upar round)
   - pichhle 60 min mein Signup OTP count `>= OtpMaxPerHour` (5) -> **429** `Too many OTP requests. Please try again later.`
4. User ke saare unconsumed Signup OTP `ConsumedOn = now`; naya OTP `SignupOtp.Create(user, now)` se (register / login ke saath shared helper); save ek baar;
   phir email (register jaisa subject / body). Email fail -> `Error` log (`UserId` + exception type), response phir bhi 200.
   Save pe `DuplicateKeyException` (`DbIndexes.OtpUnconsumed`) = parallel request ne abhi OTP banaya -> **429**
   `Please wait {OtpResendCooldownSeconds} seconds before requesting a new OTP.`, email nahi. Koi aur index -> throw.
5. **200** `OtpSentDto(otpId, sentTo = Masking.MaskEmail(email), expiresOn)` (UTC kind, JSON mein `Z`).

**Login pe bhi yahi limits** (`SignupOtpLimits.CheckAsync`, shared helper): unverified user ka koi chalu OTP na ho aur limit lagi ho
to login naya OTP / email **nahi** banata — sabse naya `otpId` (exhausted bhi) hi `OTPRequired` mein lautta hai; verify pe wo `Expired` dega
aur user cooldown ke baad Resend karega. Warna "5 galat guess → login → naya OTP" se attempts limit ghoom jaati (security audit H1).
Naya OTP banane se pehle login bhi user ke purane unconsumed (expired / exhausted) Signup OTP consume karta hai (same save);
`DuplicateKeyException` (`OtpUnconsumed`) pe sabse naya `otpId` lautata hai, email nahi.

### Unconsumed OTP unique index (parallel race)

`IX_tb_otp_codes_UserId_Purpose_Unconsumed` = unique `(UserId, Purpose) WHERE [ConsumedOn] IS NULL` (`DbIndexes.OtpUnconsumed`).
Limit check + insert atomic nahi the — N parallel resend / login se N chalu OTP (N×5 guesses). Ab DB ek hi unconsumed OTP rakhta hai.
- Register: naya user, pehla OTP — conflict nahi. Verify: consume karta hai, insert nahi. Resend / login: purane consume + naya insert ek save mein
  (EF `UPDATE` pehle, `INSERT` baad — endpoint tests SQLite partial index pe ye cover karte hain).
- `IX_tb_otp_codes_UserId` (FK) alag se rakha hai — filtered index consumed rows cover nahi karta.
- Migration `OtpUnconsumedUnique`, script `dbscript/005_OtpUnconsumedUnique.sql` (`SET XACT_ABORT ON`).
  **Pehle** `dbscript/005_OtpUnconsumedUnique_precheck.sql` STEP 1 (read-only duplicate check); rows aayein to STEP 2 cleanup
  (har UserId + Purpose ka sabse naya chhod kar baaki consume), phir 005.
- Login race mein haarne wali request ka `FailedLoginCount = 0` reset bhi rollback hota hai — jeetne wali (sahi password) ne wahi save kiya.

Note: `CreatedOn` ko `ApplicationDbContext.SaveChangesAsync` `DateTime.UtcNow` se set karta hai (TimeProvider se nahi). Limit queries `TimeProvider` ke `now` se compare karti hain —
tests mein `FakeTimeProvider` aur DB ki `CreatedOn` ka mismatch dhyan se handle karo.

### Errors

| Code | Kab | `error` |
|---|---|---|
| 404 | OTP / user nahi | `OTP not found. Please log in again.` |
| 409 | Already verified | `Email is already verified. Please log in.` |
| 429 | Cooldown | `Please wait {n} seconds before requesting a new OTP.` |
| 429 | Hourly cap | `Too many OTP requests. Please try again later.` |
| 429 | Parallel request ne OTP banaya | `Please wait 60 seconds before requesting a new OTP.` |
| 429 | IP / per-key rate limit | `Too many requests. Please try again later.` |

## 4. Test cases

verify-otp:
1. Sahi OTP -> 200 `Success`, `EmailVerified = true`, `LastLoginOn` set, OTP `VerifiedOn`/`ConsumedOn` set, dono cookies, save ek baar
2. Galat OTP -> 200 `IncorrectOTP`, `attemptsLeft = 4`, `Attempts = 1`, cookie nahi
3. 5th galat -> `attemptsLeft = 0`; uske baad sahi code bhi -> `Expired`
4. Expired / consumed / unknown otpId -> 200 `Expired`, save nahi
5. User inactive -> 403, OTP consumed nahi, cookie nahi
6. User soft-deleted -> `Expired`
7. Success pe user ke baaki unconsumed Signup OTP bhi consumed
8. `rememberMe` false / true -> refresh cookie session / 30 din
9. Validation: otpId empty, `12345`, `abcdef`, `1234567` -> 400; `" 123456 "` trim ho kar chalega
10. Log mein OTP / email nahi

resend-otp:
1. Valid -> 200, naya OTP, purane consumed, email gaya, `sentTo` masked, `expiresOn` UTC
2. Unknown otpId -> 404; user missing -> 404
3. Already verified -> 409
4. 60 s ke andar -> 429 with `n`; 60 s baad -> 200
5. Ghante mein 5 OTP ho chuke -> 429 `Too many OTP requests...`
6. Email fail -> 200, `Error` log, `UserId` + exception type hi
7. Purana OTP ab verify nahi hota (consumed -> `Expired`)
8. Register → 5 galat verify → login (cooldown ke andar) → wahi `otpId`, naya OTP / email nahi
9. Resend save pe `OtpUnconsumed` duplicate -> 429 cooldown message, email nahi; dusra index -> throw
10. Login save pe `OtpUnconsumed` duplicate -> latest `otpId`, email nahi; dusra index -> throw
11. Login (expired unconsumed OTP, cooldown ke baad) -> purana consume, naya OTP; DB mein ek hi unconsumed
12. DB: same user ka dusra unconsumed OTP reject; purana consumed ho to naya allowed

Rate limit (`AuthKeyRateLimitTests`, permits = 2):
1. Same otpId pe 3rd verify -> 429; dusra otpId chalta hai
2. verify + resend same otpId ek bucket
3. Login same userId (case / space alag) -> 3rd 429; register pe per-key limit nahi
4. `ClientPartitionKey`: same /64 -> same key, alag /64 -> alag, IPv4-mapped -> IPv4, null -> `unknown`

## Abhi khule (baad mein)

- Unverified register kabhi expire nahi hota — koi bhi kisi company ka GSTIN apne email se register karke "squat" kar sakta hai.

## 5. Known limits

- Access JWT logout / verify ke baad bhi <= 15 min valid rehta hai (stateless).
- Logout-all nahi hai.
- JWT mein `sid` claim nahi.
- OTP ka daily cap nahi (sirf 60 s cooldown + 5 / ghanta).
- Forgot-password flow is spec mein nahi hai.
- Attempts counter race: do galat guess ek saath aaye to ek hi increment ho sakta hai (login lockout jaisa).
