# Agent tooling in `Gst/Api/.claude/` — provenance

Copied on 2026-10-30 from the MyApp API checkout (`D:\Working\work\API\.claude`), which vendored it
from upstream. Same layout as that repo so both projects work the same way.

## Vendored (do not edit — project rules go in `../CLAUDE.md`)

| Component | Upstream | Version | Licence |
|---|---|---|---|
| `skills/` (47), `agents/` (10 generic: `api-designer`, `build-error-resolver`, `code-reviewer`, `devops-engineer`, `dotnet-architect`, `ef-core-specialist`, `performance-analyst`, `refactor-cleaner`, `security-auditor`, `test-engineer`), `rules/` (except `architecture.md`), `hooks/` (bash), `templates/` (5) | https://github.com/codewithmukesh/dotnet-claude-kit | 0.12.0, commit `23300897f4d1a276bc475e373ecfe886e43dd7c5` | MIT — `LICENSE-dotnet-claude-kit` |

Kit skills use `MyApp` as a generic example namespace — that is upstream sample text, not this repo.

### The kit is opinionated in ways this repo is not
The kit prefers Minimal APIs, vertical slices, no repository pattern, no MediatR, `Result<T>`,
file-scoped namespaces and CPM. Gst API uses controllers + MediatR pipeline + generic repository +
Unit of Work + `ApiResponse<T>` by choice. `CLAUDE.md` states the precedence: **project conventions
win**. Use the kit skills for techniques (EF Core, auth, testing, security scanning, caching,
resilience), not as a mandate to restructure.

## Written for this repo (edit freely)

- `agents/backend-architect.md`, `backend-coder.md`, `backend-code-reviewer.md`,
  `backend-security-auditor.md`, `backend-tester.md`, `database-migration-specialist.md` —
  rewritten from the MyApp versions against what exists in Gst API (`src/` layout, `Features/<Name>/`,
  `IRepository<T>` + `IUnitOfWork`, FluentValidation, `ApiResponse<T>`, camelCase JSON, no tests,
  shared SQL Server 192.168.120.17).
- `rules/architecture.md` — rewritten for Gst layers (alwaysApply).
- `hooks-ps/` — `gate-agent.ps1` (every agent spawn asks first), `db-write-guard.ps1` (denies DB
  writes), `session-start.ps1` (team-flow reminder), `statusline.ps1`. Wired in `settings.json`.
- `settings.json`, `launch.json`, this file, `../CLAUDE.md`, `../.mcp.json`.

## Bash hooks
The kit's `hooks/*.sh` are copied but **not wired**. To enable auto-format on edit etc., add them
to `settings.json` using `hooks/hooks.json` as the template (needs Git Bash).

## Roslyn MCP server (optional)
`../.mcp.json` expects `cwm-roslyn-navigator` on PATH. Several kit skills/rules use it
(`find_symbol`, `get_diagnostics`, …). Install once per machine:

```
dotnet tool install -g CWM.RoslynNavigator
```
