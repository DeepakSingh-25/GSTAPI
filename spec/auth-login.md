# Spec: POST /api/auth/login (API)

Email **ya** mobile + password se login. Sahi hone par session cookies set hoti hain aur `Me` (user profile) lautta hai.
Email verify nahi hui ho to login nahi hota — naya / pichhla Signup OTP ka `otpId` lautta hai, Web OTP screen kholta hai.

Web side ka spec: `D:\Gst\Web\spec\auth-login.md`
Register spec: `D:\Gst\Api\spec\auth-register.md` (OTP rules, `sentTo` masking wahi hain)

---

## 1. Endpoint

| | |
|---|---|
| Route | `POST /api/auth/login` |
| Auth | `[AllowAnonymous]` |
| Rate limit | `auth` policy — 10 request / min per IP (IPv6 /64 grouped) + per-key 10 / 10 min on normalized `userId` (`AuthKeyRateLimitFilter`, see `spec/auth-otp.md` §1) |
| File | `Features/Auth/LoginCommand.cs` (Command + Validator + Handler) |
| Controller | `AuthController.Login` → `ReturnResult(await Mediator.Send(command, cancellationToken))` |

## 2. Request

```json
{
  "userId": "deepak@demotraders.in",
  "password": "Demo@1234",
  "rememberMe": true
}
```

`userId` mein email ya 10 digit mobile (`9876543210`) dono chalega.

```csharp
public record LoginCommand(string UserId, string Password, bool RememberMe) : IRequest<ApiResponse<LoginResultDto>>;
```

## 3. Normalize (validation se pehle)

- `userId` → `Trim()`; `@` hai to email maano → lowercase. Nahi to mobile maano.
- `password` ko **trim mat karo**.

## 4. Validation → 400 Bad Request

| Field | Rule | Message |
|---|---|---|
| userId | required, max 256, email **ya** `^[6-9][0-9]{9}$` | `Enter a valid email or mobile number` |
| password | required, max 128 | `Password is Required` |
| rememberMe | bool, na aaye to `false` | — |

- Login pe password strength regex **mat lagao** — purane users ke password us rule se pehle ke ho sakte hain.

## 5. Handler steps (isi order mein)

Config (`AuthConfig`, `appsettings.json` → `Auth` section): `MaxFailedLogins = 5`, `LockoutMinutes = 15`,
`RememberMeDays = 30`, `SessionHours = 12`. Time ke liye `TimeProvider` use karo, `DateTime.UtcNow` nahi.

1. **User dhoondo** — `IUserRepository.GetByLoginIdAsync(userId)`: email ho to `Email` se, mobile ho to `MobileNo` se.
   `Company` include karo (Me ke liye). Soft-deleted user nahi milega (global filter).
2. **User nahi mila** → dummy BCrypt verify chalao (response time same rahe, timing se email guess na ho) → **401**.
3. **Locked hai?** `LockoutEnd > now` → **423**, password check **mat karo** (sahi password pe bhi 423).
4. **Password galat** →
   - `FailedLoginCount++`
   - `FailedLoginCount >= 5` → `LockoutEnd = now + 15 min`, `FailedLoginCount = 0`, save → **423**
   - warna save → **401**
5. **Password sahi** → `FailedLoginCount = 0`, `LockoutEnd = null`.
6. **Inactive** (`IsActive = false`) → save → **403**.
   Ye check password ke *baad* hai, taaki galat password wale ko pata na chale ki account inactive hai.
7. **Email verify nahi** (`EmailVerified = false`) → OTP (section 6) → save → **200 `OTPRequired`**. Cookie nahi.
8. **Success** →
   - `LastLoginOn = now`
   - `IAuthSessionService.Create(user, rememberMe)` — refresh token row add karta hai (save nahi)
   - `IUnitOfWork.SaveChangesAsync` — **sirf ek baar**
   - `IAuthSessionService.WriteCookies(session)` — save ke **baad** cookies (section 7)
   - **200 `Success`** + `Me`

Har branch mein `SaveChangesAsync` ek hi baar chale (counter / lockout bhi isi mein save hote hain).

## 6. OTPRequired — OTP kaise mile

- User ka pichhla `Purpose = Signup` OTP dekho: expire nahi hua, `ConsumedOn` null, `Attempts < 5` →
  **wahi `otpId` lautao, naya email mat bhejo** (baar-baar login se email spam na ho; user OTP screen pe Resend dabayega).
- Aisa koi OTP nahi → register jaisa naya `OtpCode` banao (`OtpGenerator.Generate()` / `OtpGenerator.Hash(otp.Id, code)`,
  `Purpose = OtpPurposes.Signup`, 10 min expiry), save ke baad `IEmailSender.SendAsync`. Email fail → log karo, response phir bhi 200.
- `sentTo` — existing `Gst.Application/Common/Masking.MaskEmail` hi use karo (register bhi wahi use karta hai).

## 7. Cookies (sirf `Success` pe)

