# Spec: POST /api/auth/register (API)

Naya account banata hai: ek **Company** + uska pehla **User** (`Owner / Admin`), aur email pe Signup OTP bhejta hai.
Login session (cookies) yahan **nahi** banta — wo `POST /api/auth/verify-otp` ke baad banega.

Web side ka spec: `D:\Gst\Web\spec\auth-register.md`

---

## 1. Endpoint

| | |
|---|---|
| Route | `POST /api/auth/register` |
| Auth | `[AllowAnonymous]` |
| Rate limit | `auth` policy — 10 request / min per IP |
| File | `Features/Auth/RegisterCommand.cs` (Command + Validator + Handler ek file mein) |
| Controller | `AuthController.Register` → `ReturnResult(await Mediator.Send(command, cancellationToken))` |

## 2. Request

```json
{
  "companyName": "Demo Traders Pvt Ltd",
  "gstin": "08AABCD1234E1Z5",
  "registrationType": "Regular",
  "state": "Rajasthan",
  "firstName": "Deepak",
  "lastName": "Singh",
  "mobileNo": "9876543210",
  "email": "deepak@demotraders.in",
  "password": "Demo@1234"
}
```

```csharp
public record RegisterCommand(
    string CompanyName, string Gstin, string RegistrationType, string State,
    string FirstName, string LastName, string MobileNo, string Email, string Password)
    : IRequest<ApiResponse<OtpSentDto>>;
```

`confirmPassword` aur `acceptTerms` sirf Web pe check hote hain — API tak nahi aate.

## 3. Normalize (validation se pehle)

- Saare text fields `Trim()`
- `email` → lowercase
- `gstin` → uppercase

## 4. Validation → 400 Bad Request

| Field | Rule | Message |
|---|---|---|
| companyName | required, max 100 | `Company Name is Required` |
| gstin | required, regex `^[0-3][0-9][A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$` | `Enter a valid GSTIN` |
| registrationType | `Regular` / `Composition` / `SEZ` / `Unregistered` | `Select a valid Registration Type` |
| state | required, state map mein ho | `Select a valid State` |
| gstin + state | GSTIN ke pehle 2 digit = state code | `GSTIN does not belong to the selected State` |
| firstName | required, max 50 | `First Name is Required` |
| lastName | required, max 50 | `Last Name is Required` |
| mobileNo | `^[6-9][0-9]{9}$` | `Enter a valid 10 digit mobile number` |
| email | valid email, max 256 | `Enter a valid Email` |
| password | `^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&#])[A-Za-z\d@$!%*?&#]{8,}$` | `Password must have 8+ characters with upper, lower, number and special character` |

### State code map

Web dropdown ke 12 states:

| State | Code | State | Code |
|---|---|---|---|
| Punjab | 03 | Madhya Pradesh | 23 |
| Haryana | 06 | Gujarat | 24 |
| Delhi | 07 | Maharashtra | 27 |
| Rajasthan | 08 | Karnataka | 29 |
| Uttar Pradesh | 09 | Tamil Nadu | 33 |
| West Bengal | 19 | Telangana | 36 |

API mein saare 37 states / UT ka map rakhein (Web list baad mein badh sakti hai).

## 5. Duplicate check → 409 Conflict (isi order mein)

| Check | Message |
|---|---|
| Email already hai | `Email is already registered. Please log in.` |
| Mobile already hai | `Mobile number is already registered.` |
| GSTIN already hai | `A company with this GSTIN is already registered.` |

- Pehle sign-up karke OTP verify na kiya ho, tab bhi 409. User Login karega → login `OTPRequired` dega → wahan se verify.
- Do request ek saath aayein to unique index fail hoga (`SqlException` 2601 / 2627) — use pakad ke 409 lautana, 500 nahi.

## 6. Handler steps

1. Validation (ValidationBehavior) + duplicate checks
2. **Company** banao

   | Column | Value |
   |---|---|
   | LegalName | companyName |
   | Gstin | gstin |
   | Pan | `gstin.Substring(2, 10)` |
   | RegistrationType | registrationType |
   | State | state |
   | StateCode | `gstin.Substring(0, 2)` |

3. **User** banao

   | Column | Value |
   |---|---|
   | CompanyId | nayi company ki Id |
   | FirstName / LastName | request se |
   | FullName | `"{firstName} {lastName}"` |
   | Email | email (lowercase) |
   | MobileNo | mobileNo |
   | PasswordHash | `IPasswordHasher.Hash(password)` (BCrypt) |
   | Role | `Roles.Owner` = `Owner / Admin` |
   | EmailVerified | `false` |
   | OnboardingCompleted | `false` |
   | IsActive | `true` |

