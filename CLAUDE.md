# Gst API — project guide

.NET 10 Web API, Clean Architecture + MediatR (CQRS) + Repository + Unit of Work.
EF Core 10 on SQL Server (write), Dapper (fast read), JWT auth, Serilog, FluentValidation.
DB: shared SQL Server `192.168.120.17`, database `Gst`, SQL login `developer` (password set by the owner, never committed).
Solution: `Gst.slnx`. Frontend: `../Web` (Angular, camelCase JSON — keep ASP.NET default).

## Layers (dependency direction: Api → Persistence → Application → Domain)

| Project | Holds |
|---|---|
| `src/Core/Gst.Domain` | `Entities/` (inherit `BaseEntity`), `Common/` (`ApiResponse<T>`, `DataGrid`), `Constants/` (`Roles`, `Policies`, `AppClaims`), `Configurations/`, `Enums/`. No packages. |
| `src/Core/Gst.Application` | `Features/<Name>/` — Command/Query + Validator + Handler in one file, DTOs. `Interfaces/` (ports: `IRepository<T>`, `IUnitOfWork`, services). `Behaviors/` (Logging → Validation → Caching). No EF Core here. |
| `src/Infrastructure/Gst.Persistence` | `Context/` (`ApplicationDbContext` = `IUnitOfWork`, audit + soft-delete filter), `Configurations/` (`IEntityTypeConfiguration<T>`), `Repository/`, `Service/` (BCrypt, JWT, CurrentUser). |
| `src/Presentation/Gst.Api` | `Controllers/` (inherit `BaseController`), `Middleware/` (global exception → `ApiResponse`), `Extensions/` (JWT, policies, CORS, Swagger). |

## Request flow

```
Controller → Mediator → LoggingBehavior → ValidationBehavior → CachingBehavior → Handler
          → IXxxRepository (track changes) → IUnitOfWork.SaveChangesAsync → ApiResponse<T>
Errors anywhere → ExceptionHandlingMiddleware → ApiResponse (400 validation / 404 / 500)
401 / 403 → JwtBearerEvents → ApiResponse
```

## Golden path for a new feature (copy `Features/Auth`)

1. Entity → `Gst.Domain/Entities/<Name>.cs : BaseEntity`.
2. Table config → `Gst.Persistence/Configurations/<Name>Configuration.cs`; add `DbSet` to `ApplicationDbContext`.
3. Port → `Gst.Application/Interfaces/IRepository/I<Name>Repository.cs : IRepository<Name>` (only extra methods).
4. Adapter → `Gst.Persistence/Repository/<Name>Repository.cs : Repository<Name>`; register in `PersistenceServiceRegistration.cs`.
5. Use case → `Gst.Application/Features/<Name>/<Verb><Name>Command.cs` (record + `AbstractValidator` + handler) and DTOs. Return `ApiResponse<TDto>` — never an entity.
6. Endpoint → `Gst.Api/Controllers/<Name>Controller.cs : BaseController`, one `Mediator.Send(..., cancellationToken)` per action, `ReturnResult(...)`.
7. Migration → `dotnet ef migrations add <Name> -p src/Infrastructure/Gst.Persistence -s src/Presentation/Gst.Api -o Migrations`, then
   `dotnet ef migrations script --idempotent ... -o dbscript/NNN_<Name>.sql`. Never run `database update` against the shared server — hand the `.sql` to the owner.
8. `dotnet build Gst.slnx` — 0 errors.

## Rules

- Handlers return business failures as `ApiResponse.Fail(msg, code)` (Conflict, NotFound…); throw only for unexpected errors.
- Repositories never call `SaveChanges`; the handler calls `IUnitOfWork.SaveChangesAsync` once.
- Always pass `CancellationToken` controller → handler → repository.
- Controllers are `[Authorize]` by default (from `BaseController`); mark public actions `[AllowAnonymous]` explicitly.
- Secrets (`ConnectionStrings`, `Jwt:Key`) stay placeholders in `appsettings.json`; real values via user-secrets / env vars.
- Comments: Hinglish/English, short.

## Databases — Claude never writes to one

`DefaultConnection` points at the **shared SQL Server 192.168.120.17** (`Gst`). There is no
design-time factory, so `dotnet ef database update` would migrate that shared DB.
`.claude/hooks-ps/db-write-guard.ps1` denies DB writes for the main session and every agent.
Hand back a reviewed idempotent `dbscript/NNN_<Name>.sql` plus the exact command; the owner runs it.

## Tooling in `.claude/`

- Project agents (tailored to this repo): `backend-architect` (opus), `backend-coder`,
  `backend-code-reviewer`, `backend-security-auditor` (opus), `backend-tester`,
  `database-migration-specialist`.
- Vendored dotnet-claude-kit: 47 skills, 10 generic agents, `rules/`, `templates/`, bash `hooks/` —
  see `.claude/VENDORED.md`.
- Hooks: every agent spawn asks for approval (`gate-agent.ps1`); DB writes denied (`db-write-guard.ps1`);
  session-start reminder; statusline.
- **Precedence:** where a vendored kit rule or skill disagrees with *this file* or
  `.claude/rules/architecture.md` (it does on repositories, MediatR, Minimal APIs, CPM, `Result<T>`),
  **this repo wins**. Don't refactor toward the kit's preferences unless the user asks.

## Run

```bash
dotnet build Gst.slnx
dotnet run --project src/Presentation/Gst.Api --launch-profile https   # https://localhost:7100/swagger
```
VS Code: open `D:\Gst\Api`, press F5.