| Cookie | Value | HttpOnly | Secure | SameSite | Path | Expiry |
|---|---|---|---|---|---|---|
| `gst_access` | JWT (`IJwtTokenService.CreateToken`) | ✔ | ✔ | Strict | `/api` | JWT expiry (`Jwt:Expiry` = 15 min) |
| `gst_refresh` | 64 random bytes, base64url | ✔ | ✔ | Strict | `/api/auth` | rememberMe → `now + 30 din`; warna **session cookie** (Expires nahi) |

- Token **response body mein nahi** jaata — JS cookie padh hi nahi sakta (XSS se chori na ho).
- DB mein refresh token ka sirf `SHA256` hash. Session cookie wale refresh ki DB expiry `now + 12 ghante`.
- Dev mein Web proxy (`localhost:4200/api` → `7100`) ki wajah se same-origin hai, isliye `SameSite=Strict` chalega aur CORS / CSRF token abhi nahi chahiye.
- Cookie likhna ASP.NET ka kaam hai, Application layer ASP.NET reference nahi kar sakta. Isliye:
  - Port: `Gst.Application/Interfaces/IService/IAuthSessionService.cs`
  - Adapter: `Gst.Api/Services/AuthSessionService.cs` (`IHttpContextAccessor` + `IJwtTokenService` + `IRefreshTokenRepository`), Api ke `Program.cs` mein register.
  - `verify-otp` bhi login hone pe yahi service use karega.

### JWT cookie se padhna

`JwtBearerEvents.OnMessageReceived` add karo: `Authorization` header nahi hai to `ctx.Token = ctx.Request.Cookies["gst_access"]`.
Header wala raasta bhi rahe — Swagger ka "Authorize" button kaam karta rahe.

## 8. Response — 200 OK

### Success

```json
{
  "response": {
    "status": "Success",
    "user": {
      "id": "8d2b0c1e-4f6a-4b8e-9a77-1c2d3e4f5a6b",
      "firstName": "Deepak",
      "lastName": "Singh",
      "fullName": "Deepak Singh",
      "email": "deepak@demotraders.in",
      "mobileNo": "9876543210",
      "role": "Owner / Admin",
      "onboardingCompleted": false,
      "companyId": "c1a2b3d4-0000-4e5f-8a9b-112233445566",
      "companyName": "Demo Traders Pvt Ltd",
      "gstin": "08AABCD1234E1Z5"
    }
  },
  "code": 200,
  "description": "OK",
  "error": null
}
```
+ `Set-Cookie: gst_access=...` aur `Set-Cookie: gst_refresh=...`

### OTPRequired

```json
{
  "response": {
    "status": "OTPRequired",
    "otpId": "3f6c1e0a-6a1b-4c39-9f0e-2b7d6f1c9a11",
    "sentTo": "d****k@demotraders.in",
    "expiresOn": "2026-10-31T10:25:00Z"
  },
  "code": 200,
  "description": "OK",
  "error": null
}
```

```csharp
public record MeDto(
    Guid Id, string FirstName, string LastName, string FullName, string Email, string? MobileNo,
    string Role, bool OnboardingCompleted, Guid? CompanyId, string? CompanyName, string? Gstin)
{
    // CompanyName = TradeName ?? LegalName; legacy user (company null) pe teeno company fields null
    public static MeDto From(User user) => ...;
}

public static class LoginStatus
{
    public const string Success = "Success";
    public const string OtpRequired = "OTPRequired";
}

// Null fields JSON mein nahi aayenge ([JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)])
public record LoginResultDto(string Status, MeDto? User = null, Guid? OtpId = null, string? SentTo = null, DateTime? ExpiresOn = null);
```

- `onboardingCompleted = false` → Web onboarding pe bheje, warna dashboard.
- Wizard save: `POST /api/auth/onboarding` (cookie auth). Body `{ firstName, lastName, companyName, businessSize, industry, role }` →
  200 `MeDto` (`onboardingCompleted = true`). Sets `User.JobRole` (never `User.Role`), `Company.BusinessSize/Industry`, `TradeName` (only if it differs from LegalName).
  400 validation, 401 no cookie, 403 inactive, 404 user missing, 409 company not linked. Already completed → 200, no write. Email / mobile / gstin ignored.
- `GET /api/auth/me` bhi yahi `MeDto` lautaye (abhi `UserDto` deta hai) — Web ke paas ek hi `Me` shape rahe.

## 9. Error responses

| Code | Kab | `error` |
|---|---|---|
| 400 | Validation fail | Section 4 ke messages, comma se jude |
| 401 | User nahi mila **ya** password galat (dono ka message **ek hi**) | `Invalid email/mobile or password.` |
| 403 | Password sahi, user inactive | `Your account is inactive. Please contact your administrator.` |
| 423 | 5th galat password pe lock laga | `Too many failed attempts. Your account is locked for 15 minutes.` |
| 423 | Pehle se locked | `Your account is locked. Try again in {n} minutes.` (`n` = bache minute, upar round) |
| 429 | Rate limit | `Too many requests. Please try again later.` |
| 500 | Koi aur exception | `An unexpected error occurred.` |

