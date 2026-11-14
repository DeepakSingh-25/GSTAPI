# Spec: POST /api/auth/refresh + POST /api/auth/logout + GET /api/auth/me inactive rule (API)

Refresh token rotation, logout, aur `/me` pe inactive user ka rule.

Login spec: `spec/auth-login.md` (cookies table, `MeDto`). OTP spec: `spec/auth-otp.md`.

---

## 1. Endpoints

| Route | Auth | Rate limit | File |
|---|---|---|---|
| `POST /api/auth/refresh` | `[AllowAnonymous]` | `refresh` (30 / min / IP) | `Features/Auth/RefreshSessionCommand.cs` |
| `POST /api/auth/logout` | `[AllowAnonymous]` (access token expire hone ke baad bhi chalna chahiye) | koi nahi | `Features/Auth/LogoutCommand.cs` |
| `GET /api/auth/me` | `[Authorize]` | — | `Features/Auth/GetMeQuery.cs` |

Config (`AuthConfig` -> `Auth`): `RefreshReuseGraceSeconds = 30`. `RateLimitPolicies.Refresh = "refresh"`.

Shared: `TokenHasher.Hash(raw)` (`Gst.Application/Common`) = SHA256 lowercase hex of the raw base64url string — login ka `AuthSessionService` aur refresh / logout dono yahi use karte hain.

Port additions (`IAuthSessionService`): `string? GetRefreshToken()` (cookie `gst_refresh`), `void ClearCookies()` (dono cookies delete,
**wahi** Path / Secure / HttpOnly / SameSite jo likhte waqt the, warna browser delete nahi karega), `CreateAsync(..., refreshExpiresOn)` optional absolute expiry,
`AuthSession.RefreshTokenId`.

## 2. refresh

Request: koi body nahi. Cookie `gst_refresh` se token aata hai.

```csharp
public record RefreshSessionCommand : IRequest<ApiResponse<MeDto>>;
```

### Handler steps (isi order mein)

1. `raw = sessions.GetRefreshToken()`; null / empty -> `ClearCookies`, **401** `Session expired. Please log in again.`
2. Row = `GetByHashAsync(TokenHasher.Hash(raw))` (tracked, User + Company). Nahi mila -> `ClearCookies`, 401 (same message).
3. `row.RevokedOn != null`:
   - `now - RevokedOn <= 30 s` **aur** `ReplacedByTokenId != null` -> 401, **cookies clear NAHI** (parallel tab ne naya cookie already set kar diya hoga), revoke-all nahi.
   - warna (replay / chori) -> user ke saare active tokens `RevokedOn = now`, save ek baar, `ClearCookies`, 401.
     Warning log: `Refresh token reuse detected. UserId {UserId}`.
4. `row.ExpiresOn <= now` -> `ClearCookies`, 401.
5. User null ya `!IsActive` -> `row.RevokedOn = now`, save, `ClearCookies`, 401.
6. Rotate: `row.RevokedOn = now`; `CreateAsync(user, row.RememberMe, refreshExpiresOn: row.ExpiresOn)` (**absolute** expiry, kabhi extend nahi);
   `row.ReplacedByTokenId = session.RefreshTokenId`; save **ek baar**; `WriteCookies(session)` -> **200** `MeDto`.
   `LastLoginOn` ko haath nahi lagana.

### Response

```json
{ "response": { "id": "...", "firstName": "Deepak", "...": "MeDto" }, "code": 200, "description": "OK", "error": null }
```
+ naye `gst_access` aur `gst_refresh` cookies.

### Errors

| Code | Kab | `error` |
|---|---|---|
| 401 | Cookie nahi / unknown / revoked / reuse / expired / user inactive | `Session expired. Please log in again.` |
| 429 | Rate limit | `Too many requests. Please try again later.` |

## 3. logout

```csharp
public record LogoutCommand : IRequest<ApiResponse<string>>;
```

1. `raw = GetRefreshToken()`; ho, row mile aur revoked na ho -> `RevokedOn = now`, save ek baar.
2. Hamesha `ClearCookies`; hamesha **200** `Logged out.` — token tha ya nahi, kabhi reveal nahi.

## 4. GET /api/auth/me

User mila par `!IsActive` -> **403** `Your account is inactive. Please contact your administrator.` (pehle MeDto de deta tha).

## 5. Test cases

refresh:
1. Valid cookie -> 200 `MeDto`, naye cookies, purana row revoked, `ReplacedByTokenId` = naya row, naye row ki `ExpiresOn` == purane ki (extend nahi), `RememberMe` same
2. Cookie nahi -> 401 + cookies clear
3. Unknown token -> 401 + cookies clear
4. Revoked, 30 s ke andar, replaced -> 401, cookies clear nahi, baaki tokens revoke nahi
5. Revoked, 30 s ke baad -> 401, user ke saare active tokens revoked, cookies clear, Warning log (UserId)
6. Revoked par `ReplacedByTokenId == null` (logout wala) -> reuse path (revoke-all)
7. Expired row -> 401 + clear
8. User inactive -> row revoked, 401, clear
9. `LastLoginOn` unchanged; save ek baar
10. 31 request / min -> 429

logout:
1. Valid cookie -> 200, row revoked, cookies clear; uske baad refresh -> 401
2. Cookie nahi / unknown / already revoked -> 200, koi save nahi, cookies clear
3. Bina access token ke chalta hai

me:
1. Active -> 200; inactive -> 403; deleted -> 404

## 6. Known limits

- Access JWT logout ke baad bhi <= 15 min valid rehta hai (stateless; revoke sirf refresh token ka).
- Logout-all (saare devices) nahi hai.
- JWT mein `sid` claim nahi, to access token ko session se bind nahi kar sakte.
- OTP ka daily cap nahi.
- Forgot-password flow covered nahi.
- Do parallel refresh bilkul ek saath aayein to dono row padh sakte hain (concurrency token nahi) — 30 s grace sirf sequential-ish case cover karta hai.