4. **OtpCode** banao

   | Column | Value |
   |---|---|
   | Id | naya Guid — yahi `otpId` hai |
   | UserId / Email | naye user ke |
   | Purpose | `Signup` |
   | Code | 6 digit — `RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6")` |
   | CodeHash | `SHA256(otpId + code)` hex. **Plain code DB mein kabhi nahi** |
   | ExpiresOn | `UtcNow + 10 min` |
   | Attempts | 0 |

5. `IUnitOfWork.SaveChangesAsync` — **sirf ek baar**, teeno rows ek saath.
6. Save ke **baad** `IEmailSender.SendAsync(email, subject, body)`:
   - Subject: `Your GST Invoice verification code`
   - Body: `Your code is 123456. It is valid for 10 minutes. Do not share it with anyone.`
   - Email fail ho → error log karo, response phir bhi **201** (user OTP modal se Resend karega).
   - Development mein `Smtp:Host` khali ho → email ki jagah code log mein print karo.

## 7. Response — 201 Created

```json
{
  "response": {
    "otpId": "3f6c1e0a-6a1b-4c39-9f0e-2b7d6f1c9a11",
    "sentTo": "d****k@demotraders.in",
    "expiresOn": "2026-10-30T10:25:00Z"
  },
  "code": 201,
  "description": "Created",
  "error": null
}
```

```csharp
// resend-otp aur forgot-password bhi yahi DTO lautayenge
public record OtpSentDto(Guid OtpId, string SentTo, DateTime ExpiresOn);
```

- **sentTo masking:** naam ka pehla + aakhri akshar, beech mein `*` (kam se kam 2), domain poora.
  `deepak@demotraders.in` → `d****k@demotraders.in`
- Is response mein koi cookie / token nahi.

## 8. Error responses

| Code | Kab | `error` |
|---|---|---|
| 400 | Validation fail | Saare messages comma se jude (ValidationBehavior + ExceptionHandlingMiddleware) |
| 409 | Duplicate | Section 5 ka message |
| 429 | Rate limit | `Too many requests. Please try again later.` |
| 500 | Koi aur exception | `An unexpected error occurred.` |

## 9. Tables / DB constraints

Naya:

- `tb_companies` — LegalName, TradeName, Gstin, Pan, RegistrationType, State, StateCode, BusinessSize, Industry,
  AddressLine1, AddressLine2, City, Pincode, Email, Phone, Website, BankName, AccountNo, Ifsc, Branch,
  TermsAndConditions + audit columns (`BaseEntity`)
- `tb_otp_codes` — UserId, Email, Purpose, CodeHash, ExpiresOn, Attempts, VerifiedOn, ConsumedOn + audit columns

`tb_users` mein naye columns: CompanyId, FirstName, LastName, MobileNo, EmailVerified, OnboardingCompleted,
JobRole, FailedLoginCount, LockoutEnd, PasswordChangedOn.

Indexes:

- `tb_companies.Gstin` — unique, filtered `WHERE IsDeleted = 0`
- `tb_users.Email` — unique, filtered `WHERE IsDeleted = 0`
- `tb_users.MobileNo` — unique, filtered `WHERE MobileNo IS NOT NULL AND IsDeleted = 0`
- `tb_users.CompanyId` — FK → `tb_companies.Id`
- `tb_otp_codes.UserId` — FK → `tb_users.Id`
- `tb_otp_codes (Email, Purpose, CreatedOn)` — resend limit check ke liye

Saari Id `uniqueidentifier` (Guid).

## 10. Existing code mein badlav

- `Roles` constants → `Owner / Admin`, `Accountant`, `Sales Executive`, `Store Keeper`
- `AdminOnly` policy → role `Owner / Admin`
- Purana `RegisterCommand(FullName, Email, Password)` is spec wale se replace

## 11. Test cases

1. Valid request → 201; Company + User + OtpCode bane; `EmailVerified = false`; plain OTP DB mein nahi
2. Email duplicate → 409; mobile duplicate → 409; GSTIN duplicate → 409
3. GSTIN `27...` + state `Rajasthan` → 400
4. Kamzor password → 400
5. `DEEPAK@X.IN` → DB mein `deepak@x.in`; lowercase GSTIN → DB mein uppercase
6. Email sender exception → phir bhi 201 + error log
7. Ek minute mein 11 request → 11th pe 429

## 12. Open decision

Abhi GSTIN hamesha required hai, `Unregistered` type ke liye bhi. Unregistered business ke paas GSTIN hota hi nahi.
Suggestion: `Unregistered` pe GSTIN optional — tab `Pan` / `StateCode` state se lo, aur GSTIN unique index mein
`Gstin IS NOT NULL` condition jodo. Web aur API dono mein ek jaisa rakhna.

## Depends on / next

- `POST /api/auth/verify-otp` aur `POST /api/auth/resend-otp` — inke bina sign-up flow poora nahi hoga.