- `ApiResponse.Fail(msg, HttpStatusCode.Locked)` — check karo ki `BaseController.ReturnResult` 423 ko sahi status code ke saath bhejta hai.
- 401 / 403 / 423 pe koi cookie set nahi.

## 10. Logging

- Failed login, lock lagna, inactive attempt → `Warning`, sirf `UserId` (Guid) ke saath.
- `userId` (email / mobile), password, OTP, token — **kabhi log nahi** (Information pe PII nahi, security rule).

## 11. Tables / DB

`tb_users` mein `FailedLoginCount`, `LockoutEnd`, `LastLoginOn` pehle se hain — naya column nahi.

Naya table `tb_refresh_tokens` (`BaseEntity` se):

| Column | Type |
|---|---|
| UserId | `uniqueidentifier`, FK → `tb_users.Id` |
| TokenHash | `nvarchar(64)`, unique index |
| ExpiresOn | `datetime2` |
| RememberMe | `bit` |
| RevokedOn | `datetime2` null |
| ReplacedByTokenId | `uniqueidentifier` null (refresh rotation ke liye) |
| CreatedByIp | `nvarchar(45)` null |

Index: `(UserId, RevokedOn)` — logout-all / cleanup ke liye.
Migration: `AuthLogin` → `dbscript/NNN_AuthLogin.sql` (idempotent), owner chalayega.

## 12. Existing code mein badlav

| Jagah | Abhi | Naya |
|---|---|---|
| `LoginCommand` | `(Email, Password)` | `(UserId, Password, RememberMe)` |
| Validator | `EmailAddress()` | email ya mobile (section 4) |
| Not verified | 403 + `"OTPRequired"` error | 200 + `status: OTPRequired` + otpId / sentTo |
| Response | `AuthResponse(Token, ExpiresOn, UserDto)` body mein | `LoginResultDto`, token sirf cookie mein |
| Lockout | nahi | section 5 step 3–4 |
| Rate limit | nahi | `auth` policy |
| `IUserRepository` | `GetByEmailAsync` | `GetByLoginIdAsync` (+ Company include) |
| `GetMeQuery` | `UserDto` | `MeDto` |
| `JwtBearerEvents` | header only | + `OnMessageReceived` cookie |
| 401 message | `Invalid email or password.` | `Invalid email/mobile or password.` |

`AuthResponse` aur `UserDto` kahin aur use na ho to hata do.

## 13. Test cases

1. Sahi email + password, verified → 200 `Success`, `user` bhara, dono cookies HttpOnly/Secure/Strict; body mein token nahi
2. Sahi **mobile** + password → 200 `Success`
3. `DEEPAK@DemoTraders.in` → login ho jaaye (lowercase)
4. Email exist nahi → 401; email sahi password galat → 401; **dono ka message aur status same**
5. 4 baar galat → 401, `FailedLoginCount = 4`; 5th galat → 423, `LockoutEnd ≈ now + 15 min`
6. Lock ke dauraan **sahi** password → 423, `FailedLoginCount` na badhe
7. 15 min baad (`FakeTimeProvider`) sahi password → 200, counter 0
8. 3 galat ke baad sahi → 200, `FailedLoginCount = 0`
9. Inactive + sahi password → 403; inactive + galat password → 401 (403 nahi)
10. Not verified + sahi password → 200 `OTPRequired`, naya OtpCode bana, email gaya, koi cookie nahi
11. Not verified, valid OTP pehle se hai → wahi `otpId`, naya OtpCode / email nahi
12. `rememberMe = false` → `gst_refresh` mein `Expires` nahi; `true` → 30 din
13. Login ke baad `GET /api/auth/me` sirf cookie ke saath (header nahi) → 200
14. Ek minute mein 11 login → 11th pe 429
15. Logs mein email / mobile / password na ho

## 14. Open decisions

1. **423 se account ka hona pata chalta hai** — 423 sirf existing account pe aata hai, to koi bhi 5 baar try karke jaan sakta hai ki email registered hai.
   Requirement mein 423 maanga hai isliye rakha. Chahein to lock pe bhi 401 hi bhejo (message same), lock andar chupchaap lage.
2. **Lock se DoS** — koi bhi kisi ka email daal ke 5 galat password se 15 min ke liye bahar kar sakta hai. IP rate limit thoda rokta hai.
   Baad mein: lock IP + account combo pe, ya lock ki jagah CAPTCHA.
3. **Counter race** — do galat request ek saath aayein to count ek hi badh sakta hai. Abhi chalega; strict chahiye to
   `UPDATE ... SET FailedLoginCount = FailedLoginCount + 1` (atomic) repository method se.

## Depends on / next

- `POST /api/auth/refresh` — `gst_refresh` se naya `gst_access` (rotation: purana revoke, naya bane). Iske bina 15 min baad user logout ho jaayega.
- `POST /api/auth/logout` — refresh revoke + dono cookies delete.
- `POST /api/auth/verify-otp` — Signup OTP sahi hone pe `EmailVerified = true` aur yahi session/cookies (request mein `rememberMe` bhi aaye).
- Web side: `D:\Gst\Web\spec\auth-login.md` — `LoginResponse` / `Me` interface, login screen wiring.
